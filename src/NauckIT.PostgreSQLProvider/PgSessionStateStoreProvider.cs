//
// Copyright � 2007 - 2013 Nauck IT KG		http://www.nauck-it.de
//
// Authors:
//	Daniel Nauck		<d.nauck(at)nauck-it.de>
//	Christ Akkermans	<c.akkermans(at)vereyon.nl>
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.


using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.SessionState;
using NauckIT.PostgreSQLProvider.ProviderSettings;
using NauckIT.PostgreSQLProvider.ProviderSettings.Tables;
using Npgsql;
using NpgsqlTypes;

namespace NauckIT.PostgreSQLProvider {
    public class PgSessionStateStoreProvider : SessionStateStoreProviderBase {
        private System.Timers.Timer m_expiredSessionDeletionTimer;
        private SessionStateStoreSettings settings;
        private bool m_enableExpireCallback = false;
        private SessionStateItemExpireCallback m_expireCallback = null;

        /// <summary>
        /// System.Configuration.Provider.ProviderBase.Initialize Method
        /// </summary>
        public override void Initialize(string name, NameValueCollection config) {
            // Initialize values from web.config.
            if (config == null)
                throw new ArgumentNullException("config", Properties.Resources.ErrArgumentNull);

            if (string.IsNullOrEmpty(name))
                name = Properties.Resources.SessionStoreProviderDefaultName;

            if (string.IsNullOrEmpty(config["description"])) {
                config.Remove("description");
                config.Add("description", Properties.Resources.SessionStoreProviderDefaultDescription);
            }

            // Initialize the abstract base class.
            base.Initialize(name, config);
            settings = new SessionStateStoreSettings(config);

            // Should automatic session garbage collection be turned on?
            bool enableExpiredSessionAutoDeletion =
                Convert.ToBoolean(
                    ConfigurationParser.GetConfigValue(config["enableExpiredSessionAutoDeletion"], "false"),
                    CultureInfo.InvariantCulture);

            if (!enableExpiredSessionAutoDeletion)
                return;

            m_enableExpireCallback =
                Convert.ToBoolean(ConfigurationParser.GetConfigValue(config["enableSessionExpireCallback"], "false"),
                    CultureInfo.InvariantCulture);

            // Load session garbage collection configuration and setup garbage collection interval timer
            double expiredSessionAutoDeletionInterval =
                Convert.ToDouble(
                    ConfigurationParser.GetConfigValue(config["expiredSessionAutoDeletionInterval"], "1800000"),
                    CultureInfo.InvariantCulture); //default: 30 minutes

            m_expiredSessionDeletionTimer = new System.Timers.Timer(expiredSessionAutoDeletionInterval);
            m_expiredSessionDeletionTimer.Elapsed +=
                new System.Timers.ElapsedEventHandler(ExpiredSessionDeletionTimer_Elapsed);
            m_expiredSessionDeletionTimer.Enabled = true;
            m_expiredSessionDeletionTimer.AutoReset = true;
        }

        /// <summary>
        /// SessionStateStoreProviderBase members
        /// </summary>

        #region SessionStateStoreProviderBase members
        public override void Dispose() {
            if (m_expiredSessionDeletionTimer == null)
                return;

            // cleanup timer
            m_expiredSessionDeletionTimer.Stop();
            m_expiredSessionDeletionTimer.Dispose();
            m_expiredSessionDeletionTimer = null;
        }

        /// <summary>
        /// SessionStateProviderBase.InitializeRequest
        /// </summary>
        public override void InitializeRequest(HttpContext context) {
        }

        /// <summary>
        /// SessionStateProviderBase.EndRequest
        /// </summary>
        public override void EndRequest(HttpContext context) {
        }

        /// <summary>
        /// SessionStateProviderBase.CreateNewStoreData
        /// </summary>
        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout) {
            return new SessionStateStoreData(new SessionStateItemCollection(),
                SessionStateUtility.GetSessionStaticObjects(context), timeout);
        }

        /// <summary>
        /// SessionStateProviderBase.CreateUninitializedItem
        /// </summary>
        public override void CreateUninitializedItem(HttpContext context, string id, int timeout) {
            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                try {
                    dbConn.Open();

                    /* 
                     * Bug #16: CreateUninitializedItem(...) throws ProviderException / duplicate key violation
                     * Url: http://dev.nauck-it.de/issues/show/16
                     * 
                     * PostgreSQL is missing a InsertOrUpdate method so
                     * try to insert first. If the insert failed due an already existing session id try
                     * to update the database record. if this also fails, e.g. due session cleanup tasks, try
                     * insert a new record again.
                     * Try the whole process 10 times before give up.
                     * 
                     */
                    int failureCount = 0;
                    while (failureCount <= 10) {
                        // try insert new item, on success leave while
                        if (CreateUninitializedItemTryInsert(id, timeout, dbConn))
                            return; // success, leave

                        // if insert fails due already existing session id, try update
                        if (CreateUninitializedItemTryUpdate(id, timeout, dbConn))
                            return; // success, leave

                        // try again
                        failureCount++;
                    }
                }
                catch (Exception e) {
                    Trace.WriteLine(e.ToString());
                    throw new ProviderException(Properties.Resources.ErrOperationAborted);
                }
                finally {
                    if (dbConn != null)
                        dbConn.Close();
                }
            }
        }

        /// <summary>
        /// SessionStateProviderBase.GetItem
        /// </summary>
        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked,
            out TimeSpan lockAge, out object lockId, out SessionStateActions actions) {
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        /// <summary>
        /// SessionStateProviderBase.GetItemExclusive
        /// </summary>
        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked,
            out TimeSpan lockAge, out object lockId, out SessionStateActions actions) {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        /// <summary>
        /// SessionStateProviderBase.ReleaseItemExclusive
        /// </summary>
        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId) {
            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                        "UPDATE {0} SET {1} = @Expires, {2} = @Locked WHERE {3} = @SessionId AND {4} = @ApplicationName AND {5} = @LockId",
                        properties.TableName, properties.Expires, properties.Locked, properties.SessionID, properties.ApplicationName, properties.LockId);

                    dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value =
                        DateTime.Now.Add(settings.m_config.Timeout);
                    dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = false;
                    dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                    dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                        settings.m_applicationName;
                    dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = lockId;

                    NpgsqlTransaction dbTrans = null;

                    try {
                        dbConn.Open();
                        PrepareStatementIfEnabled(dbCommand);

                        dbTrans = dbConn.BeginTransaction();

                        dbCommand.ExecuteNonQuery();

                        // Attempt to commit the transaction
                        dbTrans.Commit();
                    }
                    catch (NpgsqlException e) {
                        Trace.WriteLine(e.ToString());

                        if (dbTrans != null) {
                            try {
                                // Attempt to roll back the transaction
                                Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                                dbTrans.Rollback();
                            }
                            catch (NpgsqlException re) {
                                // Rollback failed
                                Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                                Trace.WriteLine(re.ToString());
                            }
                        }

                        throw new ProviderException(e.Message);
                    }
                    finally {
                        if (dbTrans != null)
                            dbTrans.Dispose();

                        if (dbConn != null)
                            dbConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// SessionStateProviderBase.RemoveItem
        /// </summary>
        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item) {
            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                        "DELETE FROM {0} WHERE {1} = @SessionId AND {2} = @ApplicationName AND {3} = @LockId",
                        properties.TableName, properties.SessionID, properties.ApplicationName, properties.LockId);

                    dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                    dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                        settings.m_applicationName;
                    dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = lockId;

                    NpgsqlTransaction dbTrans = null;

                    try {
                        dbConn.Open();
                        PrepareStatementIfEnabled(dbCommand);

                        dbTrans = dbConn.BeginTransaction();

                        dbCommand.ExecuteNonQuery();

                        // Attempt to commit the transaction
                        dbTrans.Commit();
                    }
                    catch (Exception e) {
                        Trace.WriteLine(e.ToString());

                        if (dbTrans != null) {
                            try {
                                // Attempt to roll back the transaction
                                Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                                dbTrans.Rollback();
                            }
                            catch (Exception re) {
                                // Rollback failed
                                Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                                Trace.WriteLine(re.ToString());
                            }
                        }

                        throw new ProviderException(e.Message);
                    }
                    finally {
                        if (dbTrans != null)
                            dbTrans.Dispose();

                        if (dbConn != null)
                            dbConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// SessionStateProviderBase.ResetItemTimeout
        /// </summary>
        public override void ResetItemTimeout(HttpContext context, string id) {
            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                        "UPDATE {0} SET {1}= @Expires WHERE {2} = @SessionId AND {3} = @ApplicationName",
                        properties.TableName, properties.Expires, properties.SessionID, properties.ApplicationName);

                    dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value =
                        DateTime.Now.Add(settings.m_config.Timeout);
                    dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                    dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                        settings.m_applicationName;

                    NpgsqlTransaction dbTrans = null;

                    try {
                        dbConn.Open();
                        PrepareStatementIfEnabled(dbCommand);

                        dbTrans = dbConn.BeginTransaction();

                        dbCommand.ExecuteNonQuery();

                        // Attempt to commit the transaction
                        dbTrans.Commit();
                    }
                    catch (Exception e) {
                        Trace.WriteLine(e.ToString());

                        if (dbTrans != null) {
                            try {
                                // Attempt to roll back the transaction
                                Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                                dbTrans.Rollback();
                            }
                            catch (Exception re) {
                                // Rollback failed
                                Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                                Trace.WriteLine(re.ToString());
                            }
                        }

                        throw new ProviderException(e.Message);
                    }
                    finally {
                        if (dbTrans != null)
                            dbTrans.Dispose();

                        if (dbConn != null)
                            dbConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// SessionStateProviderBase.SetAndReleaseItemExclusive
        /// </summary>
        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item,
            object lockId, bool newItem) {
            // Serialize the SessionStateItemCollection as a string
            string serializedItems = Serialize((SessionStateItemCollection) item.Items);

            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                using (NpgsqlCommand dbCommand = dbConn.CreateCommand(),
                    delCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    if (newItem) {
                        // Delete existing expired session if exist
                        delCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                            "DELETE FROM {0} WHERE {1} = @SessionId AND {2} = @ApplicationName",
                            properties.TableName, properties.SessionID, properties.ApplicationName);

                        delCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                        delCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                            settings.m_applicationName;

                        // Insert new session data
                        dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                            "INSERT INTO {0} ({1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}) Values (@SessionId, @ApplicationName, @Created, @Expires, @Timeout, @Locked, @LockId, @LockDate, @Data, @Flags)",
                            properties.TableName, properties.SessionID, properties.ApplicationName, properties.Created, properties.Expires, properties.Timeout, properties.Locked, properties.LockId, properties.LockDate, properties.Data, properties.Flags);

                        dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                        dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                            settings.m_applicationName;
                        dbCommand.Parameters.Add("@Created", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                        dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value =
                            DateTime.Now.AddMinutes((Double) item.Timeout);
                        dbCommand.Parameters.Add("@Timeout", NpgsqlDbType.Integer).Value = item.Timeout;
                        dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = false;
                        dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = 0;
                        dbCommand.Parameters.Add("@LockDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                        dbCommand.Parameters.Add("@Data", NpgsqlDbType.Text).Value = serializedItems;
                        dbCommand.Parameters.Add("@Flags", NpgsqlDbType.Integer).Value = 0;
                    }
                    else {
                        // Update existing session
                        dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                            "UPDATE {0} SET {1} = @Expires, {2} = @Locked, {3} = @Data WHERE {4} = @SessionId AND {5} = @ApplicationName AND {6} = @LockId",
                            properties.TableName, properties.Expires, properties.Locked, properties.Data, properties.SessionID, properties.ApplicationName, properties.LockId);

                        dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value =
                            DateTime.Now.AddMinutes((Double) item.Timeout);
                        dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = false;
                        dbCommand.Parameters.Add("@Data", NpgsqlDbType.Text).Value = serializedItems;
                        dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                        dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                            settings.m_applicationName;
                        dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = lockId;
                    }

                    NpgsqlTransaction dbTrans = null;

                    try {
                        dbConn.Open();
                        dbTrans = dbConn.BeginTransaction();

                        if (newItem) {
                            PrepareStatementIfEnabled(delCommand);
                            delCommand.ExecuteNonQuery();
                        }

                        PrepareStatementIfEnabled(dbCommand);
                        dbCommand.ExecuteNonQuery();

                        // Attempt to commit the transaction
                        dbTrans.Commit();
                    }
                    catch (Exception e) {
                        Trace.WriteLine(e.ToString());

                        if (dbTrans != null) {
                            try {
                                // Attempt to roll back the transaction
                                Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                                dbTrans.Rollback();
                            }
                            catch (Exception re) {
                                // Rollback failed
                                Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                                Trace.WriteLine(re.ToString());
                            }
                        }

                        throw new ProviderException(e.Message);
                    }
                    finally {
                        if (dbTrans != null)
                            dbTrans.Dispose();

                        if (dbConn != null)
                            dbConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// SessionStateProviderBase.SetItemExpireCallback
        /// </summary>
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback) {
            // Accept and store callback if session expire callback is enabled. If not, return false in order to inform SessionStateModule
            // the session expire callback is not supported.
            if (!m_enableExpireCallback)
                return false;

            m_expireCallback = expireCallback;
            return true;
        }

        #endregion

        #region private methods

        private bool CreateUninitializedItemTryInsert(string id, int timeout, NpgsqlConnection dbConn) {
            using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                SessionTableProperties properties = settings.SessionTableProperties;
                dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                    "INSERT INTO {0} ({1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}) Values (@SessionId, @ApplicationName, @Created, @Expires, @Timeout, @Locked, @LockId, @LockDate, @Data, @Flags)",
                    properties.TableName, properties.SessionID, properties.ApplicationName, properties.Created, properties.Expires, properties.Timeout, properties.Locked, properties.LockId, properties.LockDate, properties.Data, properties.Flags);

                dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                    settings.m_applicationName;
                dbCommand.Parameters.Add("@Created", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value =
                    DateTime.Now.AddMinutes((Double) timeout);
                dbCommand.Parameters.Add("@Timeout", NpgsqlDbType.Integer).Value = timeout;
                dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = false;
                dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = 0;
                dbCommand.Parameters.Add("@LockDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                dbCommand.Parameters.Add("@Data", NpgsqlDbType.Text).Value = string.Empty;
                dbCommand.Parameters.Add("@Flags", NpgsqlDbType.Integer).Value = 1;

                NpgsqlTransaction dbTrans = null;

                try {
                    PrepareStatementIfEnabled(dbCommand);

                    dbTrans = dbConn.BeginTransaction();

                    dbCommand.ExecuteNonQuery();

                    // Attempt to commit the transaction
                    dbTrans.Commit();

                    return true;
                }
                catch (Exception e) {
                    Trace.WriteLine(e.ToString());

                    if (dbTrans != null) {
                        try {
                            // Attempt to roll back the transaction
                            Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                            dbTrans.Rollback();
                        }
                        catch (NpgsqlException re) {
                            // Rollback failed
                            Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                            Trace.WriteLine(re.ToString());
                        }
                    }

                    return false;
                }
                finally {
                    if (dbTrans != null)
                        dbTrans.Dispose();
                }
            }
        }

        private bool CreateUninitializedItemTryUpdate(string id, int timeout, NpgsqlConnection dbConn) {
            using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                SessionTableProperties properties = settings.SessionTableProperties;
                dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                    "UPDATE {0} SET {1} = @Created, {2} = @Expires, {3} = @Timeout, {4} = @Locked, {5} = @LockId, {6} = @LockDate, {7} = @Data, {8} = @Flags WHERE {9} = @SessionId AND {10} = @ApplicationName",
                    properties.TableName, properties.Created, properties.Expires, properties.Timeout, properties.Locked, properties.LockId, properties.LockDate, properties.Data, properties.Flags, properties.SessionID, properties.ApplicationName);

                dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                    settings.m_applicationName;
                dbCommand.Parameters.Add("@Created", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value =
                    DateTime.Now.AddMinutes((Double) timeout);
                dbCommand.Parameters.Add("@Timeout", NpgsqlDbType.Integer).Value = timeout;
                dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = false;
                dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = 0;
                dbCommand.Parameters.Add("@LockDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                dbCommand.Parameters.Add("@Data", NpgsqlDbType.Text).Value = string.Empty;
                dbCommand.Parameters.Add("@Flags", NpgsqlDbType.Integer).Value = 1;

                NpgsqlTransaction dbTrans = null;

                try {
                    PrepareStatementIfEnabled(dbCommand);

                    dbTrans = dbConn.BeginTransaction();

                    var result = dbCommand.ExecuteNonQuery();

                    // Attempt to commit the transaction
                    dbTrans.Commit();

                    return (result > 0);
                }
                catch (Exception e) {
                    Trace.WriteLine(e.ToString());

                    if (dbTrans != null) {
                        try {
                            // Attempt to roll back the transaction
                            Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                            dbTrans.Rollback();
                        }
                        catch (NpgsqlException re) {
                            // Rollback failed
                            Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                            Trace.WriteLine(re.ToString());
                        }
                    }

                    return false;
                }
                finally {
                    if (dbTrans != null)
                        dbTrans.Dispose();
                }
            }
        }

        /// <summary>
        /// Retrieves the session data from the data source.
        /// </summary>
        /// <param name="lockRecord">If true GetSessionStoreItem locks the record and sets a new LockId and LockDate.</param>	
        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context, string id,
            out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actionFlags) {
            SessionStateStoreData result = null;
            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = 0;
            DateTime expires = DateTime.MinValue;
            int timeout = 0;
            string serializedItems = null;

            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                NpgsqlTransaction dbTrans = null;
                try {
                    dbConn.Open();
                    dbTrans = dbConn.BeginTransaction();

                    // Retrieve the current session item information and lock row
                    using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                        SessionTableProperties properties = settings.SessionTableProperties;
                        dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                            "SELECT {1}, {2}, {3}, {4}, {5}, {6}, {7} FROM {0} WHERE {8} = @SessionId AND {9} = @ApplicationName FOR UPDATE",
                            properties.TableName, properties.Expires, properties.Timeout, properties.Locked, properties.LockId, properties.LockDate, properties.Data, properties.Flags, properties.SessionID, properties.ApplicationName);

                        dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                        dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                            settings.m_applicationName;

                        using (NpgsqlDataReader reader = dbCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow)) {
                            while (reader.Read()) {
                                expires = reader.GetDateTime(0);
                                timeout = reader.GetInt32(1);
                                locked = reader.GetBoolean(2);
                                lockId = reader.GetInt32(3);
                                lockAge = DateTime.Now.Subtract(reader.GetDateTime(4));

                                if (!reader.IsDBNull(5))
                                    serializedItems = reader.GetString(5);

                                actionFlags = (SessionStateActions) reader.GetInt32(6);
                            }
                            reader.Close();
                        }
                    }

                    // If record was not found, is expired or is locked, return.
                    if (expires < DateTime.Now || locked)
                        return result;

                    // If the actionFlags parameter is not InitializeItem, deserialize the stored SessionStateItemCollection
                    if (actionFlags == SessionStateActions.InitializeItem)
                        result = CreateNewStoreData(context, Convert.ToInt32(settings.m_config.Timeout.TotalMinutes));
                    else
                        result = new SessionStateStoreData(Deserialize(serializedItems),
                            SessionStateUtility.GetSessionStaticObjects(context),
                            Convert.ToInt32(settings.m_config.Timeout.TotalMinutes));

                    if (lockRecord) {
                        lockId = (int) lockId + 1;
                        // Obtain a lock to the record
                        using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                            SessionTableProperties properties = settings.SessionTableProperties;
                            dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                                "UPDATE {0} SET {1} = @Locked, {2} = @LockId, {3} = @LockDate, {4} = @Flags WHERE {5} = @SessionId AND {6} = @ApplicationName",
                                properties.TableName, properties.Locked, properties.LockId, properties.LockDate, properties.Flags, properties.SessionID, properties.ApplicationName);

                            dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = true;
                            dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = lockId;
                            dbCommand.Parameters.Add("@LockDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                            dbCommand.Parameters.Add("@Flags", NpgsqlDbType.Integer).Value = 0;
                            dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
                            dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                                settings.m_applicationName;

                            dbCommand.ExecuteNonQuery();
                        }
                    }

                    // Attempt to commit the transaction
                    dbTrans.Commit();
                }
                catch (Exception e) {
                    Trace.WriteLine(e.ToString());

                    if (dbTrans != null) {
                        try {
                            // Attempt to roll back the transaction
                            Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                            dbTrans.Rollback();
                        }
                        catch (Exception re) {
                            // Rollback failed
                            Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                            Trace.WriteLine(re.ToString());
                        }
                    }

                    throw new ProviderException(e.Message);
                }
                finally {
                    if (dbTrans != null)
                        dbTrans.Dispose();

                    if (dbConn != null)
                        dbConn.Close();
                }

                return result;
            }
        }

        /// <summary>
        /// Convert a SessionStateItemCollection into a Base64 string
        /// </summary>
        private static string Serialize(SessionStateItemCollection items) {
            if (items == null || items.Count < 1)
                return string.Empty;

            using (MemoryStream mStream = new MemoryStream()) {
                using (BinaryWriter bWriter = new BinaryWriter(mStream)) {
                    items.Serialize(bWriter);
                    bWriter.Close();
                }

                return Convert.ToBase64String(mStream.ToArray());
            }
        }

        /// <summary>
        /// Convert a Base64 string into a SessionStateItemCollection
        /// </summary>
        /// <param name="serializedItems"></param>
        /// <returns></returns>
        private static SessionStateItemCollection Deserialize(string serializedItems) {
            SessionStateItemCollection sessionItems = new SessionStateItemCollection();

            if (string.IsNullOrEmpty(serializedItems))
                return sessionItems;

            using (MemoryStream mStream = new MemoryStream(Convert.FromBase64String(serializedItems))) {
                using (BinaryReader bReader = new BinaryReader(mStream)) {
                    sessionItems = SessionStateItemCollection.Deserialize(bReader);
                    bReader.Close();
                }
            }

            return sessionItems;
        }

        /// <summary>
        /// The ExpiredSessionDeletionTimer_Elapsed performs automatic session garbage collection by removing expired sessions from
        /// the database.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void ExpiredSessionDeletionTimer_Elapsed(object source, System.Timers.ElapsedEventArgs e) {
            /*
             * Determine mode of session garbage collection. If the session expire callback is disabled
             * one may simple delete all expired session from the session table. If however the session expire callback
             * is enabled, we need to load the session data for every expired session and invoke the expire callback
             * for each of these sessions prior to deletion.
             * Also check if an expire call back was actually defined. If m_expireCallback is null we also don't have to take
             * the more expensive path where every session is enumerated while there's no real need to do so.
             */

            if (m_enableExpireCallback && m_expireCallback != null)
                InvokeExpireCallbackAndDeleteSession();

            else
                DeleteExpiredSessionsFromDatabase();
        }

        /// <summary>
        /// Load the session data for every expired session and invoke the expire callback
        /// for each of these sessions prior to deletion.
        /// </summary>
        private void InvokeExpireCallbackAndDeleteSession() {
            Dictionary<string, SessionStateStoreData> expiredSessions = null;

            // Start out by enumerating all expired sessions
            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                using (NpgsqlCommand selectCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    selectCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                        "SELECT {1}, {2} FROM {0} WHERE {3} < @Expires AND {4} = @ApplicationName",
                        properties.TableName, properties.SessionID, properties.Data, properties.Expires, properties.ApplicationName);

                    selectCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                    selectCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                        settings.m_applicationName;

                    try {
                        dbConn.Open();
                        PrepareStatementIfEnabled(selectCommand);

                        using (NpgsqlDataReader reader = selectCommand.ExecuteReader()) {
                            if (!reader.HasRows)
                                return;

                            expiredSessions = new Dictionary<string, SessionStateStoreData>(reader.RecordsAffected);

                            // Get session data from data reader and reconstruct session.
                            // NOTE:	I'm not sure if I should pass any static objects to the constructor of the SessionStateStoreData class.
                            //			Seems to me you should not since garbage collection is say highly unlikely to be run in an actual http context.
                            while (reader.Read()) {
                                string sessionId = reader.GetString(0);
                                string serializedItems = reader.IsDBNull(1) ? null : reader.GetString(1);

                                expiredSessions.Add(sessionId,
                                    new SessionStateStoreData(Deserialize(serializedItems),
                                        new HttpStaticObjectsCollection(),
                                        Convert.ToInt32(settings.m_config.Timeout.TotalMinutes)));
                            }
                        }
                    }
                    catch (Exception ex) {
                        Trace.WriteLine(ex.ToString());

                        if (dbConn != null)
                            dbConn.Close();

                        throw new ProviderException(ex.Message);
                    }
                }

                using (NpgsqlCommand deleteCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    deleteCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                        "DELETE FROM {0} WHERE {1} = @SessionId AND {2} = @ApplicationName",
                        properties.TableName, properties.SessionID, properties.ApplicationName);

                    deleteCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80);
                    deleteCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                        settings.m_applicationName;

                    NpgsqlTransaction dbTrans = null;

                    try {
                        PrepareStatementIfEnabled(deleteCommand);
                        dbTrans = dbConn.BeginTransaction();

                        // Actually invoke session expire callback and delete session from the session table.
                        foreach (KeyValuePair<string, SessionStateStoreData> expiredSession in expiredSessions) {
                            // TODO: use async invocation insted?
                            m_expireCallback.Invoke(expiredSession.Key, expiredSession.Value);

                            deleteCommand.Parameters["@SessionId"].Value = expiredSession.Key;

                            deleteCommand.ExecuteNonQuery();
                        }

                        // Attempt to commit the transaction
                        dbTrans.Commit();
                    }
                    catch (Exception ex) {
                        Trace.WriteLine(ex.ToString());

                        if (dbTrans != null) {
                            try {
                                // Attempt to roll back the transaction
                                Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                                dbTrans.Rollback();
                            }
                            catch (Exception re) {
                                // Rollback failed
                                Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                                Trace.WriteLine(re.ToString());
                            }
                        }

                        throw new ProviderException(ex.Message);
                    }
                    finally {
                        if (dbTrans != null)
                            dbTrans.Dispose();

                        if (dbConn != null)
                            dbConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Delete all expired session from the session table.
        /// </summary>
        private void DeleteExpiredSessionsFromDatabase() {
            using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
                using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
                    SessionTableProperties properties = settings.SessionTableProperties;
                    dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
                        "DELETE FROM {0} WHERE {1} < @Expires AND {2} = @ApplicationName",
                        properties.TableName, properties.Expires, properties.ApplicationName);

                    dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
                    dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value =
                        settings.m_applicationName;

                    NpgsqlTransaction dbTrans = null;

                    try {
                        dbConn.Open();
                        PrepareStatementIfEnabled(dbCommand);

                        dbTrans = dbConn.BeginTransaction();

                        dbCommand.ExecuteNonQuery();

                        // Attempt to commit the transaction
                        dbTrans.Commit();
                    }
                    catch (Exception ex) {
                        Trace.WriteLine(ex.ToString());

                        if (dbTrans != null) {
                            try {
                                // Attempt to roll back the transaction
                                Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
                                dbTrans.Rollback();
                            }
                            catch (Exception re) {
                                // Rollback failed
                                Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
                                Trace.WriteLine(re.ToString());
                            }
                        }

                        throw new ProviderException(ex.Message);
                    }
                    finally {
                        if (dbTrans != null)
                            dbTrans.Dispose();

                        if (dbConn != null)
                            dbConn.Close();
                    }
                }
            }
        }

        private void PrepareStatementIfEnabled(NpgsqlCommand dbCommand) {
            if (settings.UsePreparedStatements) {
                dbCommand.Prepare();
            }
        }

        #endregion
    }
}