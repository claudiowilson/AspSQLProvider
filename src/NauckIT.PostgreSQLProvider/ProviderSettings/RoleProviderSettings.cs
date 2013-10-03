using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web.Hosting;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	class RoleProviderSettings : ProviderSettings {
		public RoleTableProperties RoleTableProperties;
		public UsersInRolesTableProperties UsersRoleTableProperties;
		public bool UsePreparedStatements = false;
		#region System.Web.Security.RoleProvider variables
		public string m_applicationName = string.Empty;
		#endregion

		public RoleProviderSettings(NameValueCollection config) {
			RoleTableProperties = new RoleTableProperties {
				ApplicationName = "application_name",
				Rolename = "role_name",
				TableName = "test.role"
			};

			UsersRoleTableProperties = new UsersInRolesTableProperties {
				ApplicationName = "application_name",
				Rolename = "role_name",
				TableName = "test.user_in_role",
                Username = "username"
			};

			m_applicationName = ConfigurationParser.GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);
			m_connectionString = ConfigurationParser.GetConnectionString(config["connectionStringName"]);
		}
	}
}
