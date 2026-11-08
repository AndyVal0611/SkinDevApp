// ============================================================================
// AppSession.cs  -  namespace SkinDevApp.Data
//
// Who is using the app and which participant the workflow is on.
//
//   Operator   - kiosk / client portal: registration, consent, scanning, results.
//   Researcher - signed-in specialist: everything above plus records, research
//                dashboard, verification, model information and settings.
// ============================================================================

namespace SkinDevApp.Data
{
    public enum UserRole
    {
        Operator,
        Researcher
    }

    public static class AppSession
    {
        public static UserRole Role { get; private set; } = UserRole.Operator;
        public static string UserId { get; private set; } = "";

        /// <summary>Participant the registration -> consent -> scan workflow is on (null = none selected).</summary>
        public static string CurrentParticipantId { get; set; }

        public static bool IsResearcher => Role == UserRole.Researcher;

        /// <summary>True for administrator accounts (and accounts made before account types existed). Only they create accounts.</summary>
        public static bool IsAdministrator
        {
            get
            {
                if (string.IsNullOrWhiteSpace(UserId)) return false;
                try { return AccountProfiles.Get(UserId).IsAdministrator; } catch { return false; }
            }
        }

        public static string RoleTitle => IsResearcher ? "Researcher / Admin" : "Operator";

        /// <summary>Recorded in the audit log and as OperatorID on new rows.</summary>
        public static string ActorId =>
            string.IsNullOrWhiteSpace(UserId) ? (IsResearcher ? "researcher" : "operator") : UserId;

        public static void SignIn(UserRole role, string userId)
        {
            Role = role;
            UserId = (userId ?? "").Trim();
            CurrentParticipantId = null;
        }

        public static void SignOut()
        {
            Role = UserRole.Operator;
            UserId = "";
            CurrentParticipantId = null;
        }
    }
}
