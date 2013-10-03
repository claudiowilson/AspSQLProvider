//
// Copyright © 2006 - 2013 Nauck IT KG		http://www.nauck-it.de
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
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Web;
using System.Web.Hosting;
using System.Web.Configuration;
using System.Web.Security;
using System.Configuration;
using System.Configuration.Provider;
using NauckIT.PostgreSQLProvider.ProviderSettings;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace NauckIT.PostgreSQLProvider {
	public class PgMembershipProvider : MembershipProvider {
		private MembershipProviderSettings settings;

		/// <summary>
		/// System.Configuration.Provider.ProviderBase.Initialize Method.
		/// </summary>
		public override void Initialize(string name, NameValueCollection config) {
			// Initialize values from web.config.
			if (config == null)
				throw new ArgumentNullException("config", Properties.Resources.ErrArgumentNull);

			if (string.IsNullOrEmpty(name))
				name = Properties.Resources.MembershipProviderDefaultName;

			if (string.IsNullOrEmpty(config["description"])) {
				config.Remove("description");
				config.Add("description", Properties.Resources.MembershipProviderDefaultDescription);
			}

			// Initialize the abstract base class.
			base.Initialize(name, config);
			settings = new MembershipProviderSettings(config);
		}

		/// <summary>
		/// System.Web.Security.MembershipProvider properties.
		/// </summary>

		#region System.Web.Security.MembershipProvider properties
		public override string ApplicationName {
			get { return settings.m_applicationName; }
			set { settings.m_applicationName = value; }
		}

		public override bool EnablePasswordReset {
			get { return settings.m_enablePasswordReset; }
		}

		public override bool EnablePasswordRetrieval {
			get { return settings.m_enablePasswordRetrieval; }
		}

		public override bool RequiresQuestionAndAnswer {
			get { return settings.m_requiresQuestionAndAnswer; }
		}

		public override bool RequiresUniqueEmail {
			get { return settings.m_requiresUniqueEmail; }
		}

		public override int MaxInvalidPasswordAttempts {
			get { return settings.m_maxInvalidPasswordAttempts; }
		}

		public override int PasswordAttemptWindow {
			get { return settings.m_passwordAttemptWindow; }
		}

		public override MembershipPasswordFormat PasswordFormat {
			get { return settings.m_passwordFormat; }
		}

		public override int MinRequiredNonAlphanumericCharacters {
			get { return settings.m_minRequiredNonAlphanumericCharacters; }
		}

		public override int MinRequiredPasswordLength {
			get { return settings.m_minRequiredPasswordLength; }
		}

		public override string PasswordStrengthRegularExpression {
			get { return settings.m_passwordStrengthRegularExpression; }
		}

		#endregion

		/// <summary>
		/// System.Web.Security.MembershipProvider methods.
		/// </summary>

		#region System.Web.Security.MembershipProvider methods

		/// <summary>
		/// MembershipProvider.ChangePassword
		/// </summary>
		public override bool ChangePassword(string username, string oldPassword, string newPassword) {
			if (!ValidateUser(username, oldPassword))
				return false;

			ValidatePasswordEventArgs args = new ValidatePasswordEventArgs(username, newPassword, true);

			OnValidatingPassword(args);

			if (args.Cancel) {
				if (args.FailureInformation != null)
					throw args.FailureInformation;
				else
					throw new MembershipPasswordException(Properties.Resources.ErrPasswordChangeCanceled);
			}

			int rowsAffected = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"UPDATE {0} SET {1} = @Password, {2} = @LastPasswordChangedDate WHERE {3} = @Username AND {4} = @ApplicationName",
						properties.TableName, properties.Password, properties.LastPasswordChangedDate, properties.UserName,
						properties.ApplicationName);

					dbCommand.Parameters.Add("@Password", NpgsqlDbType.Varchar, 128).Value = EncodePassword(newPassword);
					dbCommand.Parameters.Add("@LastPasswordChangedDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						rowsAffected = dbCommand.ExecuteNonQuery();
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			if (rowsAffected > 0)
				return true;
			else
				return false;
		}

		/// <summary>
		/// MembershipProvider.ChangePasswordQuestionAndAnswer
		/// </summary>
		public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion,
			string newPasswordAnswer) {
			if (!ValidateUser(username, password))
				return false;

			int rowsAffected = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"UPDATE {0} SET {1} = @PasswordQuestion, {2} = @PasswordAnswer WHERE {3} = @Username AND {4} = @ApplicationName",
						properties.TableName, properties.PasswordQuestion, properties.PasswordAnswer, properties.UserName,
						properties.ApplicationName);

					dbCommand.Parameters.Add("@PasswordQuestion", NpgsqlDbType.Varchar, 255).Value = newPasswordQuestion;
					dbCommand.Parameters.Add("@PasswordAnswer", NpgsqlDbType.Varchar, 255).Value = EncodePassword(newPasswordAnswer);
					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						rowsAffected = dbCommand.ExecuteNonQuery();
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			if (rowsAffected > 0)
				return true;
			else
				return false;
		}

		/// <summary>
		/// MembershipProvider.CreateUser
		/// </summary>
		public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion,
			string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status) {
			ValidatePasswordEventArgs args = new ValidatePasswordEventArgs(username, password, true);

			OnValidatingPassword(args);

			if (args.Cancel) {
				status = MembershipCreateStatus.InvalidPassword;
				return null;
			}

			if (RequiresUniqueEmail && string.IsNullOrEmpty(email)) {
				status = MembershipCreateStatus.InvalidEmail;
				return null;
			}

			if (RequiresUniqueEmail && !string.IsNullOrEmpty(GetUserNameByEmail(email))) {
				status = MembershipCreateStatus.DuplicateEmail;
				return null;
			}

			if (GetUser(username, false) == null) {
				DateTime createDate = DateTime.Now;

				if (providerUserKey == null) {
					providerUserKey = Guid.NewGuid();
				} else {
					if (!(providerUserKey is Guid)) {
						status = MembershipCreateStatus.InvalidProviderUserKey;
						return null;
					}
				}

				// Create user in database
				using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
					using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
						MembershipTableProperties properties = settings.TableProperties;
						dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
							"INSERT INTO {0} ({1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17}) Values (@pId, @Username, @Password, @Email, @PasswordQuestion, @PasswordAnswer, @IsApproved, @CreationDate, @LastPasswordChangedDate, @LastActivityDate, @ApplicationName, @IsLockedOut, @LastLockedOutDate, @FailedPasswordAttemptCount, @FailedPasswordAttemptWindowStart, @FailedPasswordAnswerAttemptCount, @FailedPasswordAnswerAttemptWindowStart)",
							properties.TableName, properties.UserId, properties.UserName, properties.Password, properties.Email,
							properties.PasswordQuestion, properties.PasswordAnswer, properties.IsApproved, properties.CreationDate,
							properties.LastPasswordChangedDate, properties.LastActivityDate, properties.ApplicationName,
							properties.IsLockedOut, properties.LastLockedOutDate, properties.FailedPasswordAttemptCount,
							properties.FailedPasswordAttemptWindowStart, properties.FailedPasswordAnswerAttemptCount,
							properties.FailedPasswordAnswerAttemptWindowStart);

						dbCommand.Parameters.Add("@pId", NpgsqlDbType.Varchar, 36).Value = providerUserKey;
						dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
						dbCommand.Parameters.Add("@Password", NpgsqlDbType.Varchar, 255).Value = EncodePassword(password);
						dbCommand.Parameters.Add("@Email", NpgsqlDbType.Varchar, 128).Value = email;
						dbCommand.Parameters.Add("@PasswordQuestion", NpgsqlDbType.Varchar, 255).Value = passwordQuestion;
						dbCommand.Parameters.Add("@PasswordAnswer", NpgsqlDbType.Varchar, 255).Value = EncodePassword(passwordAnswer);
						dbCommand.Parameters.Add("@IsApproved", NpgsqlDbType.Boolean).Value = isApproved;
						dbCommand.Parameters.Add("@CreationDate", NpgsqlDbType.TimestampTZ).Value = createDate;
						dbCommand.Parameters.Add("@LastPasswordChangedDate", NpgsqlDbType.TimestampTZ).Value = createDate;
						dbCommand.Parameters.Add("@LastActivityDate", NpgsqlDbType.TimestampTZ).Value = createDate;
						dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;
						dbCommand.Parameters.Add("@IsLockedOut", NpgsqlDbType.Boolean).Value = false;
						dbCommand.Parameters.Add("@LastLockedOutDate", NpgsqlDbType.TimestampTZ).Value = createDate;
						dbCommand.Parameters.Add("@FailedPasswordAttemptCount", NpgsqlDbType.Integer).Value = 0;
						dbCommand.Parameters.Add("@FailedPasswordAttemptWindowStart", NpgsqlDbType.TimestampTZ).Value = createDate;
						dbCommand.Parameters.Add("@FailedPasswordAnswerAttemptCount", NpgsqlDbType.Integer).Value = 0;
						dbCommand.Parameters.Add("@FailedPasswordAnswerAttemptWindowStart", NpgsqlDbType.TimestampTZ).Value = createDate;

						try {
							dbConn.Open();
							PrepareStatementIfEnabled(dbCommand);

							if (dbCommand.ExecuteNonQuery() > 0) {
								status = MembershipCreateStatus.Success;
							} else {
								status = MembershipCreateStatus.UserRejected;
							}
						} catch (NpgsqlException e) {
							status = MembershipCreateStatus.ProviderError;
							Trace.WriteLine(e.ToString());
							throw new ProviderException(e.Message);
						} finally {
							if (dbConn != null)
								dbConn.Close();
						}

						return GetUser(username, false);
					}
				}
			} else {
				status = MembershipCreateStatus.DuplicateUserName;
			}
			return null;
		}

		/// <summary>
		/// MembershipProvider.DeleteUser
		/// </summary>
		public override bool DeleteUser(string username, bool deleteAllRelatedData) {
			int rowsAffected = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"DELETE FROM {0} WHERE {1} = @Username AND  {2} = @ApplicationName",
						properties.TableName, properties.UserName, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						rowsAffected = dbCommand.ExecuteNonQuery();

						if (deleteAllRelatedData) {
							// Process commands to delete all data for the user in the database.
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

			if (rowsAffected > 0)
				return true;
			else
				return false;
		}

		/// <summary>
		/// MembershipProvider.FindUsersByEmail
		/// </summary>
		public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize,
			out int totalRecords) {
			totalRecords = 0;
			MembershipUserCollection users = new MembershipUserCollection();

			if (string.IsNullOrEmpty(emailToMatch))
				return users;

			// replace permitted wildcard characters 
			emailToMatch = emailToMatch.Replace('*', '%');
			emailToMatch = emailToMatch.Replace('?', '_');

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				// Get user count
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT Count(*) FROM {0} WHERE {1} ILIKE @Email AND {2} = @ApplicationName",
						properties.TableName, properties.Email, properties.ApplicationName);

					dbCommand.Parameters.Add("@Email", NpgsqlDbType.Varchar, 128).Value = emailToMatch;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						if (!Int32.TryParse(dbCommand.ExecuteScalar().ToString(), out totalRecords))
							return users;

						if (totalRecords <= 0) {
							return users;
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}

				// Fetch user from database
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12} FROM {0} WHERE {3} ILIKE @Email AND {13} = @ApplicationName ORDER BY {2} ASC LIMIT @MaxCount OFFSET @StartIndex",
						properties.TableName, properties.UserId, properties.UserName, properties.Email, properties.PasswordQuestion,
						properties.Comment, properties.IsApproved, properties.IsLockedOut, properties.CreationDate,
						properties.LastLoginDate, properties.LastActivityDate, properties.LastPasswordChangedDate,
						properties.LastLockedOutDate, properties.ApplicationName);

					dbCommand.Parameters.Add("@Email", NpgsqlDbType.Varchar, 128).Value = emailToMatch;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;
					dbCommand.Parameters.Add("@MaxCount", NpgsqlDbType.Integer).Value = pageSize;
					dbCommand.Parameters.Add("@StartIndex", NpgsqlDbType.Integer).Value = pageSize*pageIndex;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							while (reader.Read()) {
								MembershipUser u = GetUserFromReader(reader);
								users.Add(u);
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

			return users;
		}

		/// <summary>
		/// MembershipProvider.FindUsersByName
		/// </summary>
		public override MembershipUserCollection FindUsersByName(string usernameToMatch, int pageIndex, int pageSize,
			out int totalRecords) {
			totalRecords = 0;
			MembershipUserCollection users = new MembershipUserCollection();

			// replace permitted wildcard characters 
			usernameToMatch = usernameToMatch.Replace('*', '%');
			usernameToMatch = usernameToMatch.Replace('?', '_');

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				// Get user count
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT Count(*) FROM {0} WHERE {1} ILIKE @Username AND {2} = @ApplicationName",
						properties.TableName, properties.UserName, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = usernameToMatch;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						if (!Int32.TryParse(dbCommand.ExecuteScalar().ToString(), out totalRecords))
							return users;

						if (totalRecords <= 0) {
							return users;
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}

				// Fetch user from database
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12} FROM {0} WHERE {2} ILIKE @Username AND {13} = @ApplicationName ORDER BY {2} ASC LIMIT @MaxCount OFFSET @StartIndex",
						properties.TableName, properties.UserId, properties.UserName, properties.Email, properties.PasswordQuestion,
						properties.Comment, properties.IsApproved, properties.IsLockedOut, properties.CreationDate,
						properties.LastLoginDate, properties.LastActivityDate, properties.LastPasswordChangedDate,
						properties.LastLockedOutDate, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = usernameToMatch;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;
					dbCommand.Parameters.Add("@MaxCount", NpgsqlDbType.Integer).Value = pageSize;
					dbCommand.Parameters.Add("@StartIndex", NpgsqlDbType.Integer).Value = pageSize*pageIndex;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							while (reader.Read()) {
								MembershipUser u = GetUserFromReader(reader);
								users.Add(u);
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

			return users;
		}

		/// <summary>
		/// MembershipProvider.GetAllUsers
		/// </summary>
		public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords) {
			totalRecords = 0;
			MembershipUserCollection users = new MembershipUserCollection();

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				// Get user count
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT Count(*) FROM {0} WHERE {1} = @ApplicationName",
						properties.TableName, properties.ApplicationName);

					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						if (!Int32.TryParse(dbCommand.ExecuteScalar().ToString(), out totalRecords))
							return users;

						if (totalRecords <= 0) {
							return users;
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}

				// Fetch user from database
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12} FROM {0} WHERE {13} = @ApplicationName ORDER BY {2} ASC LIMIT @MaxCount OFFSET @StartIndex",
						properties.TableName, properties.UserId, properties.UserName, properties.Email, properties.PasswordQuestion,
						properties.Comment, properties.IsApproved, properties.IsLockedOut, properties.CreationDate,
						properties.LastLoginDate, properties.LastActivityDate, properties.LastPasswordChangedDate,
						properties.LastLockedOutDate, properties.ApplicationName);

					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;
					dbCommand.Parameters.Add("@MaxCount", NpgsqlDbType.Integer).Value = pageSize;
					dbCommand.Parameters.Add("@StartIndex", NpgsqlDbType.Integer).Value = pageSize*pageIndex;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							while (reader.Read()) {
								MembershipUser u = GetUserFromReader(reader);
								users.Add(u);
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

			return users;
		}

		/// <summary>
		/// MembershipProvider.GetNumberOfUsersOnline
		/// </summary>
		public override int GetNumberOfUsersOnline() {
			int numOnline = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					TimeSpan onlineSpan = new TimeSpan(0, System.Web.Security.Membership.UserIsOnlineTimeWindow, 0);
					DateTime compareTime = DateTime.Now.Subtract(onlineSpan);
					MembershipTableProperties properties = settings.TableProperties;

					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT Count(*) FROM {0} WHERE {1} > @CompareTime AND {2} = @ApplicationName",
						properties.TableName, properties.LastActivityDate, properties.ApplicationName);

					dbCommand.Parameters.Add("@CompareTime", NpgsqlDbType.TimestampTZ, 255).Value = compareTime;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						numOnline = (int) dbCommand.ExecuteScalar();
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return numOnline;
		}

		/// <summary>
		/// MembershipProvider.GetPassword
		/// </summary>
		public override string GetPassword(string username, string answer) {
			if (!EnablePasswordRetrieval) {
				throw new ProviderException(Properties.Resources.ErrPasswordRetrievalNotEnabled);
			}

			if (PasswordFormat == MembershipPasswordFormat.Hashed) {
				throw new ProviderException(Properties.Resources.ErrCantRetrieveHashedPw);
			}

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3} FROM {0} WHERE {4} = @Username AND {5} = @ApplicationName",
						properties.TableName, properties.Password, properties.PasswordAnswer, properties.IsLockedOut, properties.UserName,
						properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								reader.Read();

								string password = reader.GetString(0);
								string passwordAnswer = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
								bool isLockedOut = reader.IsDBNull(2) ? false : reader.GetBoolean(2);

								reader.Close();

								if (isLockedOut)
									throw new MembershipPasswordException(Properties.Resources.ErrUserIsLoggedOut);

								if (settings.m_requiresQuestionAndAnswer && !CheckPassword(answer, passwordAnswer)) {
									UpdateFailureCount(username, FailureType.PasswordAnswer);

									throw new MembershipPasswordException(Properties.Resources.ErrIncorrectPasswordAnswer);
								}

								if (settings.m_passwordFormat == MembershipPasswordFormat.Encrypted) {
									password = UnEncodePassword(password);
								}

								return password;
							} else {
								throw new MembershipPasswordException(Properties.Resources.ErrUserNotFound);
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
		}

		/// <summary>
		/// MembershipProvider.GetUser
		/// </summary>
		public override MembershipUser GetUser(string username, bool userIsOnline) {
			MembershipUser u = null;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12} FROM {0} WHERE {2} = @Username AND {13} = @ApplicationName",
						properties.TableName, properties.UserId, properties.UserName, properties.Email, properties.PasswordQuestion,
						properties.Comment, properties.IsApproved, properties.IsLockedOut, properties.CreationDate,
						properties.LastLoginDate, properties.LastActivityDate, properties.LastPasswordChangedDate,
						properties.LastLockedOutDate, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								reader.Read();
								u = GetUserFromReader(reader);
								reader.Close();

								if (userIsOnline) {
									// Update user online status
									using (NpgsqlCommand dbUpdateCommand = dbConn.CreateCommand()) {
										dbUpdateCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
											"UPDATE {0} SET {1} = @LastActivityDate WHERE {2} = @pId",
											properties.TableName, properties.LastActivityDate, properties.UserId);

										dbUpdateCommand.Parameters.Add("@LastActivityDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
										dbUpdateCommand.Parameters.Add("@pId", NpgsqlDbType.Char, 36).Value = u.ProviderUserKey;

										PrepareStatementIfEnabled(dbUpdateCommand);

										dbUpdateCommand.ExecuteNonQuery();
									}
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

			return u;
		}

		/// <summary>
		/// MembershipProvider.GetUser
		/// </summary>
		public override MembershipUser GetUser(object providerUserKey, bool userIsOnline) {
			MembershipUser u = null;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12} FROM {0} WHERE {1} = @pId",
						properties.TableName, properties.UserId, properties.UserName, properties.Email, properties.PasswordQuestion,
						properties.Comment, properties.IsApproved, properties.IsLockedOut, properties.CreationDate,
						properties.LastLoginDate, properties.LastActivityDate, properties.LastPasswordChangedDate,
						properties.LastLockedOutDate);

					dbCommand.Parameters.Add("@pId", NpgsqlDbType.Char, 36).Value = providerUserKey;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								reader.Read();
								u = GetUserFromReader(reader);
								reader.Close();

								if (userIsOnline) {
									// Update user online status
									using (NpgsqlCommand dbUpdateCommand = dbConn.CreateCommand()) {
										dbUpdateCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
											"UPDATE {0} SET {1} = @LastActivityDate WHERE {2} = @pId",
											properties.TableName, properties.LastActivityDate, properties.UserId);

										dbUpdateCommand.Parameters.Add("@LastActivityDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
										dbUpdateCommand.Parameters.Add("@pId", NpgsqlDbType.Char, 36).Value = u.ProviderUserKey;

										PrepareStatementIfEnabled(dbUpdateCommand);

										dbUpdateCommand.ExecuteNonQuery();
									}
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

			return u;
		}

		/// <summary>
		/// MembershipProvider.GetUserNameByEmail
		/// </summary>
		public override string GetUserNameByEmail(string email) {
			string username = string.Empty;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1} FROM {0} WHERE {2} = @Email AND {3} = @ApplicationName",
						properties.TableName, properties.UserName, properties.Email, properties.ApplicationName);

					dbCommand.Parameters.Add("@Email", NpgsqlDbType.Varchar, 128).Value = email;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						username = (dbCommand.ExecuteScalar() as string) ?? string.Empty;
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			return username;
		}

		/// <summary>
		/// MembershipProvider.ResetPassword
		/// </summary>
		public override string ResetPassword(string username, string answer) {
			if (!settings.m_enablePasswordReset) {
				throw new NotSupportedException(Properties.Resources.ErrPasswordResetNotEnabled);
			}

			if (string.IsNullOrEmpty(answer) && settings.m_requiresQuestionAndAnswer) {
				UpdateFailureCount(username, FailureType.PasswordAnswer);

				throw new ProviderException(Properties.Resources.ErrPasswordAnswerRequired);
			}

			string newPassword = Membership.GeneratePassword(settings.NewPasswordLengthRequirement,
				settings.m_minRequiredNonAlphanumericCharacters);


			ValidatePasswordEventArgs args = new ValidatePasswordEventArgs(username, newPassword, true);

			OnValidatingPassword(args);

			if (args.Cancel) {
				if (args.FailureInformation != null)
					throw args.FailureInformation;
				else
					throw new MembershipPasswordException(Properties.Resources.ErrPasswordResetCanceled);
			}

			int rowsAffected = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2} FROM {0} WHERE {3} = @Username AND {4} = @ApplicationName",
						properties.TableName, properties.PasswordAnswer, properties.IsLockedOut, properties.UserName,
						properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						string passwordAnswer = string.Empty;

						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								reader.Read();

								passwordAnswer = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
								;
								bool isLockedOut = reader.IsDBNull(1) ? false : reader.GetBoolean(1);

								reader.Close();

								if (isLockedOut)
									throw new MembershipPasswordException(Properties.Resources.ErrUserIsLoggedOut);

								if (settings.m_requiresQuestionAndAnswer && !CheckPassword(answer, passwordAnswer)) {
									UpdateFailureCount(username, FailureType.PasswordAnswer);

									throw new MembershipPasswordException(Properties.Resources.ErrIncorrectPasswordAnswer);
								}
							} else {
								throw new MembershipPasswordException(Properties.Resources.ErrUserNotFound);
							}
						}

						// Reset Password
						using (NpgsqlCommand dbUpdateCommand = dbConn.CreateCommand()) {
							dbUpdateCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
								"UPDATE {0} SET {1} = @Password, {2} = @LastPasswordChangedDate WHERE {3} = @Username AND {4} = @ApplicationName AND {5} = @IsLockedOut",
								properties.TableName, properties.Password, properties.LastPasswordChangedDate, properties.UserName,
								properties.ApplicationName, properties.IsLockedOut);

							dbUpdateCommand.Parameters.Add("@Password", NpgsqlDbType.Varchar, 128).Value = EncodePassword(newPassword);
							dbUpdateCommand.Parameters.Add("@LastPasswordChangedDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
							dbUpdateCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
							dbUpdateCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;
							dbUpdateCommand.Parameters.Add("@IsLockedOut", NpgsqlDbType.Boolean).Value = false;

							PrepareStatementIfEnabled(dbUpdateCommand);

							rowsAffected = dbUpdateCommand.ExecuteNonQuery();
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

			if (rowsAffected > 0)
				return newPassword;

			else
				throw new MembershipPasswordException(Properties.Resources.ErrPasswordResetAborted);
		}

		/// <summary>
		/// MembershipProvider.UnlockUser
		/// </summary>
		public override bool UnlockUser(string userName) {
			int rowsAffected = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"UPDATE  {0} SET {1} = @IsLockedOut, {2} = @LastLockedOutDate WHERE {3} = @Username AND {4} = @ApplicationName",
						properties.TableName, properties.IsLockedOut, properties.LastLockedOutDate, properties.UserName,
						properties.ApplicationName);

					dbCommand.Parameters.Add("@IsLockedOut", NpgsqlDbType.Boolean).Value = false;
					dbCommand.Parameters.Add("@LastLockedOutDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = userName;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						rowsAffected = dbCommand.ExecuteNonQuery();
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());
						throw new ProviderException(e.Message);
					} finally {
						if (dbConn != null)
							dbConn.Close();
					}
				}
			}

			if (rowsAffected > 0)
				return true;

			else
				return false;
		}

		/// <summary>
		/// MembershipProvider.UpdateUser
		/// </summary>
		public override void UpdateUser(MembershipUser user) {
			// validate duplicate email address, see issue #29
			if (RequiresUniqueEmail && !string.IsNullOrEmpty(GetUserNameByEmail(user.Email)))
				throw new ProviderException("Duplicate E-mail address. The E-mail supplied is invalid.");

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"UPDATE {0} SET {1} = @Email, {2} = @Comment, {3} = @IsApproved WHERE {4} = @Username AND {5} = @ApplicationName",
						properties.TableName, properties.Email, properties.Comment, properties.IsApproved, properties.UserName,
						properties.ApplicationName);

					dbCommand.Parameters.Add("@Email", NpgsqlDbType.Varchar, 128).Value = user.Email;
					dbCommand.Parameters.Add("@Comment", NpgsqlDbType.Varchar, 255).Value = user.Comment;
					dbCommand.Parameters.Add("@IsApproved", NpgsqlDbType.Boolean).Value = user.IsApproved;
					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = user.UserName;
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
		/// MembershipProvider.ValidateUser
		/// </summary>
		public override bool ValidateUser(string username, string password) {
			string dbPassword = string.Empty;
			bool dbIsApproved = false;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				// Fetch user data from database
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2} FROM {0} WHERE {3} = @Username AND {4} = @ApplicationName AND {5} = @IsLockedOut",
						properties.TableName, properties.Password, properties.IsApproved, properties.UserName, properties.ApplicationName,
						properties.IsLockedOut);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;
					dbCommand.Parameters.Add("@IsLockedOut", NpgsqlDbType.Boolean).Value = false;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								reader.Read();
								dbPassword = reader.GetString(0);
								dbIsApproved = reader.IsDBNull(1) ? false : reader.GetBoolean(1);
							} else {
								return false;
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

				if (CheckPassword(password, dbPassword)) {
					if (dbIsApproved) {
						// Update last login date
						using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
							MembershipTableProperties properties = settings.TableProperties;
							dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
								"UPDATE {0} SET {1} = @LastLoginDate WHERE {2} = @Username AND {3} = @ApplicationName",
								properties.TableName, properties.LastLoginDate, properties.UserName, properties.ApplicationName);

							dbCommand.Parameters.Add("@LastLoginDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
							dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
							dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

							try {
								dbConn.Open();
								PrepareStatementIfEnabled(dbCommand);

								dbCommand.ExecuteNonQuery();

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
				}

				return false;
			}
		}

		#endregion

		#region private methods

		/// <summary>
		/// A helper function that takes the current row from the NpgsqlDataReader
		/// and hydrates a MembershipUser from the values. Called by the 
		/// MembershipUser.GetUser implementation.
		/// </summary>
		/// <param name="reader">NpgsqlDataReader object</param>
		/// <returns>MembershipUser object</returns>
		private MembershipUser GetUserFromReader(NpgsqlDataReader reader) {
			object providerUserKey = reader.GetValue(0);
			string username = reader.GetString(1);
			string email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
			string passwordQuestion = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
			string comment = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
			bool isApproved = reader.IsDBNull(5) ? false : reader.GetBoolean(5);
			bool isLockedOut = reader.IsDBNull(6) ? false : reader.GetBoolean(6);
			DateTime creationDate = reader.IsDBNull(7) ? DateTime.MinValue : reader.GetDateTime(7);
			DateTime lastLoginDate = reader.IsDBNull(8) ? DateTime.MinValue : reader.GetDateTime(8);
			DateTime lastActivityDate = reader.IsDBNull(9) ? DateTime.MinValue : reader.GetDateTime(9);
			DateTime lastPasswordChangedDate = reader.IsDBNull(10) ? DateTime.MinValue : reader.GetDateTime(10);
			DateTime lastLockedOutDate = reader.IsDBNull(11) ? DateTime.MinValue : reader.GetDateTime(11);

			return new MembershipUser(this.Name,
				username,
				providerUserKey,
				email,
				passwordQuestion,
				comment,
				isApproved,
				isLockedOut,
				creationDate,
				lastLoginDate,
				lastActivityDate,
				lastPasswordChangedDate,
				lastLockedOutDate);
		}

		/// <summary>
		/// Compares password values based on the MembershipPasswordFormat.
		/// </summary>
		/// <param name="password"></param>
		/// <param name="dbpassword"></param>
		/// <returns></returns>
		private bool CheckPassword(string password, string dbpassword) {
			string pass1 = password;
			string pass2 = dbpassword;

			switch (PasswordFormat) {
				case MembershipPasswordFormat.Encrypted:
					pass2 = UnEncodePassword(dbpassword);
					break;

				case MembershipPasswordFormat.Hashed:
					pass1 = EncodePassword(password);
					break;

				default:
					break;
			}

			if (pass1.Equals(pass2))
				return true;
			else
				return false;
		}

		/// <summary>
		/// Encrypts, Hashes, or leaves the password clear based on the PasswordFormat.
		/// </summary>
		/// <param name="password"></param>
		/// <returns></returns>
		private string EncodePassword(string password) {
			if (string.IsNullOrEmpty(password))
				return password;

			string encodedPassword = password;

			switch (PasswordFormat) {
				case MembershipPasswordFormat.Clear:
					break;

				case MembershipPasswordFormat.Encrypted:
					encodedPassword = Convert.ToBase64String(EncryptPassword(Encoding.Unicode.GetBytes(password)));
					break;

				case MembershipPasswordFormat.Hashed:
					HMACSHA1 hash = new HMACSHA1();
					hash.Key = HexToByte(settings.MachineKeyConfig.ValidationKey);
					encodedPassword = Convert.ToBase64String(hash.ComputeHash(Encoding.Unicode.GetBytes(password)));
					break;

				default:
					throw new ProviderException(Properties.Resources.ErrPwFormatNotSupported);
			}

			return encodedPassword;
		}

		/// <summary>
		/// Decrypts or leaves the password clear based on the PasswordFormat.
		/// </summary>
		/// <param name="encodedPassword"></param>
		/// <returns></returns>
		private string UnEncodePassword(string encodedPassword) {
			string password = encodedPassword;

			switch (PasswordFormat) {
				case MembershipPasswordFormat.Clear:
					break;

				case MembershipPasswordFormat.Encrypted:
					password = Encoding.Unicode.GetString(DecryptPassword(Convert.FromBase64String(password)));
					break;

				case MembershipPasswordFormat.Hashed:
					throw new ProviderException(Properties.Resources.ErrCantDecodeHashedPw);

				default:
					throw new ProviderException(Properties.Resources.ErrPwFormatNotSupported);
			}

			return password;
		}

		/// <summary>
		/// Converts a hexadecimal string to a byte array. Used to convert encryption
		/// key values from the configuration.
		/// </summary>
		/// <param name="hexString"></param>
		/// <returns></returns>
		private static byte[] HexToByte(string hexString) {
			byte[] returnBytes = new byte[hexString.Length/2];
			for (int i = 0; i < returnBytes.Length; i++)
				returnBytes[i] = Convert.ToByte(hexString.Substring(i*2, 2), 16);

			return returnBytes;
		}

		/// <summary>
		/// A helper method that performs the checks and updates associated with
		/// password failure tracking.
		/// </summary>
		/// <param name="username"></param>
		/// <param name="failType"></param>
		private void UpdateFailureCount(string username, FailureType failType) {
			DateTime windowStart = new DateTime();
			int failureCount = 0;

			using (NpgsqlConnection dbConn = new NpgsqlConnection(settings.ConnectionString)) {
				// Fetch user data from database
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					MembershipTableProperties properties = settings.TableProperties;
					dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
						"SELECT {1}, {2}, {3}, {4} FROM {0} WHERE {5} = @Username AND {6} = @ApplicationName",
						properties.TableName, properties.FailedPasswordAttemptCount, properties.FailedPasswordAttemptWindowStart,
						properties.FailedPasswordAnswerAttemptCount, properties.FailedPasswordAnswerAttemptWindowStart,
						properties.UserName, properties.ApplicationName);

					dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
					dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

					try {
						dbConn.Open();
						PrepareStatementIfEnabled(dbCommand);

						using (NpgsqlDataReader reader = dbCommand.ExecuteReader()) {
							if (reader.HasRows) {
								reader.Read();

								if (failType.Equals(FailureType.Password)) {
									failureCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
									windowStart = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1);
								} else if (failType.Equals(FailureType.PasswordAnswer)) {
									failureCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
									windowStart = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);
								}
							}
						}
					} catch (NpgsqlException e) {
						Trace.WriteLine(e.ToString());

						if (dbConn != null)
							dbConn.Close();

						throw new ProviderException(e.Message);
					}
				}

				// Calculate failture count and update database
				using (NpgsqlCommand dbCommand = dbConn.CreateCommand()) {
					DateTime windowEnd = windowStart.AddMinutes(settings.m_passwordAttemptWindow);

					try {
						MembershipTableProperties properties = settings.TableProperties;
						if (failureCount == 0 || DateTime.Now > windowEnd) {
							// First password failure or outside of PasswordAttemptWindow. 
							// Start a new password failure count from 1 and a new window starting now.
							if (failType.Equals(FailureType.Password)) {
								dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
									"UPDATE {0} SET {1} = @Count, {2} = @WindowStart WHERE {3} = @Username AND {4} = @ApplicationName",
									properties.TableName, properties.FailedPasswordAttemptCount, properties.FailedPasswordAttemptWindowStart,
									properties.UserName, properties.ApplicationName);
							} else if (failType.Equals(FailureType.PasswordAnswer)) {
								dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
									"UPDATE {0} SET {1} = @Count, {2} = @WindowStart WHERE {3} = @Username AND {4} = @ApplicationName",
									properties.TableName, properties.FailedPasswordAnswerAttemptCount,
									properties.FailedPasswordAnswerAttemptWindowStart, properties.UserName, properties.ApplicationName);
							}

							dbCommand.Parameters.Add("@Count", NpgsqlDbType.Integer).Value = 1;
							dbCommand.Parameters.Add("@WindowStart", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
							dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
							dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

							if (dbCommand.ExecuteNonQuery() < 0)
								throw new ProviderException(Properties.Resources.ErrCantUpdateFailtureCountAndWindowStart);
						} else {
							failureCount++;

							if (failureCount >= settings.m_maxInvalidPasswordAttempts) {
								// Password attempts have exceeded the failure threshold. Lock out
								// the user.
								dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
									"UPDATE {0} SET {1} = @IsLockedOut, {2} = @LastLockedOutDate WHERE {3} = @Username AND {4} = @ApplicationName",
									properties.TableName, properties.IsLockedOut, properties.LastLockedOutDate, properties.UserName,
									properties.ApplicationName);

								dbCommand.Parameters.Add("@IsLockedOut", NpgsqlDbType.Boolean).Value = true;
								dbCommand.Parameters.Add("@LastLockedOutDate", NpgsqlDbType.TimestampTZ).Value = DateTime.Now;
								dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
								dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

								if (dbCommand.ExecuteNonQuery() < 0)
									throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrCantLogoutUser,
										username));
							} else {
								// Password attempts have not exceeded the failure threshold. Update
								// the failure counts. Leave the window the same.
								if (failType.Equals(FailureType.Password)) {
									dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
										"UPDATE {0} SET {1} = @Count WHERE {2} = @Username AND {3} = @ApplicationName",
										properties.TableName, properties.FailedPasswordAttemptCount, properties.UserName, properties.ApplicationName);
								} else if (failType.Equals(FailureType.PasswordAnswer)) {
									dbCommand.CommandText = string.Format(CultureInfo.InvariantCulture,
										"UPDATE {0} SET {1} = @Count WHERE {2} = @Username AND {3} = @ApplicationName",
										properties.TableName, properties.FailedPasswordAnswerAttemptCount, properties.UserName,
										properties.ApplicationName);
								}

								dbCommand.Parameters.Add("@Count", NpgsqlDbType.Integer).Value = failureCount;
								dbCommand.Parameters.Add("@Username", NpgsqlDbType.Varchar, 255).Value = username;
								dbCommand.Parameters.Add("@ApplicationName", NpgsqlDbType.Varchar, 255).Value = ApplicationName;

								if (dbCommand.ExecuteNonQuery() < 0)
									throw new ProviderException(Properties.Resources.ErrCantUpdateFailtureCount);
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
		}

		private enum FailureType {
			Password,
			PasswordAnswer
		}

		private void PrepareStatementIfEnabled(NpgsqlCommand dbCommand) {
			if (settings.UsePreparedStatements) {
				dbCommand.Prepare();
			}
		}

		#endregion
	}
}