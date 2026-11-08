// Profiles.cs - namespace SkinDevApp.Data
//
//   AccountProfile   the person who signs in to LUMYVUE is the reviewing dermatologist: their personal details (the same set the patient
//                    registration asks for, without anything medical), contact and professional details are saved with the account
//                    (existing ResearcherAccounts table, additive columns) and shown on their dashboard.
using System;
using System.Globalization;

namespace SkinDevApp.Data
{
    public sealed class AccountProfile
    {
        public string Username { get; set; }
        public string Title { get; set; }             // e.g. Dr.
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string DateOfBirth { get; set; }       // yyyy-MM-dd
        public string Sex { get; set; }
        public string Birthplace { get; set; }
        public string CivilStatus { get; set; }
        public string Citizenship { get; set; }
        public string HomeAddress { get; set; }
        public string Phone { get; set; }             // cell phone
        public string HomeTel { get; set; }
        public string Email { get; set; }
        public string JobTitle { get; set; }          // specialty or role
        public string Registration { get; set; }      // professional registration / license number
        public string Clinic { get; set; }
        public string Availability { get; set; }
        public string AccountType { get; set; }        // Dermatologist | Administrator (empty = an account made before types existed: Administrator)
        public bool IsAdministrator => string.IsNullOrWhiteSpace(AccountType) || AccountType == "Administrator";
        public string FullNameStored { get; set; }    // the name as saved (also kept for accounts made before the detailed form)

        /// <summary>"Dr. Maria S. Santos" from the parts, or the stored name when the parts are empty.</summary>
        public string FullName
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title.Trim());
                if (!string.IsNullOrWhiteSpace(FirstName)) parts.Add(FirstName.Trim());
                if (!string.IsNullOrWhiteSpace(MiddleName)) parts.Add(MiddleName.Trim());
                if (!string.IsNullOrWhiteSpace(LastName)) parts.Add(LastName.Trim());
                return parts.Count > 0 && !string.IsNullOrWhiteSpace(LastName) ? string.Join(" ", parts) : FullNameStored;
            }
        }

        public DateTime? BirthDate
        {
            get
            {
                DateTime d;
                return DateTime.TryParseExact(DateOfBirth ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (DateTime?)d : null;
            }
        }

        public int? Age
        {
            get
            {
                DateTime? b = BirthDate;
                if (!b.HasValue) return null;
                DateTime t = DateTime.Today;
                int a = t.Year - b.Value.Year;
                if (b.Value.Date > t.AddYears(-a)) a--;
                return a;
            }
        }

        public bool IsComplete => Check(this) == null;

        public static string Check(AccountProfile p)
        {
            if (string.IsNullOrWhiteSpace(p.LastName)) return "Please enter your family name.";
            if (string.IsNullOrWhiteSpace(p.FirstName)) return "Please enter your given name.";
            DateTime? b = p.BirthDate;
            if (!b.HasValue) return "Please enter your birthday.";
            if (b.Value.Date > DateTime.Today) return "The birthday cannot be in the future.";
            int? age = p.Age;
            if (age < 18 || age > 120) return "Please check the birthday: the age comes out as " + age + ".";
            if (string.IsNullOrWhiteSpace(p.Sex)) return "Please choose your sex.";
            if (string.IsNullOrWhiteSpace(p.Birthplace)) return "Please enter your birthplace.";
            if (string.IsNullOrWhiteSpace(p.CivilStatus)) return "Please choose your civil status.";
            if (string.IsNullOrWhiteSpace(p.Citizenship)) return "Please enter your citizenship.";
            if (string.IsNullOrWhiteSpace(p.HomeAddress)) return "Please enter your home address.";
            if (string.IsNullOrWhiteSpace(p.Phone)) return "Please enter your cell phone number.";
            if (!IsPhone(p.Phone.Trim())) return "That cell phone number does not look right.";
            if (!string.IsNullOrWhiteSpace(p.HomeTel) && !IsPhone(p.HomeTel.Trim())) return "That home telephone number does not look right.";
            if (!string.IsNullOrWhiteSpace(p.Email) && !IsEmail(p.Email.Trim())) return "That e-mail address does not look right.";
            if (string.IsNullOrWhiteSpace(p.JobTitle)) return "Please enter your specialty or role.";
            if (string.IsNullOrWhiteSpace(p.Registration)) return "Please enter your professional registration or license number.";
            return null;
        }

        public static bool IsEmail(string s)
        {
            int at = s.IndexOf('@');
            return at > 0 && at == s.LastIndexOf('@') && s.IndexOf('.', at) > at + 1 && !s.EndsWith(".") && !s.Contains(" ");
        }

        public static bool IsPhone(string s)
        {
            int digits = 0;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) digits++;
                else if ("+-() .".IndexOf(c) < 0) return false;
            }
            return digits >= 7 && digits <= 15;
        }
    }

    public static class AccountProfiles
    {
        public static readonly string[] Columns = { "Title", "FirstName", "MiddleName", "LastName", "DateOfBirth", "Sex", "Birthplace", "CivilStatus", "Citizenship", "HomeAddress", "HomeTel",
                                                    "FullName", "JobTitle", "Email", "Phone", "Registration", "Clinic", "Availability", "AccountType" };

        private static string S(System.Data.SQLite.SQLiteDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));

        public static AccountProfile Get(string username)
        {
            var p = new AccountProfile { Username = username };
            if (string.IsNullOrWhiteSpace(username)) return p;
            try
            {
                using (var c = StudyDatabase.Open())
                using (var cmd = StudyRepository.Cmd(c, "SELECT " + string.Join(", ", Columns) + " FROM ResearcherAccounts WHERE Username=@u;", null, "@u", username))
                using (var r = cmd.ExecuteReader())
                    if (r.Read())
                    {
                        p.Title = S(r, 0); p.FirstName = S(r, 1); p.MiddleName = S(r, 2); p.LastName = S(r, 3); p.DateOfBirth = S(r, 4); p.Sex = S(r, 5);
                        p.Birthplace = S(r, 6); p.CivilStatus = S(r, 7); p.Citizenship = S(r, 8); p.HomeAddress = S(r, 9); p.HomeTel = S(r, 10);
                        p.FullNameStored = S(r, 11); p.JobTitle = S(r, 12); p.Email = S(r, 13); p.Phone = S(r, 14); p.Registration = S(r, 15); p.Clinic = S(r, 16); p.Availability = S(r, 17); p.AccountType = S(r, 18);
                    }
            }
            catch { }
            return p;
        }

        private static string N(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public static void Save(AccountProfile p)
        {
            string err = AccountProfile.Check(p);
            if (err != null) throw new InvalidOperationException(err);
            using (var c = StudyDatabase.Open())
            using (var tx = c.BeginTransaction())
            {
                using (var cmd = StudyRepository.Cmd(c, @"UPDATE ResearcherAccounts SET Title=@t, FirstName=@fn, MiddleName=@mn, LastName=@ln, DateOfBirth=@dob, Sex=@sx, Birthplace=@bp,
                        CivilStatus=@cs, Citizenship=@ct, HomeAddress=@ha, HomeTel=@ht, FullName=@full, JobTitle=@j, Email=@e, Phone=@p, Registration=@r, Clinic=@cl, Availability=@a
                        WHERE Username=@u;", tx,
                    "@t", N(p.Title), "@fn", N(p.FirstName), "@mn", N(p.MiddleName), "@ln", N(p.LastName), "@dob", N(p.DateOfBirth), "@sx", N(p.Sex), "@bp", N(p.Birthplace),
                    "@cs", N(p.CivilStatus), "@ct", N(p.Citizenship), "@ha", N(p.HomeAddress), "@ht", N(p.HomeTel), "@full", p.FullName, "@j", N(p.JobTitle),
                    "@e", N(p.Email), "@p", N(p.Phone), "@r", N(p.Registration), "@cl", N(p.Clinic), "@a", N(p.Availability), "@u", p.Username))
                    cmd.ExecuteNonQuery();
                StudyRepository.Audit(c, tx, "Account profile saved", "Account", p.Username, p.FullName);
                tx.Commit();
            }
        }
    }
}
