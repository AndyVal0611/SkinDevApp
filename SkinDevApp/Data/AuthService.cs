// ============================================================================
// AuthService.cs  -  namespace SkinDevApp.Data
//
// Local researcher / admin accounts. Replaces the credentials that used to be written
// in the source code.
//
//   * Passwords are never stored: only a random per-account salt and a PBKDF2-SHA256
//     hash (150,000 iterations).
//   * There is no default account. The first time a researcher signs in, the login
//     screen asks them to create the first account.
//   * Five wrong passwords for one username lock that username for 60 seconds. Failed
//     and successful sign-ins are written to the audit log.
//
// This protects the app's researcher screens on a shared kiosk. It is not a substitute
// for protecting the computer itself (Windows account, BitLocker).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SkinDevApp.Data
{
    public enum SignInResult
    {
        Ok,
        Invalid,
        LockedOut
    }

    public static class AuthService
    {
        public const int MinPasswordLength = 8;
        private const int Iterations = 150000;
        private const int MaxFailures = 5;
        private static readonly TimeSpan LockFor = TimeSpan.FromSeconds(60);

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> Failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LockedUntil = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // -------------------------------------------------------------- queries --

        public static bool HasAccounts()
        {
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT COUNT(*) FROM ResearcherAccounts;"))
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public static List<string> Usernames()
        {
            var list = new List<string>();
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT Username FROM ResearcherAccounts ORDER BY Username;"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) list.Add(r.GetString(0));
            return list;
        }

        /// <summary>Seconds this username remains locked (0 = not locked).</summary>
        public static int LockedSeconds(string username)
        {
            lock (Gate)
            {
                DateTime until;
                if (!string.IsNullOrEmpty(username) && LockedUntil.TryGetValue(username.Trim(), out until) && until > DateTime.UtcNow)
                    return (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds);
                return 0;
            }
        }

        // ------------------------------------------------------------- password --

        /// <summary>Null when the password is acceptable, otherwise the reason it is not.</summary>
        public static string CheckNewPassword(string username, string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
                return "The password must be at least " + MinPasswordLength + " characters long.";
            if (!string.IsNullOrEmpty(username) && string.Equals(password, username.Trim(), StringComparison.OrdinalIgnoreCase))
                return "The password must not be the same as the username.";
            return null;
        }

        public static string CheckUsername(string username)
        {
            string u = (username ?? "").Trim();
            if (u.Length < 3) return "The username must be at least 3 characters long.";
            if (u.Length > 40) return "The username must be 40 characters or fewer.";
            foreach (char ch in u)
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-'))
                    return "The username may contain only letters, digits, '_', '.' and '-'.";
            return null;
        }

        private static byte[] Hash(string password, byte[] salt, int iterations)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(32);
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ------------------------------------------------------------- accounts --

        public static void CreateAccount(string username, string password, string createdBy)
        {
            string u = (username ?? "").Trim();
            string err = CheckUsername(u) ?? CheckNewPassword(u, password);
            if (err != null) throw new InvalidOperationException(err);

            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash = Hash(password, salt, Iterations);

            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                using (var cmd = StudyRepository.Cmd(c, "SELECT COUNT(*) FROM ResearcherAccounts WHERE Username=@u;", tx, "@u", u))
                    if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                        throw new InvalidOperationException("An account named '" + u + "' already exists.");

                using (var cmd = StudyRepository.Cmd(c, @"INSERT INTO ResearcherAccounts
                    (Username, Salt, Hash, Iterations, CreatedAt, CreatedBy, PasswordChangedAt)
                    VALUES (@u,@s,@h,@i,@at,@by,@at);", tx,
                    "@u", u, "@s", salt, "@h", hash, "@i", Iterations, "@at", StudyRepository.Now(), "@by", createdBy))
                    cmd.ExecuteNonQuery();

                StudyRepository.Audit(c, tx, "Account created", "ResearcherAccount", u, "created by " + (string.IsNullOrEmpty(createdBy) ? "first-run setup" : createdBy));
                tx.Commit();
            }
        }

        /// <summary>Check a username and password. Throttles repeated failures.</summary>
        public static SignInResult SignIn(string username, string password)
        {
            string u = (username ?? "").Trim();
            if (LockedSeconds(u) > 0) return SignInResult.LockedOut;

            bool ok = Verify(u, password);

            lock (Gate)
            {
                if (ok)
                {
                    Failures.Remove(u);
                    LockedUntil.Remove(u);
                }
                else
                {
                    int n;
                    Failures.TryGetValue(u, out n);
                    n++;
                    Failures[u] = n;
                    if (n >= MaxFailures)
                    {
                        LockedUntil[u] = DateTime.UtcNow + LockFor;
                        Failures[u] = 0;
                    }
                }
            }

            StudyRepository.Audit(ok ? "Sign in" : "Sign in failed", "ResearcherAccount", u, ok ? "researcher / admin" : "wrong username or password");
            return ok ? SignInResult.Ok : (LockedSeconds(u) > 0 ? SignInResult.LockedOut : SignInResult.Invalid);
        }

        private static bool Verify(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || password == null) { DummyWork(); return false; }

            byte[] salt = null, hash = null;
            int iterations = Iterations;
            using (var c = StudyDatabase.Open())
            using (var cmd = StudyRepository.Cmd(c, "SELECT Salt, Hash, Iterations FROM ResearcherAccounts WHERE Username=@u;", null, "@u", username))
            using (var r = cmd.ExecuteReader())
                if (r.Read())
                {
                    salt = (byte[])r["Salt"];
                    hash = (byte[])r["Hash"];
                    iterations = Convert.ToInt32(r["Iterations"]);
                }

            if (salt == null) { DummyWork(); return false; }       // same time whether or not the username exists
            return SameBytes(Hash(password, salt, iterations), hash);
        }

        private static void DummyWork() => Hash("x", new byte[16], Iterations);

        public static void ChangePassword(string username, string currentPassword, string newPassword)
        {
            string u = (username ?? "").Trim();
            if (!Verify(u, currentPassword)) throw new InvalidOperationException("The current password is not correct.");
            string err = CheckNewPassword(u, newPassword);
            if (err != null) throw new InvalidOperationException(err);

            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash = Hash(newPassword, salt, Iterations);

            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                using (var cmd = StudyRepository.Cmd(c, @"UPDATE ResearcherAccounts SET Salt=@s, Hash=@h, Iterations=@i, PasswordChangedAt=@at WHERE Username=@u;", tx,
                    "@s", salt, "@h", hash, "@i", Iterations, "@at", StudyRepository.Now(), "@u", u))
                    cmd.ExecuteNonQuery();
                StudyRepository.Audit(c, tx, "Password changed", "ResearcherAccount", u, "by the account holder");
                tx.Commit();
            }
        }
    }
}
