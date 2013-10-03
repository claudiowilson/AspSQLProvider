//
// Copyright © 2006 - 2011 Nauck IT KG		http://www.nauck-it.de
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
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Configuration;
using System.Configuration.Provider;
using System.Web.Hosting;
using System.Web.Security;
using NauckIT.PostgreSQLProvider.ProviderSettings;
using Npgsql;
using NpgsqlTypes;

namespace NauckIT.PostgreSQLProvider {
	public class PgRoleProvider : RoleProvider {
		private RoleProviderSettings _settings;

		/// <summary>
		/// System.Configuration.Provider.ProviderBase.Initialize Method
		/// </summary>
		public override void Initialize(string name, NameValueCollection config) {
			// Initialize values from web.config.
			if (config == null)
				throw new ArgumentNullException("config", Properties.Resources.ErrArgumentNull);

			if (string.IsNullOrEmpty(name))
				name = Properties.Resources.RoleProviderDefaultName;

			if (string.IsNullOrEmpty(config["description"])) {
				config.Remove("description");
				config.Add("description", Properties.Resources.RoleProviderDefaultDescription);
			}

			// Initialize the abstract base class.
			base.Initialize(name, config);
			_settings = new RoleProviderSettings(config);
		}

		/// <summary>
		/// System.Web.Security.RoleProvider properties.
		/// </summary>

		#region System.Web.Security.RoleProvider properties
		public override string ApplicationName {
			get { return _settings.m_applicationName; }
			set { _settings.m_applicationName = value; }
		}

		#endregion

		/// <summary>
		/// System.Web.Security.RoleProvider methods.
		/// </summary>

		#region System.Web.Security.RoleProvider methods

		/// <summary>
		/// RoleProvider.AddUsersToRoles
		/// </summary>
		public override void AddUsersToRoles(string[] usernames, string[] roleNames) {
			foreach (string rolename in roleNames) {
				if (!RoleExists(rolename)) {
					throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrRoleNotExist,
						rolename));
				}
			}

			foreach (string username in usernames) {
				foreach (string rolename in roleNames) {
					if (IsUserInRole(username, rolename)) {
						throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrUserAlreadyInRole,
							username, rolename));
					}
				}
			}

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"INSERT INTO {0} ({1}, {2}, {3}) Values (@Username, @Rolename, @ApplicationName)",
						properties.TableName, properties.Username, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255);
					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255);
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					NpgsqlTransaction dbTrans = null;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (dbTrans = dbConn.BeginTransaction()) {
							foreach (string username in usernames) {
								foreach (string rolename in roleNames) {
									dbCommand.Parameters["@Username"].Value = username;
									dbCommand.Parameters["@Rolename"].Value = rolename;
									dbCommand.ExecuteNonQuery();
								}
							}
							// Attempt to commit the transaction
							dbTrans.Commit();
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());

						try {
							// Attempt to roll back the transaction
							Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
							dbTrans.Rollback();
						} catch (NpgsqlException re) {
							// Rollback failed
							Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
							Trace.WriteLine(re.ToString());
						}

						throw new ProviderException(Properties.Resources.ErrOperationAborted);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}
		}

		/// <summary>
		/// RoleProvider.CreateRole
		/// </summary>
		public override void CreateRole(string roleName) {
			if (RoleExists(roleName)) {
				throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrRoleAlreadyExist,
					roleName));
			}

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					RoleTableProperties properties = _settings.RoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"INSERT INTO {0} ({1}, {2}) Values (@Rolename, @ApplicationName)",
						properties.TableName, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255).Value = roleName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						dbCommand.ExecuteNonQuery();
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}
		}

		/// <summary>
		/// RoleProvider.DeleteRole
		/// </summary>
		public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) {
			if (!RoleExists(roleName)) {
				throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrRoleNotExist,
					roleName));
			}

			if (throwOnPopulatedRole && GetUsersInRole(roleName).Length > 0) {
				throw new ProviderException(Properties.Resources.ErrCantDeletePopulatedRole);
			}

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					RoleTableProperties properties = _settings.RoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"DELETE FROM {0} WHERE {1} = @Rolename AND {2} = @ApplicationName",
						properties.TableName, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255).Value = roleName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					NpgsqlTransaction dbTrans = null;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (dbTrans = dbConn.BeginTransaction()) {
							dbCommand.ExecuteNonQuery();

							// Attempt to commit the transaction
							dbTrans.Commit();
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.Message);

						try {
							// Attempt to roll back the transaction
							Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
							dbTrans.Rollback();
						} catch (NpgsqlException re) {
							// Rollback failed
							Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
							Trace.WriteLine(re.ToString());
						}

						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return true;
		}

		/// <summary>
		/// RoleProvider.FindUsersInRole
		/// </summary>
		public override string[] FindUsersInRole(string roleName, string usernameToMatch) {
			List<string> userList = new List<string>();

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1} FROM {0} WHERE {1} ILIKE @Username AND {2} = @Rolename AND {3} = @ApplicationName ORDER BY {1} ASC",
						properties.TableName, properties.Username, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = usernameToMatch;
					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255).Value = roleName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								while (reader.Read()) {
									userList.Add(reader.GetString(0));
								}
							}
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return userList.ToArray();
		}

		/// <summary>
		/// RoleProvider.GetAllRoles
		/// </summary>
		public override string[] GetAllRoles() {
			List<string> rolesList = new List<string>();

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1} FROM {0} WHERE {2} = @ApplicationName ORDER BY {1} ASC",
						properties.TableName, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							while (reader.Read()) {
								rolesList.Add(reader.GetString(0));
							}
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return rolesList.ToArray();
		}

		/// <summary>
		/// RoleProvider.GetRolesForUser
		/// </summary>
		public override string[] GetRolesForUser(string username) {
			List<string> rolesList = new List<string>();

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1} FROM {0} WHERE {2} = @Username AND {3} = @ApplicationName ORDER BY {1} ASC",
						properties.TableName, properties.Rolename, properties.Username, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								while (reader.Read()) {
									rolesList.Add(reader.GetString(0));
								}
							}
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return rolesList.ToArray();
		}

		/// <summary>
		/// RoleProvider.GetUsersInRole
		/// </summary>
		public override string[] GetUsersInRole(string roleName) {
			List<string> userList = new List<string>();

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1} FROM {0} WHERE {2} = @Rolename AND {3} = @ApplicationName ORDER BY {1} ASC",
						properties.TableName, properties.Username, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255).Value = roleName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								while (reader.Read()) {
									userList.Add(reader.GetString(0));
								}
							}
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return userList.ToArray();
		}

		/// <summary>
		/// RoleProvider.IsUserInRole
		/// </summary>
		public override bool IsUserInRole(string username, string roleName) {
			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT COUNT(*) FROM {0} WHERE {1}= @Username AND {2} = @Rolename AND {3} = @ApplicationName",
						properties.TableName, properties.Username, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255).Value = roleName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						int numRecs = 0;
						if (!Int32.TryParse(dbCommand.ExecuteScalar().ToString(), out numRecs))
							return false;

						if (numRecs > 0)
							return true;
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return false;
		}

		/// <summary>
		/// RoleProvider.RemoveUsersFromRoles
		/// </summary>
		public override void RemoveUsersFromRoles(string[] usernames, string[] roleNames) {
			foreach (string rolename in roleNames) {
				if (!RoleExists(rolename)) {
					throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrRoleNotExist,
						rolename));
				}
			}

			foreach (string username in usernames) {
				foreach (string rolename in roleNames) {
					if (!IsUserInRole(username, rolename)) {
						throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrUserIsNotInRole,
							username, rolename));
					}
				}
			}

			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					UsersInRolesTableProperties properties = _settings.UsersRoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"DELETE FROM {0} WHERE {1} = @Username AND {2} = @Rolename AND {3} = @ApplicationName",
						properties.TableName, properties.Username, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255);
					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255);
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					NpgsqlTransaction dbTrans = null;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (dbTrans = dbConn.BeginTransaction()) {
							foreach (string username in usernames) {
								foreach (string rolename in roleNames) {
									dbCommand.Parameters["@Username"].Value = username;
									dbCommand.Parameters["@Rolename"].Value = rolename;
									dbCommand.ExecuteNonQuery();
								}
							}
							// Attempt to commit the transaction
							dbTrans.Commit();
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());

						try {
							// Attempt to roll back the transaction
							Trace.WriteLine(Properties.Resources.LogRollbackAttempt);
							dbTrans.Rollback();
						} catch (NpgsqlException re) {
							// Rollback failed
							Trace.WriteLine(Properties.Resources.ErrRollbackFailed);
							Trace.WriteLine(re.ToString());
						}

						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}
		}

		/// <summary>
		/// RoleProvider.RoleExists
		/// </summary>
		public override bool RoleExists(string roleName) {
			using (NpgsqlConnection dbConn = new NpgsqlConnection(_settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					RoleTableProperties properties = _settings.RoleTableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT COUNT(*) FROM {0} WHERE {1} = @Rolename AND {2} = @ApplicationName",
						properties.TableName, properties.Rolename, properties.ApplicationName);

					dbCommand.Parameters.Add("@Rolename", NpgsqlDbType.Varchar, 255).Value = roleName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						int numRecs = 0;
						if (!Int32.TryParse(dbCommand.ExecuteScalar().ToString(), out numRecs))
							return false;

						if (numRecs > 0)
							return true;
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return false;
		}

		#endregion
		private void PrepareStatementIfEnabled(NpgsqlCommand dbCommand) {
			if (_settings.UsePreparedStatements) {
				dbCommand.Prepare();
			}
		}
	}

}