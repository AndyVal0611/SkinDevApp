// RegistrationPage.xaml.cs - detailed participant registration with an automatic PatientID.
// New mode: Save Registration -> Continue to Consent. Edit mode (from Participant Records):
// explicit edit with a reason; scan results are never touched.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SkinDevApp.Data;
using SkinDevApp.Views;

namespace SkinDevApp
{
    public partial class RegistrationPage : Page
    {
        private readonly bool _editMode;
        private string _participantId;          // set once saved (new) or from the record (edit)
        private bool _loading = true;
        private bool _dirty;

        public RegistrationPage() : this(null) { }

        public RegistrationPage(string editParticipantId)
        {
            InitializeComponent();
            _editMode = !string.IsNullOrEmpty(editParticipantId);
            HeaderHost.Content = Ui.Header(_editMode ? "Edit Registration" : "Registration");

            if (_editMode) LoadForEdit(editParticipantId);
            else
            {
                try { PatientIdTxt.Text = StudyRepository.PeekNextParticipantId() + "  (assigned on save)"; }
                catch (Exception ex) { PatientIdTxt.Text = "database unavailable: " + ex.Message; }
                RegisteredAtTxt.Text = "set automatically on save";
                OperatorTxt.Text = AppSession.ActorId;
                SensitivityCombo.SelectedIndex = 0;
                DurationCombo.SelectedIndex = 0;
            }

            _loading = false;
        }

        // ------------------------------------------------------------- edit --

        private void LoadForEdit(string id)
        {
            Participant p = StudyRepository.GetParticipant(id);
            if (p == null) throw new InvalidOperationException("Participant " + id + " not found.");
            SkinProfile sp = StudyRepository.GetSkinProfile(p.ParticipantID) ?? new SkinProfile();

            _participantId = p.ParticipantID;
            PageTitle.Text = "Edit Registration — " + p.ParticipantID;
            PatientIdTxt.Text = p.ParticipantID;
            RegisteredAtTxt.Text = p.RegisteredAt;
            FirstNameTxt.Text = p.FirstName;
            MiddleNameTxt.Text = p.MiddleName;
            LastNameTxt.Text = p.LastName;
            DateTime dob;
            if (DateTime.TryParse(p.DateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)) DobPicker.SelectedDate = dob;
            AgeTxt.Text = p.Age.HasValue ? p.Age.Value.ToString(CultureInfo.InvariantCulture) : "";
            Select(SexCombo, p.Sex);
            ContactTxt.Text = p.Contact;
            OperatorTxt.Text = p.OperatorID;
            OperatorTxt.IsReadOnly = true;
            OperatorTxt.Background = Ui.Brush("#F3F4F6");
            StatusPanel.Visibility = Visibility.Visible;
            Select(StatusCombo, p.Status ?? "Active");

            Select(SkinTypeCombo, sp.GeneralSkinType);
            Select(SensitivityCombo, sp.Sensitivity);
            Select(FitzCombo, string.IsNullOrEmpty(sp.FitzpatrickManual) ? "Not collected" : sp.FitzpatrickManual);
            Select(FitzSourceCombo, sp.FitzpatrickSource ?? "Self-reported");
            SetChecks(ConcernPanel, sp.Concerns);
            ConcernOtherTxt.Text = sp.ConcernOther;
            SetChecks(RegionPanel, sp.Regions);
            RegionOtherTxt.Text = sp.RegionOther;
            Select(DurationCombo, sp.ConcernDuration ?? "Not applicable");
            CleanserTxt.Text = sp.Cleanser;
            MoisturizerTxt.Text = sp.Moisturizer;
            SunscreenTxt.Text = sp.Sunscreen;
            AcneTxTxt.Text = sp.AcneTreatment;
            EczemaTxTxt.Text = sp.EczemaTreatment;
            PigmentTxTxt.Text = sp.PigmentationTreatment;
            OtherProductsTxt.Text = sp.OtherProducts;
            ProceduresTxt.Text = sp.RecentProcedures;
            OtherResponsesTxt.Text = sp.OtherResponses;

            SaveBtn.Content = "Save Changes";
            ContinueBtn.Visibility = Visibility.Collapsed;
        }

        // ---------------------------------------------------------- helpers --

        private static string Selected(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Content as string;

        private static void Select(ComboBox c, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (ComboBoxItem i in c.Items)
                if (string.Equals(i.Content as string, value, StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = i; return; }
        }

        private static string Checked(Panel p) =>
            string.Join(", ", p.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content));

        private static void SetChecks(Panel p, string csv)
        {
            var set = new HashSet<string>((csv ?? "").Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (CheckBox c in p.Children.OfType<CheckBox>()) c.IsChecked = set.Contains((string)c.Content);
        }

        private static string T(TextBox t) => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim();

        private void AnyField_Changed(object sender, TextChangedEventArgs e) { if (!_loading) _dirty = true; }
        private void AnyCombo_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) _dirty = true; }
        private void AnyCheck_Changed(object sender, RoutedEventArgs e) { if (!_loading) _dirty = true; }

        private void DobPicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !DobPicker.SelectedDate.HasValue) return;
            _dirty = true;
            DateTime d = DobPicker.SelectedDate.Value, today = DateTime.Today;
            int age = today.Year - d.Year - (today.DayOfYear < d.DayOfYear ? 1 : 0);
            if (age >= 0 && age < 130) AgeTxt.Text = age.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------- validation --

        private bool Validate(out Participant p, out SkinProfile sp)
        {
            p = null; sp = null;
            var errors = new List<string>();

            int age = 0;
            bool hasAge = int.TryParse((AgeTxt.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out age);
            if (!hasAge) errors.Add("age (or date of birth)");
            else if (age < 0 || age > 120) errors.Add("a valid age (0-120)");
            if (DobPicker.SelectedDate.HasValue && DobPicker.SelectedDate.Value > DateTime.Today) errors.Add("a date of birth that is not in the future");
            if (Selected(SexCombo) == null) errors.Add("sex");
            if (string.IsNullOrWhiteSpace(OperatorTxt.Text)) errors.Add("researcher / operator ID");
            if (Selected(SkinTypeCombo) == null) errors.Add("general skin type");

            string concerns = Checked(ConcernPanel);
            if (concerns.Length == 0) errors.Add("at least one reported concern");
            if (concerns.Contains("Other") && string.IsNullOrWhiteSpace(ConcernOtherTxt.Text)) errors.Add("a description of the 'Other' concern");
            string regions = Checked(RegionPanel);
            if (regions.Contains("Other") && string.IsNullOrWhiteSpace(RegionOtherTxt.Text)) errors.Add("a description of the 'Other' region");
            if (_editMode && string.IsNullOrWhiteSpace(EditReasonTxt.Text)) errors.Add("a reason for this edit");

            if (errors.Count > 0)
            {
                ValidationTxt.Text = "Please provide " + string.Join(", ", errors) + ".";
                return false;
            }
            ValidationTxt.Text = "";

            p = new Participant
            {
                ParticipantID = _participantId,
                FirstName = T(FirstNameTxt),
                MiddleName = T(MiddleNameTxt),
                LastName = T(LastNameTxt),
                DateOfBirth = DobPicker.SelectedDate.HasValue ? DobPicker.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                Age = age,
                Sex = Selected(SexCombo),
                Contact = T(ContactTxt),
                OperatorID = T(OperatorTxt),
                Status = _editMode ? (Selected(StatusCombo) ?? "Active") : "Active"
            };

            string fitz = Selected(FitzCombo) ?? "Not collected";
            sp = new SkinProfile
            {
                GeneralSkinType = Selected(SkinTypeCombo),
                FitzpatrickManual = fitz,
                FitzpatrickSource = fitz == "Not collected" ? "Not collected" : Selected(FitzSourceCombo),
                Sensitivity = Selected(SensitivityCombo),
                Concerns = concerns,
                ConcernOther = T(ConcernOtherTxt),
                Regions = regions,
                RegionOther = T(RegionOtherTxt),
                ConcernDuration = Selected(DurationCombo),
                Cleanser = T(CleanserTxt),
                Moisturizer = T(MoisturizerTxt),
                Sunscreen = T(SunscreenTxt),
                AcneTreatment = T(AcneTxTxt),
                EczemaTreatment = T(EczemaTxTxt),
                PigmentationTreatment = T(PigmentTxTxt),
                OtherProducts = T(OtherProductsTxt),
                RecentProcedures = T(ProceduresTxt),
                OtherResponses = T(OtherResponsesTxt)
            };
            return true;
        }

        // ---------------------------------------------------------- actions --

        /// <summary>Returns true when the data is saved (or already saved and unchanged).</summary>
        private bool Save()
        {
            if (!_editMode && _participantId != null && !_dirty) return true;

            Participant p; SkinProfile sp;
            if (!Validate(out p, out sp)) return false;

            try
            {
                if (_editMode)
                {
                    StudyRepository.UpdateParticipant(p, sp, EditReasonTxt.Text.Trim());
                }
                else if (_participantId == null)
                {
                    _participantId = StudyRepository.RegisterParticipant(p, sp);
                    Participant saved = StudyRepository.GetParticipant(_participantId);
                    PatientIdTxt.Text = _participantId;
                    RegisteredAtTxt.Text = saved != null ? saved.RegisteredAt : "";
                    AppSession.CurrentParticipantId = _participantId;
                    PageTitle.Text = "Participant Registration — " + _participantId;
                    SaveBtn.Content = "Save Changes";
                }
                else
                {
                    StudyRepository.UpdateParticipant(p, sp, "Correction during registration");
                }

                _dirty = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("The registration could not be saved.\n\n" + ex.Message, "LUMYVUE Registration",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            bool wasNew = _participantId == null;
            if (!Save()) return;

            if (_editMode)
            {
                MessageBox.Show("Registration updated for " + _participantId + ".", "LUMYVUE Registration");
                Nav.Back();
            }
            else if (wasNew)
            {
                MessageBox.Show("Participant registered.\n\nPatientID: " + _participantId +
                                "\n\nPlease note this ID; it is used to find the participant later.",
                                "LUMYVUE Registration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ContinueBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!Save()) return;
            Nav.Go(new ConsentPage(_participantId));
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_dirty && MessageBox.Show("Discard the unsaved changes on this form?", "LUMYVUE Registration",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            Nav.Back();
        }
    }
}
