using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.Security;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	public class MembershipProviderSettings : ProviderSettings {
		public int NewPasswordLengthRequirement = 8;
		public MembershipTableProperties TableProperties;
		public MachineKeySection MachineKeyConfig { get; set; }
		public bool UsePreparedStatements = false;
		#region System.Web.Security.MembershipProvider property variables
		public string m_applicationName = string.Empty;
		public bool m_enablePasswordReset = false;
		public bool m_enablePasswordRetrieval = false;
		public bool m_requiresQuestionAndAnswer = false;
		public bool m_requiresUniqueEmail = false;
		public int m_maxInvalidPasswordAttempts = 0;
		public int m_passwordAttemptWindow = 0;
		public MembershipPasswordFormat m_passwordFormat = MembershipPasswordFormat.Clear;
		public int m_minRequiredNonAlphanumericCharacters = 0;
		public int m_minRequiredPasswordLength = 0;
		public string m_passwordStrengthRegularExpression = string.Empty;
		#endregion


		public MembershipProviderSettings(NameValueCollection config) 
		{
			m_connectionString = ConfigurationParser.GetConnectionString(config["connectionStringName"]);
			InitTableProperties();
			SetPasswordEncryption(config);
			InitMachineKeyConfiguration();
			InitMembershipProviderProperties(config);
		}

		public void SetPasswordEncryption(NameValueCollection config) 
		{
			string pwFormat = ConfigurationParser.GetConfigValue(config["passwordFormat"], "Hashed");
            switch (pwFormat)
            {
                case "Hashed":
					m_passwordFormat = MembershipPasswordFormat.Hashed;
                    break;
                case "Encrypted":
					m_passwordFormat = MembershipPasswordFormat.Encrypted;
                    break;
                case "Clear":
					m_passwordFormat = MembershipPasswordFormat.Clear;
                    break;
                default:
                    throw new ProviderException(Properties.Resources.ErrPwFormatNotSupported);
            }
		}

		private void InitTableProperties() {
			TableProperties = new MembershipTableProperties {
				ApplicationName = "application_name",
				Comment = "comment",
				Email = "email",
				CreationDate = "creation_date",
				FailedPasswordAnswerAttemptWindowStart = "failed_password_answer_attempt_window_start",
				FailedPasswordAttemptCount = "failed_password_attempt_count",
				FailedPasswordAnswerAttemptCount = "failed_password_answer_attempt_count",
				IsApproved = "is_approved",
				FailedPasswordAttemptWindowStart = "failed_password_attempt_window_start",
				IsLockedOut = "is_locked_out",
				IsOnline = "is_online",
				UserId = "user_id",
				UserName = "username",
				LastActivityDate = "last_activity_date",
				LastLoginDate = "last_login_date",
				LastLockedOutDate = "last_lock_out_date",
				LastPasswordChangedDate = "last_password_change_date",
				Password = "password",
				PasswordAnswer = "password_answer",
				PasswordQuestion = "password_question",
				TableName = "test.user"
			};
		}

		private void InitMembershipProviderProperties(NameValueCollection config) {
			m_applicationName = ConfigurationParser.GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);
			m_maxInvalidPasswordAttempts = Convert.ToInt32(ConfigurationParser.GetConfigValue(config["maxInvalidPasswordAttempts"], "5"), CultureInfo.InvariantCulture);
			m_passwordAttemptWindow = Convert.ToInt32(ConfigurationParser.GetConfigValue(config["passwordAttemptWindow"], "10"), CultureInfo.InvariantCulture);
			m_minRequiredNonAlphanumericCharacters = Convert.ToInt32(ConfigurationParser.GetConfigValue(config["minRequiredNonAlphanumericCharacters"], "1"), CultureInfo.InvariantCulture);
			m_minRequiredPasswordLength = Convert.ToInt32(ConfigurationParser.GetConfigValue(config["minRequiredPasswordLength"], "7"), CultureInfo.InvariantCulture);
			m_passwordStrengthRegularExpression = ConfigurationParser.GetConfigValue(config["passwordStrengthRegularExpression"], "");
			m_enablePasswordReset = Convert.ToBoolean(ConfigurationParser.GetConfigValue(config["enablePasswordReset"], "true"), CultureInfo.InvariantCulture);
			m_enablePasswordRetrieval = Convert.ToBoolean(ConfigurationParser.GetConfigValue(config["enablePasswordRetrieval"], "true"), CultureInfo.InvariantCulture);
			m_requiresQuestionAndAnswer = Convert.ToBoolean(ConfigurationParser.GetConfigValue(config["requiresQuestionAndAnswer"], "false"), CultureInfo.InvariantCulture);
			m_requiresUniqueEmail = Convert.ToBoolean(ConfigurationParser.GetConfigValue(config["requiresUniqueEmail"], "true"), CultureInfo.InvariantCulture);
		}

		private void InitMachineKeyConfiguration() {
			// Check whether we are on a web hosted application or not
			// If we're web hosted use the Web.config; otherwise the application's config file.
			// Then get encryption and decryption key information from the configuration.
			MachineKeyConfig = HostingEnvironment.IsHosted
									? WebConfigurationManager.GetSection("system.web/machineKey") as MachineKeySection
									: ConfigurationManager.GetSection("system.web/machineKey") as MachineKeySection;

			if (!m_passwordFormat.Equals(MembershipPasswordFormat.Clear)) {
				if (MachineKeyConfig == null)
					throw new ProviderException(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ErrConfigSectionNotFound, "system.web/machineKey"));

				if (MachineKeyConfig.ValidationKey.Contains("AutoGenerate"))
					throw new ProviderException(Properties.Resources.ErrAutoGeneratedKeyNotSupported);
			}
		}
	}
}
