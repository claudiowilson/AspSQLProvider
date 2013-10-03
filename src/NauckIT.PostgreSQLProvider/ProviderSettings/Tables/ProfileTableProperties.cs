using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	class ProfileTableProperties : SQLTable {
		public string ProfileID,
			Username,
			ApplicationName,
			IsAnonymous,
			LastActivityDate,
			LastUpdatedDate;
	}
}
