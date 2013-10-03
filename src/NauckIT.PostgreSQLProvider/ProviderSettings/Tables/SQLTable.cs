using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	public abstract class SQLTable {
		private string _tableName;

		public string TableName {
			get { return _tableName; }
			set { _tableName = value; }
		}
	}
}
