using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NauckIT.PostgreSQLProvider.ProviderSettings.Tables {
    internal class SessionTableProperties : SQLTable {
        public string SessionID,
            ApplicationName,
            Created,
            Expires,
            Timeout,
            Locked,
            LockId,
            LockDate,
            Data,
            Flags;
    }
}