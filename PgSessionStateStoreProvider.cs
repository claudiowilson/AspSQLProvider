//
// $Id$
//
// Copyright � 2007 Nauck IT KG		http://www.nauck-it.de
//
// Author:
//	Daniel Nauck		<d.nauck(at)nauck-it.de>
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
using System.Text;
using System.Web;
using System.Web.Hosting;
using System.Web.SessionState;
using Npgsql;
using NpgsqlTypes;

namespace NauckIT.PostgreSQLProvider
{
	public class PgSessionStateStoreProvider : SessionStateStoreProviderBase
	{
		private const string m_TableName = "Sessions";
		private string m_ConnectionString = string.Empty;
		private string m_ApplicationName = string.Empty;

		/// <summary>
		/// System.Configuration.Provider.ProviderBase.Initialize Method
		/// </summary>
		public override void Initialize(string name, NameValueCollection config)
		{
			// Initialize values from web.config.
			if (config == null)
				throw new ArgumentNullException("Config", Properties.Resources.ErrArgumentNull);

			if (string.IsNullOrEmpty(name))
				name = Properties.Resources.SessionStoreProviderDefaultName;

			if (string.IsNullOrEmpty(config["description"]))
			{
				config.Remove("description");
				config.Add("description", Properties.Resources.SessionStoreProviderDefaultDescription);
			}

			// Initialize the abstract base class.
			base.Initialize(name, config);

			m_ApplicationName = GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);

			// Get connection string.
			string connStrName = config["connectionStringName"];

			if (string.IsNullOrEmpty(connStrName))
			{
				throw new ArgumentOutOfRangeException("ConnectionStringName", Properties.Resources.ErrArgumentNullOrEmpty);
			}
			else
			{
				ConnectionStringSettings ConnectionStringSettings = ConfigurationManager.ConnectionStrings[connStrName];

				if (ConnectionStringSettings == null || string.IsNullOrEmpty(ConnectionStringSettings.ConnectionString.Trim()))
				{
					throw new ProviderException(Properties.Resources.ErrConnectionStringNullOrEmpty);
				}

				m_ConnectionString = ConnectionStringSettings.ConnectionString;
			}
		}

		/// <summary>
		/// SessionStateStoreProviderBase members
		/// </summary>
		#region SessionStateStoreProviderBase members

		public override void Dispose()
		{
		}

		/// <summary>
		/// SessionStateProviderBase.InitializeRequest
		/// </summary>
		public override void InitializeRequest(HttpContext context)
		{
		}

		/// <summary>
		/// SessionStateProviderBase.EndRequest
		/// </summary>
		public override void EndRequest(HttpContext context)
		{
		}

		/// <summary>
		/// SessionStateProviderBase.CreateNewStoreData
		/// </summary>
		public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
		{
			return new SessionStateStoreData(new SessionStateItemCollection(), SessionStateUtility.GetSessionStaticObjects(context), timeout);
		}

		/// <summary>
		/// SessionStateProviderBase.CreateUninitializedItem
		/// </summary>
		public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
		{
			using (NpgsqlConnection dbConn = new NpgsqlConnection(m_ConnectionString))
			{
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand())
				{
					dbCommand.CommandText = string.Format("INSERT INTO \"{0}\" (\"SessionId\", \"ApplicationName\", \"Created\", \"Expires\", \"Timeout\", \"Locked\", \"LockId\", \"LockDate\", \"Data\", \"Flags\") Values (@SessionId, @ApplicationName, @Created, @Expires, @Timeout, @Locked, @LockId, @LockDate, @Data, @Flags)", m_TableName);

					dbCommand.Parameters.Add("@SessionId", NpgsqlDbType.Varchar, 80).Value = id;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = m_ApplicationName;
					dbCommand.Parameters.Add("@Created", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
					dbCommand.Parameters.Add("@Expires", NpgsqlDbType.TimestampTZ).Value = DateTime.Now.AddMinutes((Double)timeout);
					dbCommand.Parameters.Add("@Timeout", NpgsqlDbType.Integer).Value = timeout;
					dbCommand.Parameters.Add("@Locked", NpgsqlDbType.Boolean).Value = false;
					dbCommand.Parameters.Add("@LockId", NpgsqlDbType.Integer).Value = 0;
					dbCommand.Parameters.Add("@LockDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
					dbCommand.Parameters.Add("@Data", NpgsqlDbType.Text).Value = string.Empty;
					dbCommand.Parameters.Add("@Flags", NpgsqlDbType.Integer).Value = 1;

					NpgsqlTransaction dbTrans = null;

					try
					{
						dbConn.Open();
						dbCommand.Prepare();

						using (dbTrans = dbConn.BeginTransaction())
						{
							dbCommand.ExecuteNonQuery();

							// Attempt to commit the transaction
							dbTrans.Commit();
						}
					}
					catch (NpgsqlException e)
					{
						Trace.WriteLine(e.ToString());

						try
						{
							// Attempt to roll back the transaction
							Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
							dbTrans.Rollback();
						}
						catch (NpgsqlException re)
						{
							// Rollback failed
							Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
							Trace.WriteLine(re.ToString());
						}

						throw new ProviderException(Properties.Resources.ErrOperationAborted);
					}
					finally
					{
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}
		}

		public override SessionStateStoreData GetItem(System.Web.HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public override SessionStateStoreData GetItemExclusive(System.Web.HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public override void ReleaseItemExclusive(System.Web.HttpContext context, string id, object lockId)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public override void RemoveItem(System.Web.HttpContext context, string id, object lockId, SessionStateStoreData item)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public override void ResetItemTimeout(System.Web.HttpContext context, string id)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public override void SetAndReleaseItemExclusive(System.Web.HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		/// <summary>
		/// SessionStateProviderBase.SetItemExpireCallback
		/// </summary>
		public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
		{
			return false;
		}
		#endregion

		#region private methods
		/// <summary>
		/// A helper function to retrieve config values from the configuration file.
		/// </summary>
		/// <param name="configValue"></param>
		/// <param name="defaultValue"></param>
		/// <returns></returns>
		private string GetConfigValue(string configValue, string defaultValue)
		{
			if (string.IsNullOrEmpty(configValue))
				return defaultValue;

			return configValue;
		}
		#endregion
	}
}
