using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NauckIT.PostgreSQLProvider.ProviderSettings {
	public class MembershipTableProperties : SQLTable {
		public string UserId,
			UserName,
			ApplicationName,
			Email,
			Comment,
			Password,
			PasswordQuestion,
			PasswordAnswer,
			IsApproved,
			LastActivityDate,
			LastLoginDate,
			LastPasswordChangedDate,
			CreationDate,
			IsOnline,
			IsLockedOut,
			LastLockedOutDate,
			FailedPasswordAttemptCount,
			FailedPasswordAttemptWindowStart,
			FailedPasswordAnswerAttemptCount,
			FailedPasswordAnswerAttemptWindowStart;
	}
}