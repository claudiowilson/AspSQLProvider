using System;
using System.Collections.Generic;
using System.Configuration;
using System.Configuration.Provider;
using System.Text;

namespace NauckIT.PostgreSQLProvider {
	public class ConfigurationParser {
		/// <summary>
		/// A helper function to retrieve config values from the configuration file.
		/// </summary>
		/// <param name="configValue"></param>
		/// <param name="defaultValue"></param>
		/// <returns></returns>
		internal static string GetConfigValue(string configValue, string defaultValue) 
		{
			if (string.IsNullOrEmpty(configValue))
				return defaultValue;
			return configValue;
		}

		/// <summary>
		/// A helper function to retrieve the connecion string from the configuration file
		/// </summary>
		/// <param name="connectionStringName">Name of the connection string</param>
		/// <returns></returns>
		internal static string GetConnectionString(string connectionStringName) 
		{
			if (string.IsNullOrEmpty(connectionStringName))
				throw new ArgumentException(Properties.Resources.ErrArgumentNullOrEmpty, "connectionStringName");

			ConnectionStringSettings ConnectionStringSettings = ConfigurationManager.ConnectionStrings[connectionStringName];

			if (ConnectionStringSettings == null || string.IsNullOrEmpty(ConnectionStringSettings.ConnectionString.Trim()))
				throw new ProviderException(Properties.Resources.ErrConnectionStringNullOrEmpty);

			return ConnectionStringSettings.ConnectionString;
		}



	}
}
