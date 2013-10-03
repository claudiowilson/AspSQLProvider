using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web.Hosting;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	class ProfileProviderSettings : ProviderSettings {
		public ProfileTableProperties ProfileTableProperties;
		public ProfileDataTableProperties ProfileDataProperties;
		public bool UsePreparedStatements = false;
		#region System.Web.Security.ProfileProvider properties
		public string m_applicationName = string.Empty;
		#endregion

		public ProfileProviderSettings(NameValueCollection config) {
			ProfileTableProperties = new ProfileTableProperties {
				TableName = "test.profile",
				ApplicationName = "application_name",
				IsAnonymous = "is_anonymous",
				LastActivityDate = "last_activity_date",
				LastUpdatedDate = "last_updated_date",
				ProfileID = "profile_id",
				Username = "username"
			};

			ProfileDataProperties = new ProfileDataTableProperties {
				Name = "name",
				Profile = "profile",
				ProfileDataId = "profile_data_id",
				TableName = "test.profile_data",
				ValueBinary = "value_binary",
				ValueString = "value_string",
			};

			m_applicationName = ConfigurationParser.GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);

			m_connectionString = ConfigurationParser.GetConnectionString(config["connectionStringName"]);
		}
	}
}
