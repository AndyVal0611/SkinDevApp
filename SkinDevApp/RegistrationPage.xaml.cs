// RegistrationPage.xaml.cs - patient registration and medical-history intake with an automatic PatientID.
// The form follows the paper "Patient Registration" + "Medical History and Intake" format. Billing, insurance and
// the signature are left out: participants sign the separate paper consent form.
// New mode: Save Registration -> Continue to Consent. Edit mode (from Participant Records):
// explicit edit with a reason; scan results are never touched.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public partial class RegistrationPage : Page
    {
        private const string ReasonNeeded = "the reason for this change";

        /// <summary>One card of the form and its entry in the section list on the left.</summary>
        private sealed class Section
        {
            public int Number;
            public Border Card, Item, Badge;
            public TextBlock NumberText, TitleText;
            public UIElement Tick;
            public string BadgeBg, BadgeInk;
            public bool Attention;                // set when Save found something missing here; cleared once the section is complete
        }

        private readonly List<Section> _sections = new List<Section>();
        private Section _record, _patient, _contact, _referral, _emergency, _visit, _history, _meds, _social, _alerts, _pharmacy;
        private Section _active;
        private TextBlock _progressText;
        private ProgressBar _progressBar;
        private DispatcherTimer _scrollAnim;
        private bool _navScrolling;

        private SkinProfile _skin;                // carried through unchanged: Monk Skin Tone (rated later by a person) and the earlier form's skin answers
        private string _legacyContact;            // the earlier form's single "contact" field, kept when it is not a phone number or e-mail
        private bool _strict = true;              // false only while editing a patient registered with the earlier form
        private readonly bool _editMode;
        private string _participantId;            // set once saved (new) or from the record (edit)
        private bool _loading = true;
        private bool _syncing;
        private bool _dirty;
        private bool _showingProblems;            // the "Please complete" line is on screen and is kept up to date

        public RegistrationPage() : this(null) { }

        public RegistrationPage(string editParticipantId)
        {
            InitializeComponent();
            _editMode = !string.IsNullOrEmpty(editParticipantId);
            HeaderHost.Content = Ui.Header(_editMode ? "Edit registration" : "Registration");
            BuildSections();

            // one handler for every field: marks the form as changed and refreshes the section list
            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(AnyField_Changed));
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(AnyCombo_Changed));
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(AnyCheck_Changed));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(AnyCheck_Changed));
            SizeChanged += (s, e) => ApplyWidth();

            if (_editMode) LoadForEdit(editParticipantId);
            else
            {
                try { PatientIdTxt.Text = StudyRepository.PeekNextParticipantId(); }
                catch (Exception ex) { PatientIdTxt.Text = "unavailable"; ValidationTxt.Text = "The database could not be read: " + ex.Message; }
                RegDateTxt.Text = DateTime.Now.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                OperatorTxt.Text = AppSession.ActorId;
                _skin = new SkinProfile { MonkSkinTone = "Not collected", MonkSource = "Not collected" };
            }

            _loading = false;
            Refresh();
            SetActive(_sections[0]);
        }

        // ------------------------------------------------------------- edit --

        private void LoadForEdit(string id)
        {
            Participant p = StudyRepository.GetParticipant(id);
            if (p == null) throw new InvalidOperationException("Patient " + id + " was not found.");
            _skin = StudyRepository.GetSkinProfile(p.ParticipantID) ?? new SkinProfile();
            if (string.IsNullOrEmpty(_skin.MonkSkinTone)) _skin.MonkSkinTone = "Not collected";      // otherwise kept exactly as stored
            if (string.IsNullOrEmpty(_skin.MonkSource)) _skin.MonkSource = "Not collected";
            MedicalIntake m = StudyRepository.GetMedicalIntake(p.ParticipantID);

            _participantId = p.ParticipantID;
            PageTitle.Text = "Edit registration · " + p.ParticipantID;
            PatientIdTxt.Text = p.ParticipantID;
            RegDateTxt.Text = Fmt.When(p.RegisteredAt);
            FirstNameTxt.Text = p.FirstName;
            MiddleNameTxt.Text = p.MiddleName;
            LastNameTxt.Text = p.LastName;
            DateTime dob;
            if (DateTime.TryParse(p.DateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)) DobPicker.SelectedDate = dob;
            AgeTxt.Text = p.Age.HasValue ? p.Age.Value.ToString(CultureInfo.InvariantCulture) : "";
            Select(SexCombo, p.Sex);
            OperatorTxt.Text = p.OperatorID;
            OperatorTxt.IsReadOnly = true;
            OperatorTxt.Background = Ui.Brush("#F3EFE6");
            OperatorReq.Text = "";
            StatusPanel.Visibility = Visibility.Visible;
            Select(StatusCombo, p.Status ?? "Active");

            if (m != null) FillIntake(m);
            else if (!string.IsNullOrWhiteSpace(p.Contact))
            {
                // the earlier form had one free "contact" field: place it where it belongs, or keep it as it was
                string c = p.Contact.Trim();
                if (IsEmail(c)) EmailTxt.Text = c;
                else if (IsPhone(c)) CellPhoneTxt.Text = c;
                else _legacyContact = c;
            }

            // A patient registered with the earlier form is not forced to complete the whole medical history on every edit.
            // Once the form is complete it is checked like any other record.
            _strict = !Problems().Any(x => x.Key != _record);
            ApplyStrictness();

            SaveBtn.Content = "Save changes";
            ContinueBtn.Visibility = Visibility.Collapsed;
        }

        private void FillIntake(MedicalIntake m)
        {
            AddressTxt.Text = m.MailingAddress;
            EmailTxt.Text = m.Email;
            HomePhoneTxt.Text = m.HomePhone;
            CellPhoneTxt.Text = m.CellPhone;
            Select(MaritalCombo, m.MaritalStatus);
            OccupationTxt.Text = m.Occupation;
            SetChoice(ReferralPanel, m.ReferredBy);
            ReferralDetailTxt.Text = m.ReferralDetail;
            PcpNameTxt.Text = m.PrimaryCarePhysician;
            PcpPhoneTxt.Text = m.PrimaryCarePhone;
            EmergencyNameTxt.Text = m.EmergencyName;
            EmergencyRelationTxt.Text = m.EmergencyRelationship;
            EmergencyPhoneTxt.Text = m.EmergencyPhone;
            VisitReasonTxt.Text = m.VisitReason;
            LocationTxt.Text = m.ProblemLocation;
            DurationTxt.Text = m.ProblemDuration;
            SetChecks(PmhPanel, m.PastMedicalHistory);
            CancerTxt.Text = m.CancerDetail;
            SetYesNo(SkinHxYes, SkinHxNo, m.SkinHistory);
            SkinHxTxt.Text = m.SkinHistoryDetail;
            MedsNaChk.IsChecked = m.Medications == "N/A";
            MedsTxt.Text = m.Medications == "N/A" ? "" : m.Medications;
            AllergiesNaChk.IsChecked = m.Allergies == "N/A";
            AllergiesTxt.Text = m.Allergies == "N/A" ? "" : m.Allergies;
            SetYesNo(SmokeYes, SmokeNo, m.Smokes);
            SmokeAmountTxt.Text = m.SmokingAmount;
            SetYesNo(AlcoholYes, AlcoholNo, m.DrinksAlcohol);
            AlcoholAmountTxt.Text = m.AlcoholAmount;
            SetChecks(RosPanel, m.ReviewOfSystems);
            SetChecks(AlertsPanel, m.Alerts);
            PharmacyNameTxt.Text = m.PharmacyName;
            PharmacyPhoneTxt.Text = m.PharmacyPhone;
            PharmacyAddressTxt.Text = m.PharmacyAddress;
            PharmacyCityTxt.Text = m.PharmacyCity;
        }

        /// <summary>Earlier-form patients: only the record details, age and gender stay required, and the age can be typed.</summary>
        private void ApplyStrictness()
        {
            if (_strict) return;
            LegacyNote.Visibility = Visibility.Visible;
            foreach (Run req in new[] { FirstNameReq, LastNameReq, DobReq, HomePhoneReq, CellPhoneReq, VisitReq, PmhReq, SkinHxReq, MedsReq, AllergiesReq, SmokeReq, AlcoholReq, AlertsReq })
                req.Text = "";
            PhoneHint.Visibility = Visibility.Collapsed;
            AgeLabel.Inlines.Clear();
            AgeLabel.Inlines.Add(new Run("Age "));
            AgeLabel.Inlines.Add(new Run("*") { Foreground = Ui.Brush("#B4483F") });
            AgeTxt.IsReadOnly = false;
            AgeTxt.Focusable = true;
            AgeTxt.Background = Ui.Brush("#FFFDF9");
        }

        // ------------------------------------------------------- the section list --

        private void BuildSections()
        {
            _record = AddSection(SecRecord, "Registration record", "Kept by LUMYVUE for this patient.");
            _patient = AddSection(SecPatient, "Patient information", "Legal name as it appears on a driver's license or photo ID.");
            _contact = AddSection(SecContact, "Contact details", "How the clinic can reach the patient.");
            _referral = AddSection(SecReferral, "Referral and primary care", "Who referred the patient, and their regular doctor.");
            _emergency = AddSection(SecEmergency, "Emergency contact", "Someone the patient authorizes to receive their health information.");
            _visit = AddSection(SecVisit, "Reason for visit", "The problem, where it is and how long it has been there.");
            _history = AddSection(SecHistory, "Medical history", "Past conditions, and any history of skin cancer or skin disorders.");
            _meds = AddSection(SecMeds, "Medications and allergies", "If there are none, tick None (N/A).");
            _social = AddSection(SecSocial, "Social history and review of systems", "Smoking, alcohol and recent symptoms.");
            _alerts = AddSection(SecAlerts, "Alerts", "Conditions the care team must know before any procedure.");
            _pharmacy = AddSection(SecPharmacy, "Preferred pharmacy", "Optional.");

            NavPanel.Children.Add(Eyebrow("PATIENT REGISTRATION", 0));
            for (int i = 0; i < 5; i++) NavPanel.Children.Add(_sections[i].Item);
            NavPanel.Children.Add(Eyebrow("MEDICAL HISTORY AND INTAKE", 16));
            for (int i = 5; i < _sections.Count; i++) NavPanel.Children.Add(_sections[i].Item);

            _progressText = Ui.Text("", 11.5, false, Ui.Muted, new Thickness(8, 18, 8, 6));
            _progressBar = new ProgressBar
            {
                Height = 6, Maximum = _sections.Count, Margin = new Thickness(8, 0, 8, 2),
                Background = Ui.Brush("#EEE8DA"), Foreground = Ui.Brush("#818263")
            };
            NavPanel.Children.Add(_progressText);
            NavPanel.Children.Add(_progressBar);
        }

        private TextBlock Eyebrow(string text, double top) =>
            new TextBlock { Text = text, Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(8, top, 0, 6) };

        /// <summary>Puts a numbered heading on a form card and creates its entry in the section list.</summary>
        private Section AddSection(Border card, string title, string hint)
        {
            var s = new Section { Number = _sections.Count + 1, Card = card };
            bool intake = s.Number > 5;                                   // the second part of the paper form
            s.BadgeBg = intake ? "#E7E8CF" : "#F6E6DF";
            s.BadgeInk = intake ? "#5C5D45" : "#8A5A4C";
            string number = s.Number.ToString(CultureInfo.InvariantCulture);

            // card heading: number, title, one line of help, then a hairline
            var head = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            head.Children.Add(new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Ui.Brush(s.BadgeBg),
                Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = number, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(s.BadgeInk),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0) });
            words.Children.Add(new TextBlock { Text = hint, Style = (Style)FindResource("Muted"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(words, 1);
            head.Children.Add(words);
            var line = new Border { Height = 1, Background = Ui.Brush("#EEE8DA"), Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(line, 1);
            Grid.SetColumnSpan(line, 2);
            head.Children.Add(line);
            ((StackPanel)card.Child).Children.Insert(0, head);

            // entry in the section list: number (a tick once complete, "!" when Save found something missing) and title
            s.NumberText = new TextBlock
            {
                Text = number, FontSize = 12, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            s.Tick = Ui.Icon("Icon.Check", 14, Brushes.White);
            s.Badge = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center, Child = new Grid { Children = { s.NumberText, s.Tick } }
            };
            s.TitleText = new TextBlock { Text = title, FontSize = 13, Foreground = Ui.Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var row = new DockPanel();
            DockPanel.SetDock(s.Badge, Dock.Left);
            row.Children.Add(s.Badge);
            row.Children.Add(s.TitleText);
            s.Item = new Border
            {
                Child = row, Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 1, 0, 1),
                CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand
            };
            s.Item.MouseEnter += (o, e) => { if (s != _active) s.Item.Background = Ui.Brush("#F6F2E8"); };
            s.Item.MouseLeave += (o, e) => { if (s != _active) s.Item.Background = Brushes.Transparent; };
            s.Item.MouseLeftButtonUp += (o, e) => GoTo(s);

            _sections.Add(s);
            return s;
        }

        /// <summary>Updates the "if yes" fields, the section ticks and the progress line.</summary>
        private void Refresh()
        {
            UpdateDependentFields();
            List<KeyValuePair<Section, string>> problems = Problems();
            int done = 0;
            foreach (Section s in _sections)
            {
                bool complete = !problems.Any(x => x.Key == s) && HasAnswer(s.Card);
                if (complete) { s.Attention = false; done++; }
                bool alert = !complete && s.Attention;
                s.Badge.Background = Ui.Brush(complete ? "#818263" : alert ? "#FBEDEA" : s.BadgeBg);
                s.Badge.BorderBrush = Ui.Brush(complete ? "#818263" : alert ? "#E3A69C" : s.BadgeBg);
                s.NumberText.Text = alert ? "!" : s.Number.ToString(CultureInfo.InvariantCulture);
                s.NumberText.Foreground = Ui.Brush(alert ? "#9B2F25" : s.BadgeInk);
                s.NumberText.Visibility = complete ? Visibility.Collapsed : Visibility.Visible;
                s.Tick.Visibility = complete ? Visibility.Visible : Visibility.Collapsed;
                s.TitleText.Foreground = alert ? Ui.Brush("#9B2F25") : Ui.Ink;
            }
            _progressText.Text = done + " of " + _sections.Count + " sections complete";
            _progressBar.Value = done;
            if (_showingProblems) ShowProblems(problems);
        }

        private void ShowProblems(List<KeyValuePair<Section, string>> problems)
        {
            _showingProblems = problems.Count > 0;
            List<string> what = problems.Select(x => x.Value).ToList();
            ValidationTxt.Text = what.Count == 0 ? "" : "Please complete: " + (what.Count <= 4
                ? string.Join(", ", what)
                : string.Join(", ", what.Take(3)) + " and " + (what.Count - 3) + " more (marked in the section list)") + ".";
        }

        private void UpdateDependentFields()
        {
            string referral = SelectedChoice(ReferralPanel);
            bool doctor = referral == "Doctor", other = referral == "Other";
            ReferralDetailTxt.IsEnabled = doctor || other;
            ReferralDetailLabel.Inlines.Clear();
            ReferralDetailLabel.Inlines.Add(new Run(doctor ? "Doctor's name " : other ? "Please specify " : "Referral details"));
            if (doctor || other) ReferralDetailLabel.Inlines.Add(new Run("*") { Foreground = Ui.Brush("#B4483F") });

            CancerTxt.IsEnabled = CancerChk.IsChecked == true;
            SkinHxTxt.IsEnabled = SkinHxYes.IsChecked == true;
            SmokeAmountTxt.IsEnabled = SmokeYes.IsChecked == true;
            AlcoholAmountTxt.IsEnabled = AlcoholYes.IsChecked == true;
            MedsTxt.IsEnabled = MedsNaChk.IsChecked != true;
            AllergiesTxt.IsEnabled = AllergiesNaChk.IsChecked != true;
            PregnancyNote.Visibility = PregnantChk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetActive(Section s)
        {
            if (s == _active) return;
            if (_active != null)
            {
                _active.Item.Background = Brushes.Transparent;
                _active.TitleText.FontWeight = FontWeights.Normal;
            }
            _active = s;
            s.Item.Background = Ui.Brush("#F1EDE0");
            s.TitleText.FontWeight = FontWeights.SemiBold;
        }

        private double TopOf(Section s) => s.Card.TransformToAncestor(FormStack).Transform(new Point(0, 0)).Y;

        private void GoTo(Section s)
        {
            SetActive(s);
            ScrollSmoothly(Math.Max(0, Math.Min(TopOf(s) - 8, FormScroll.ScrollableHeight)));
        }

        private void ScrollSmoothly(double to)
        {
            _scrollAnim?.Stop();
            double from = FormScroll.VerticalOffset;
            if (Math.Abs(to - from) < 1) return;
            DateTime start = DateTime.Now;
            _navScrolling = true;
            _scrollAnim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            _scrollAnim.Tick += (o, e) =>
            {
                double t = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / 320);
                FormScroll.ScrollToVerticalOffset(from + (to - from) * (1 - Math.Pow(1 - t, 3)));
                if (t < 1) return;
                _scrollAnim.Stop();
                // the last scroll is laid out first, so the section that was picked stays highlighted
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _navScrolling = false));
            };
            _scrollAnim.Start();
        }

        /// <summary>Highlights the section at the top of the form while it is scrolled.</summary>
        private void FormScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_navScrolling || _sections.Count == 0 || !IsLoaded) return;
            double line = FormScroll.VerticalOffset + 48;
            Section current = _sections[0];
            foreach (Section s in _sections)
                if (TopOf(s) <= line) current = s;
            if (FormScroll.ScrollableHeight > 0 && FormScroll.VerticalOffset >= FormScroll.ScrollableHeight - 1)
                current = _sections[_sections.Count - 1];
            SetActive(current);
        }

        /// <summary>The section list needs room; on a narrow (portrait) screen the form uses the whole width.</summary>
        private void ApplyWidth()
        {
            bool roomy = ActualWidth >= 1150;
            NavCard.Visibility = roomy ? Visibility.Visible : Visibility.Collapsed;
            NavCol.Width = new GridLength(roomy ? 262 : 0);
        }

        // ---------------------------------------------------------- helpers --

        private static string Selected(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Content as string;

        private static void Select(ComboBox c, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (ComboBoxItem i in c.Items)
                if (string.Equals(i.Content as string, value, StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = i; return; }
        }

        /// <summary>Every tick box inside a check list (the lists are laid out in columns).</summary>
        private static IEnumerable<CheckBox> Boxes(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is CheckBox c) yield return c;
                else if (child is DependencyObject d) foreach (CheckBox x in Boxes(d)) yield return x;
            }
        }

        private static string Checked(DependencyObject list) =>
            string.Join(", ", Boxes(list).Where(c => c.IsChecked == true).Select(c => (string)c.Content));

        private static void SetChecks(DependencyObject list, string csv)
        {
            var set = new HashSet<string>((csv ?? "").Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (CheckBox c in Boxes(list)) c.IsChecked = set.Contains((string)c.Content);
        }

        private static bool IsInside(DependencyObject element, DependencyObject container)
        {
            for (DependencyObject d = element; d != null; d = LogicalTreeHelper.GetParent(d))
                if (d == container) return true;
            return false;
        }

        private static string SelectedChoice(Panel p) => p.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string;

        private static void SetChoice(Panel p, string value)
        {
            foreach (RadioButton r in p.Children.OfType<RadioButton>())
                r.IsChecked = string.Equals(r.Content as string, value, StringComparison.OrdinalIgnoreCase);
        }

        private static string YesNo(RadioButton yes, RadioButton no) => yes.IsChecked == true ? "Yes" : no.IsChecked == true ? "No" : null;

        private static void SetYesNo(RadioButton yes, RadioButton no, string value)
        {
            yes.IsChecked = value == "Yes";
            no.IsChecked = value == "No";
        }

        /// <summary>True when anything in the card has been answered (read-only boxes such as the Patient ID do not count).</summary>
        private static bool HasAnswer(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TextBox t) { if (!t.IsReadOnly && !string.IsNullOrWhiteSpace(t.Text)) return true; }
                else if (child is ToggleButton b) { if (b.IsChecked == true) return true; }
                else if (child is ComboBox c) { if (c.SelectedItem != null) return true; }
                else if (child is DatePicker p) { if (p.SelectedDate.HasValue) return true; }
                else if (child is DependencyObject d && HasAnswer(d)) return true;
            }
            return false;
        }

        private static string T(TextBox t) => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim();

        private static string Null(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static bool IsPhone(string s) => Regex.IsMatch(s, @"^[0-9+()\-.\s/]+$") && s.Count(char.IsDigit) >= 7;

        private static bool IsEmail(string s) => Regex.IsMatch(s, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        private void Changed()
        {
            if (_loading) return;
            _dirty = true;
            Refresh();
        }

        private void AnyField_Changed(object sender, TextChangedEventArgs e) => Changed();
        private void AnyCombo_Changed(object sender, SelectionChangedEventArgs e) => Changed();

        private void AnyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;

            // "None" and the other answers of the same list exclude each other
            var box = e.OriginalSource as CheckBox;
            if (box != null && box.IsChecked == true)
            {
                DependencyObject list = IsInside(box, PmhPanel) ? PmhPanel : IsInside(box, AlertsPanel) ? (DependencyObject)AlertsPanel : null;
                if (list != null)
                {
                    bool none = (box.Tag as string) == "None";
                    _syncing = true;
                    foreach (CheckBox other in Boxes(list))
                        if (other != box && (none || (other.Tag as string) == "None")) other.IsChecked = false;
                    _syncing = false;
                }
            }
            Changed();
        }

        private void DobPicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _dirty = true;
            if (!DobPicker.SelectedDate.HasValue)
            {
                if (_strict) AgeTxt.Text = "";
                return;
            }
            DateTime d = DobPicker.SelectedDate.Value.Date, today = DateTime.Today;
            int age = today.Year - d.Year;
            if (d > today.AddYears(-age)) age--;
            AgeTxt.Text = age >= 0 && age < 130 ? age.ToString(CultureInfo.InvariantCulture) : "";
        }

        // ------------------------------------------------------- validation --

        /// <summary>Everything still missing or wrong, with the section it belongs to (in the order of the form).</summary>
        private List<KeyValuePair<Section, string>> Problems()
        {
            var list = new List<KeyValuePair<Section, string>>();
            void Need(Section s, string what) => list.Add(new KeyValuePair<Section, string>(s, what));
            void Phone(Section s, TextBox box, string what) { if (T(box) != null && !IsPhone(T(box))) Need(s, "a valid " + what); }

            // 1. registration record
            if (T(OperatorTxt) == null) Need(_record, "the staff ID");
            if (_editMode && T(EditReasonTxt) == null) Need(_record, ReasonNeeded);

            // 2. patient information
            if (_strict && T(FirstNameTxt) == null) Need(_patient, "the legal first name");
            if (_strict && T(LastNameTxt) == null) Need(_patient, "the legal last name");
            int age;
            bool hasAge = int.TryParse((AgeTxt.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out age);
            if (DobPicker.SelectedDate.HasValue && DobPicker.SelectedDate.Value.Date > DateTime.Today) Need(_patient, "a date of birth that is not in the future");
            else if (_strict && !DobPicker.SelectedDate.HasValue) Need(_patient, "the date of birth");
            else if (!hasAge) Need(_patient, "the age (or date of birth)");
            else if (age < 0 || age > 120) Need(_patient, "a valid age (0 to 120)");
            if (Selected(SexCombo) == null) Need(_patient, "the gender");

            // 3. contact details
            if (_strict && T(HomePhoneTxt) == null && T(CellPhoneTxt) == null) Need(_contact, "a home or cell phone number");
            Phone(_contact, HomePhoneTxt, "home phone number");
            Phone(_contact, CellPhoneTxt, "cell phone number");
            if (T(EmailTxt) != null && !IsEmail(T(EmailTxt))) Need(_contact, "a valid e-mail address");

            // 4. referral and primary care
            string referral = SelectedChoice(ReferralPanel);
            if (referral == "Doctor" && T(ReferralDetailTxt) == null) Need(_referral, "the referring doctor's name");
            if (referral == "Other" && T(ReferralDetailTxt) == null) Need(_referral, "who referred the patient");
            Phone(_referral, PcpPhoneTxt, "PCP phone number");

            // 5. emergency contact
            Phone(_emergency, EmergencyPhoneTxt, "emergency contact phone number");

            // 6. reason for visit
            if (_strict && T(VisitReasonTxt) == null) Need(_visit, "the reason for the visit");

            // 7. medical history
            if (_strict && Checked(PmhPanel).Length == 0) Need(_history, "the past medical history (or None)");
            if (CancerChk.IsChecked == true && T(CancerTxt) == null) Need(_history, "the type of cancer");
            string skinHx = YesNo(SkinHxYes, SkinHxNo);
            if (_strict && skinHx == null) Need(_history, "the history of skin cancer or skin disorders (yes or no)");
            if (skinHx == "Yes" && T(SkinHxTxt) == null) Need(_history, "the skin condition or disorder");

            // 8. medications and allergies
            if (_strict && MedsNaChk.IsChecked != true && T(MedsTxt) == null) Need(_meds, "the current medications (or None)");
            if (_strict && AllergiesNaChk.IsChecked != true && T(AllergiesTxt) == null) Need(_meds, "the allergies (or None)");

            // 9. social history
            string smokes = YesNo(SmokeYes, SmokeNo), alcohol = YesNo(AlcoholYes, AlcoholNo);
            if (_strict && smokes == null) Need(_social, "whether the patient smokes");
            if (smokes == "Yes" && T(SmokeAmountTxt) == null) Need(_social, "how much the patient smokes");
            if (_strict && alcohol == null) Need(_social, "whether the patient drinks alcohol");
            if (alcohol == "Yes" && T(AlcoholAmountTxt) == null) Need(_social, "how much alcohol the patient drinks");

            // 10. alerts
            if (_strict && Checked(AlertsPanel).Length == 0) Need(_alerts, "the alerts (or None)");

            // 11. preferred pharmacy
            Phone(_pharmacy, PharmacyPhoneTxt, "pharmacy telephone number");
            return list;
        }

        private bool Validate(out Participant p, out MedicalIntake m)
        {
            p = null; m = null;

            List<KeyValuePair<Section, string>> problems = Problems();
            if (problems.Count > 0)
            {
                foreach (Section s in _sections) s.Attention = problems.Any(x => x.Key == s);
                _showingProblems = true;
                Refresh();
                GoTo(problems[0].Key);
                return false;
            }
            ShowProblems(problems);

            p = new Participant
            {
                ParticipantID = _participantId,
                FirstName = T(FirstNameTxt),
                MiddleName = T(MiddleNameTxt),
                LastName = T(LastNameTxt),
                DateOfBirth = DobPicker.SelectedDate.HasValue ? DobPicker.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                Age = int.Parse(AgeTxt.Text.Trim(), CultureInfo.InvariantCulture),
                Sex = Selected(SexCombo),
                Contact = T(CellPhoneTxt) ?? T(HomePhoneTxt) ?? T(EmailTxt) ?? _legacyContact,      // the short contact shown in lists
                OperatorID = T(OperatorTxt),
                Status = _editMode ? (Selected(StatusCombo) ?? "Active") : "Active"
            };

            string referral = SelectedChoice(ReferralPanel), skinHx = YesNo(SkinHxYes, SkinHxNo);
            string smokes = YesNo(SmokeYes, SmokeNo), alcohol = YesNo(AlcoholYes, AlcoholNo);
            m = new MedicalIntake
            {
                MailingAddress = T(AddressTxt),
                Email = T(EmailTxt),
                HomePhone = T(HomePhoneTxt),
                CellPhone = T(CellPhoneTxt),
                MaritalStatus = Selected(MaritalCombo),
                Occupation = T(OccupationTxt),
                ReferredBy = referral,
                ReferralDetail = referral == "Doctor" || referral == "Other" ? T(ReferralDetailTxt) : null,
                PrimaryCarePhysician = T(PcpNameTxt),
                PrimaryCarePhone = T(PcpPhoneTxt),
                EmergencyName = T(EmergencyNameTxt),
                EmergencyRelationship = T(EmergencyRelationTxt),
                EmergencyPhone = T(EmergencyPhoneTxt),
                VisitReason = T(VisitReasonTxt),
                ProblemLocation = T(LocationTxt),
                ProblemDuration = T(DurationTxt),
                PastMedicalHistory = Null(Checked(PmhPanel)),
                CancerDetail = CancerChk.IsChecked == true ? T(CancerTxt) : null,
                SkinHistory = skinHx,
                SkinHistoryDetail = skinHx == "Yes" ? T(SkinHxTxt) : null,
                Medications = MedsNaChk.IsChecked == true ? "N/A" : T(MedsTxt),
                Allergies = AllergiesNaChk.IsChecked == true ? "N/A" : T(AllergiesTxt),
                Smokes = smokes,
                SmokingAmount = smokes == "Yes" ? T(SmokeAmountTxt) : null,
                DrinksAlcohol = alcohol,
                AlcoholAmount = alcohol == "Yes" ? T(AlcoholAmountTxt) : null,
                ReviewOfSystems = Null(Checked(RosPanel)),
                Alerts = Null(Checked(AlertsPanel)),
                PharmacyName = T(PharmacyNameTxt),
                PharmacyPhone = T(PharmacyPhoneTxt),
                PharmacyAddress = T(PharmacyAddressTxt),
                PharmacyCity = T(PharmacyCityTxt)
            };

            // an earlier-form patient whose new sections were left empty gets no empty intake row
            MedicalIntake intake = m;
            if (!_strict && typeof(MedicalIntake).GetProperties().All(x => x.Name == "ParticipantID" || x.Name == "UpdatedAt" || x.GetValue(intake) == null))
                m = null;
            return true;
        }

        // ---------------------------------------------------------- actions --

        /// <summary>Returns true when the data is saved (or already saved and unchanged).</summary>
        private bool Save()
        {
            if (!_editMode && _participantId != null && !_dirty) return true;

            Participant p; MedicalIntake intake;
            if (!Validate(out p, out intake)) return false;

            try
            {
                if (_editMode)
                {
                    StudyRepository.UpdateParticipant(p, _skin, EditReasonTxt.Text.Trim(), intake);
                }
                else if (_participantId == null)
                {
                    _participantId = StudyRepository.RegisterParticipant(p, _skin, intake);
                    PatientIdTxt.Text = _participantId;
                    AppSession.CurrentParticipantId = _participantId;
                    PageTitle.Text = "Patient registration · " + _participantId;
                    SaveBtn.Content = "Save changes";
                }
                else
                {
                    StudyRepository.UpdateParticipant(p, _skin, "Correction during registration", intake);
                }

                _dirty = false;
                return true;
            }
            catch (Exception ex)
            {
                LumyDialog.Show("The registration could not be saved.\n\n" + ex.Message, "LUMYVUE registration",
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
                LumyDialog.Show("The registration of " + _participantId + " was updated.", "LUMYVUE registration");
                Nav.Back();
            }
            else if (wasNew)
            {
                LumyDialog.Show("The patient is registered.\n\nPatient ID: " + _participantId +
                                "\n\nPlease note this ID. It is used to find the patient later.",
                                "LUMYVUE registration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ContinueBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!Save()) return;
            Nav.Go(new ConsentPage(_participantId));
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_dirty && LumyDialog.Show("Discard the unsaved changes on this form?", "LUMYVUE Registration",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            Nav.Back();
        }
    }
}
