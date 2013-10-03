using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.SessionState;
using NauckIT.PostgreSQLProvider.ProviderSettings.Tables;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
    internal class SessionStateStoreSettings : ProviderSettings {
        public SessionTableProperties SessionTableProperties;
        public bool UsePreparedStatements = false;
        public string m_applicationName = string.Empty;
        public SessionStateSection m_config { get; set; }


        public SessionStateStoreSettings(NameValueCollection config) {
            m_applicationName = ConfigurationParser.GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);

            // Get connection string.
            m_connectionString = ConfigurationParser.GetConnectionString(config["connectionStringName"]);

            // Get <sessionState> configuration element.
            m_config = (SessionStateSection)WebConfigurationManager.GetSection("system.web/sessionState");

            SessionTableProperties = new SessionTableProperties {
                ApplicationName = "application_name",
                Created = "created",
                Data = "data",
                Expires = "expires",
                Flags = "flag",
                LockDate = "lock_date",
                Locked = "locked",
                LockId = "lock_id",
                SessionID = "session_id",
                TableName = "test.session",
                Timeout = "timeout"
            };

        }

    }
}