using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	public abstract class ProviderSettings {
		protected string m_connectionString { get; set; }

		public string ConnectionString {
			get { return m_connectionString; }
		}
	}
}
