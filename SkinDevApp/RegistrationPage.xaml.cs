// RegistrationPage.xaml.cs - patient registration following the clinic's "Patient's Chart" (personal information, present
// illness, personal / past and family medical history, and an optional physical examination and special tests),
// with an automatic PatientID. The signature is left out: participants sign the separate paper consent form.
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
        private Section _record, _patient, _contact, _work, _parents, _illness, _history, _famHistory, _exam, _tests;
        private Section _active;
        private TextBlock _progressText;
        private ProgressBar _progressBar;
        private DispatcherTimer _scrollAnim;
        private bool _navScrolling;

        // family medical history: one Father and one Mother tick per condition
        private static readonly string[] FamilyLeft = { "Asthma", "Hypertension", "Diabetes mellitus", "Heart diseases" };
        private static readonly string[] FamilyRight = { "Acne", "Skin cancer", "Vitiligo", "Baldness" };
        private readonly Dictionary<string, CheckBox[]> _family = new Dictionary<string, CheckBox[]>();

        private SkinProfile _skin;                // carried through unchanged: Monk Skin Tone (rated later by a person) and any skin answers from an earlier form
        private string _legacyContact;            // an earlier form's single "contact" field, kept when it is not a phone number or e-mail
        private bool _strict = true;              // false only while editing a patient registered with an earlier form
        private readonly bool _editMode;
        private string _participantId;            // set once saved (new) or from the record (edit)
        private bool _loading = true;
        private bool _dirty;
        private bool _showingProblems;            // the "Please complete" line is on screen and is kept up to date

        public RegistrationPage() : this(null) { }

        public RegistrationPage(string editParticipantId)
        {
            InitializeComponent();
            _editMode = !string.IsNullOrEmpty(editParticipantId);
            HeaderHost.Content = Ui.Header(_editMode ? "Edit registration" : "Registration");
            FamilyHost.Content = BuildFamily();
            BuildSections();

            // one handler for every field: marks the form as changed and refreshes the section list
            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(AnyField_Changed));
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(AnyCombo_Changed));
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(AnyCheck_Changed));
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(AnyCheck_Changed));
            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ClearOptionalChoice));
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
            PatientChart chart = StudyRepository.GetPatientChart(p.ParticipantID);

            _participantId = p.ParticipantID;
            PageTitle.Text = "Edit registration · " + p.ParticipantID;
            PatientIdTxt.Text = p.ParticipantID;
            RegDateTxt.Text = Fmt.When(p.RegisteredAt);
            LastNameTxt.Text = p.LastName;
            FirstNameTxt.Text = p.FirstName;
            MiddleNameTxt.Text = p.MiddleName;
            DateTime dob;
            if (DateTime.TryParse(p.DateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)) DobPicker.SelectedDate = dob;
            AgeTxt.Text = p.Age.HasValue ? p.Age.Value.ToString(CultureInfo.InvariantCulture) : "";
            SetSex(p.Sex);
            OperatorTxt.Text = p.OperatorID;
            OperatorTxt.IsReadOnly = true;
            OperatorTxt.Background = Ui.Brush("#F3EFE6");
            OperatorReq.Text = "";
            StatusPanel.Visibility = Visibility.Visible;
            Select(StatusCombo, p.Status ?? "Active");

            if (chart != null) FillChart(chart);
            else PrefillFromEarlierForm(p);

            // the earlier form had one free "contact" field: keep it when nothing on this form can hold it
            if (!string.IsNullOrWhiteSpace(p.Contact) && T(HomeCellTxt) == null && T(HomeTelTxt) == null && T(EmailTxt) == null)
                _legacyContact = p.Contact.Trim();

            // A patient registered with an earlier form is not forced to complete the whole chart on every edit.
            // Once the chart is complete it is checked like any other record.
            _strict = !Problems().Any(x => x.Key != _record);
            ApplyStrictness();

            SaveBtn.Content = "Save changes";
            ContinueBtn.Visibility = Visibility.Collapsed;
        }

        /// <summary>Carries over what the earlier forms already collected, where it clearly belongs on this form. Nothing is deleted from the earlier record.</summary>
        private void PrefillFromEarlierForm(Participant p)
        {
            MedicalIntake m = StudyRepository.GetMedicalIntake(p.ParticipantID);
            if (m != null)
            {
                HomeAddressTxt.Text = m.MailingAddress;
                EmailTxt.Text = m.Email;
                HomeTelTxt.Text = m.HomePhone;
                HomeCellTxt.Text = m.CellPhone;
                OccupationTxt.Text = m.Occupation;
                Select(CivilCombo, m.MaritalStatus);
                if (!string.IsNullOrWhiteSpace(m.Medications) && m.Medications != "N/A") SupplementOtherTxt.Text = m.Medications;
            }
            else if (!string.IsNullOrWhiteSpace(p.Contact))
            {
                string c = p.Contact.Trim();
                if (IsEmail(c)) EmailTxt.Text = c;
                else if (IsPhone(c)) HomeCellTxt.Text = c;
            }
        }

        private void FillChart(PatientChart c)
        {
            BirthplaceTxt.Text = c.Birthplace;
            Select(CivilCombo, c.CivilStatus);
            CitizenshipTxt.Text = c.Citizenship;
            HomeAddressTxt.Text = c.HomeAddress;
            HomeTelTxt.Text = c.HomeTel;
            HomeCellTxt.Text = c.HomeCell;
            ProvAddressTxt.Text = c.ProvincialAddress;
            ProvTelTxt.Text = c.ProvincialTel;
            ProvCellTxt.Text = c.ProvincialCell;
            OccupationTxt.Text = c.Occupation;
            EmailTxt.Text = c.Email;
            BizAddressTxt.Text = c.BusinessAddress;
            BizTelTxt.Text = c.BusinessTel;
            BizCellTxt.Text = c.BusinessCell;
            SetYesNo(StudentYes, StudentNo, c.IsStudent);
            GradeTxt.Text = c.GradeYear;
            SchoolTxt.Text = c.School;
            FatherTxt.Text = c.FatherName;
            MotherTxt.Text = c.MotherName;

            OnsetTxt.Text = c.OnsetDate;
            AreaTxt.Text = c.AffectedArea;
            DescribedTxt.Text = c.DescribedLesions;
            ConsultTxt.Text = c.PreviousConsultation;
            LabTxt.Text = c.LabExams;
            TreatmentTxt.Text = c.TreatmentNotes;
            OralsTxt.Text = c.Orals;
            TopicalsTxt.Text = c.Topicals;

            AllergyFoodTxt.Text = c.AllergyFood;
            AllergyDrugsTxt.Text = c.AllergyDrugs;
            LmpTxt.Text = c.LastMenstrualPeriod;
            PregnantChk.IsChecked = c.Pregnant == "Yes";
            BreastfeedingChk.IsChecked = c.Breastfeeding == "Yes";
            SetChecks(SupplementPanel, c.Supplements);
            SupplementOtherTxt.Text = c.SupplementsOther;
            SetChecks(ConditionPanel, c.Conditions);
            ConditionOtherTxt.Text = c.ConditionsOther;
            SurgeryTxt.Text = c.Surgery;
            SurgeryYearTxt.Text = c.SurgeryYear;

            SetFamily(c.FamilyHistory);
            FamilyOtherTxt.Text = c.FamilyHistoryOther;

            SetChoice(CountPanel, c.LesionCount);
            LesionNumberTxt.Text = c.LesionNumber;
            SetChoice(ItchPanel, c.Pruritus);
            SetChoice(RednessPanel, c.Erythema);
            SetChoice(ColorPanel, c.LesionColor);
            SetChecks(LesionTypePanel, c.LesionTypes);
            DistributionTxt.Text = c.Distribution;

            WoodLampTxt.Text = c.WoodLamp;
            SetChoice(KohPanel, c.Koh);
            BiopsyTxt.Text = c.Biopsy;
        }

        /// <summary>Earlier-form patients: only the record details, age and sex stay required, and the age can be typed.</summary>
        private void ApplyStrictness()
        {
            if (_strict) return;
            LegacyNote.Visibility = Visibility.Visible;
            foreach (Run req in new[] { FirstNameReq, LastNameReq, DobReq, BirthplaceReq, CivilReq, CitizenshipReq, HomeAddressReq, HomeCellReq, OccupationReq, StudentReq })
                req.Text = "";
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
            _patient = AddSection(SecPatient, "Name and personal details", "Name, birth details, sex, civil status and citizenship.");
            _contact = AddSection(SecContact, "Addresses and contact numbers", "Home and provincial address, phone numbers and e-mail.");
            _work = AddSection(SecWork, "Occupation and school", "Work, and for students the grade or year and the school.");
            _parents = AddSection(SecParents, "Parents", "Father's and mother's names.");
            _illness = AddSection(SecIllness, "Present illness", "The skin problem the patient is here for.");
            _history = AddSection(SecHistory, "Personal and past medical history", "Allergies, medications and supplements, conditions and surgery.");
            _famHistory = AddSection(SecFamily, "Family medical history", "Conditions in the patient's father or mother.");
            _exam = AddSection(SecExam, "Physical examination", "Optional. Usually filled in by the dermatologist; it can be added later by editing the registration.");
            _tests = AddSection(SecTests, "Special tests", "Optional. Wood lamp, KOH and biopsy.");

            NavPanel.Children.Add(Eyebrow("PERSONAL INFORMATION", 0));
            for (int i = 0; i < 5; i++) NavPanel.Children.Add(_sections[i].Item);
            NavPanel.Children.Add(Eyebrow("MEDICAL INFORMATION", 16));
            for (int i = 5; i < 8; i++) NavPanel.Children.Add(_sections[i].Item);
            NavPanel.Children.Add(Eyebrow("EXAMINATION · OPTIONAL", 16));
            for (int i = 8; i < _sections.Count; i++) NavPanel.Children.Add(_sections[i].Item);

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
            bool medical = s.Number > 5;                                  // the medical half of the chart
            s.BadgeBg = medical ? "#E7E8CF" : "#F6E6DF";
            s.BadgeInk = medical ? "#5C5D45" : "#8A5A4C";
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

        /// <summary>Updates the dependent fields, the section ticks and the progress line.</summary>
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

        private static void SetLabel(TextBlock label, string text, bool required)
        {
            label.Inlines.Clear();
            label.Inlines.Add(new Run(text));
            if (required) label.Inlines.Add(new Run(" *") { Foreground = Ui.Brush("#B4483F") });
        }

        /// <summary>Fields that only apply after a choice are greyed out until then.</summary>
        private void UpdateDependentFields()
        {
            bool student = StudentYes.IsChecked == true;
            GradeTxt.IsEnabled = SchoolTxt.IsEnabled = student;
            SetLabel(GradeLabel, "Grade / year", student && _strict);
            SetLabel(SchoolLabel, "School", student && _strict);

            bool female = SelectedChoice(SexPanel) == "Female";
            LmpTxt.IsEnabled = PregnantChk.IsEnabled = BreastfeedingChk.IsEnabled = female;
            FemaleHead.Opacity = female ? 1 : 0.5;
            FemaleHint.Visibility = female ? Visibility.Collapsed : Visibility.Visible;

            bool multiple = MultipleChip.IsChecked == true;
            LesionNumberTxt.IsEnabled = multiple;
            NumberLabel.Opacity = multiple ? 1 : 0.5;
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

        // ------------------------------------------------- family medical history --

        /// <summary>Two blocks of conditions, each with a Father and a Mother tick (the chart's "F/M").</summary>
        private UIElement BuildFamily()
        {
            var both = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -28, 0) };
            foreach (string[] block in new[] { FamilyLeft, FamilyRight })
            {
                var g = new Grid { Margin = new Thickness(0, 0, 28, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 1; c <= 2; c++)
                {
                    TextBlock h = Ui.Text(c == 1 ? "Father" : "Mother", 11.5, true, Ui.Muted, new Thickness(0, 0, 0, 2));
                    h.HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetColumn(h, c);
                    g.Children.Add(h);
                }
                for (int i = 0; i < block.Length; i++)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var name = new Border
                    {
                        BorderBrush = Ui.Brush("#EEE8DA"), BorderThickness = new Thickness(0, 1, 0, 0),
                        Child = Ui.Text(block[i], 13.5, false, Ui.Ink, new Thickness(0))
                    };
                    ((TextBlock)name.Child).VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(name, i + 1);
                    g.Children.Add(name);

                    var boxes = new CheckBox[2];
                    for (int c = 0; c < 2; c++)
                    {
                        // the tick box has no label here: nudge it so the box itself is centred in its column
                        boxes[c] = new CheckBox
                        {
                            Style = (Style)FindResource("Tick"), HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(12, 1, 0, 1), ToolTip = (c == 0 ? "Father: " : "Mother: ") + block[i]
                        };
                        Grid.SetRow(boxes[c], i + 1);
                        Grid.SetColumn(boxes[c], c + 1);
                        g.Children.Add(boxes[c]);
                    }
                    _family[block[i]] = boxes;
                }
                both.Children.Add(g);
            }
            return both;
        }

        /// <summary>"Asthma (Father), Acne (Father/Mother)"</summary>
        private string FamilyText()
        {
            var parts = new List<string>();
            foreach (string name in FamilyLeft.Concat(FamilyRight))
            {
                bool father = _family[name][0].IsChecked == true, mother = _family[name][1].IsChecked == true;
                if (father || mother) parts.Add(name + " (" + (father && mother ? "Father/Mother" : father ? "Father" : "Mother") + ")");
            }
            return string.Join(", ", parts);
        }

        private void SetFamily(string text)
        {
            foreach (CheckBox[] boxes in _family.Values) { boxes[0].IsChecked = false; boxes[1].IsChecked = false; }
            foreach (string piece in (text ?? "").Split(','))
            {
                Match m = Regex.Match(piece.Trim(), @"^(.+?) \((Father|Mother|Father/Mother)\)$");
                CheckBox[] boxes;
                if (!m.Success || !_family.TryGetValue(m.Groups[1].Value, out boxes)) continue;
                boxes[0].IsChecked = m.Groups[2].Value.Contains("Father");
                boxes[1].IsChecked = m.Groups[2].Value.Contains("Mother");
            }
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

        private static string SelectedChoice(Panel p) => p.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string;

        private static void SetChoice(Panel p, string value)
        {
            foreach (RadioButton r in p.Children.OfType<RadioButton>())
                r.IsChecked = !string.IsNullOrEmpty(value) && string.Equals(r.Content as string, value, StringComparison.OrdinalIgnoreCase);
        }

        private static string YesNo(RadioButton yes, RadioButton no) => yes.IsChecked == true ? "Yes" : no.IsChecked == true ? "No" : null;

        private static void SetYesNo(RadioButton yes, RadioButton no, string value)
        {
            yes.IsChecked = value == "Yes";
            no.IsChecked = value == "No";
        }

        /// <summary>Male and Female are the choices on the chart. A different value saved by an earlier form is shown as a third, selected choice so it is not lost.</summary>
        private void SetSex(string sex)
        {
            if (string.IsNullOrWhiteSpace(sex)) return;
            foreach (RadioButton r in SexPanel.Children.OfType<RadioButton>())
                if (r != SexOtherChip && string.Equals(r.Content as string, sex, StringComparison.OrdinalIgnoreCase)) { r.IsChecked = true; return; }
            SexOtherChip.Content = sex;
            SexOtherChip.Visibility = Visibility.Visible;
            SexOtherChip.IsChecked = true;
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
        private void AnyCheck_Changed(object sender, RoutedEventArgs e) => Changed();

        private static DependencyObject ParentOf(DependencyObject d) =>
            d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        /// <summary>
        /// The examination choices are optional: pressing a choice that is already selected clears it (a radio button cannot do this by itself).
        /// Only groups whose panel is marked Tag="Optional" behave this way; Sex and the Yes / No questions are not affected.
        /// </summary>
        private void ClearOptionalChoice(object sender, MouseButtonEventArgs e)
        {
            RadioButton rb = null;
            for (DependencyObject d = e.OriginalSource as DependencyObject; d != null; d = ParentOf(d))
                if (d is RadioButton r) { rb = r; break; }
            if (rb == null || rb.IsChecked != true) return;
            var panel = LogicalTreeHelper.GetParent(rb) as FrameworkElement;
            if (panel == null || !string.Equals(panel.Tag as string, "Optional", StringComparison.Ordinal)) return;
            rb.IsChecked = false;
            e.Handled = true;
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

            // 2. name and personal details (the fields highlighted on the chart)
            if (_strict && T(LastNameTxt) == null) Need(_patient, "the family name");
            if (_strict && T(FirstNameTxt) == null) Need(_patient, "the given name");
            int age;
            bool hasAge = int.TryParse((AgeTxt.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out age);
            if (DobPicker.SelectedDate.HasValue && DobPicker.SelectedDate.Value.Date > DateTime.Today) Need(_patient, "a birthday that is not in the future");
            else if (_strict && !DobPicker.SelectedDate.HasValue) Need(_patient, "the birthday");
            else if (!hasAge) Need(_patient, "the age (or birthday)");
            else if (age < 0 || age > 120) Need(_patient, "a valid age (0 to 120)");
            if (SelectedChoice(SexPanel) == null) Need(_patient, "the sex");
            if (_strict && T(BirthplaceTxt) == null) Need(_patient, "the birthplace");
            if (_strict && Selected(CivilCombo) == null) Need(_patient, "the civil status");
            if (_strict && T(CitizenshipTxt) == null) Need(_patient, "the citizenship");

            // 3. addresses and contact numbers
            if (_strict && T(HomeAddressTxt) == null) Need(_contact, "the home address");
            if (_strict && T(HomeCellTxt) == null) Need(_contact, "the cell phone number");
            Phone(_contact, HomeTelTxt, "home telephone number");
            Phone(_contact, HomeCellTxt, "cell phone number");
            Phone(_contact, ProvTelTxt, "provincial telephone number");
            Phone(_contact, ProvCellTxt, "provincial cell phone number");
            if (T(EmailTxt) != null && !IsEmail(T(EmailTxt))) Need(_contact, "a valid e-mail address");

            // 4. occupation and school
            if (_strict && T(OccupationTxt) == null) Need(_work, "the occupation");
            Phone(_work, BizTelTxt, "business telephone number");
            Phone(_work, BizCellTxt, "business cell phone number");
            string student = YesNo(StudentYes, StudentNo);
            if (_strict && student == null) Need(_work, "whether the patient is a student");
            if (_strict && student == "Yes" && T(GradeTxt) == null) Need(_work, "the grade or year");
            if (_strict && student == "Yes" && T(SchoolTxt) == null) Need(_work, "the school");

            // 5-10: the rest of the chart is optional
            return list;
        }

        private bool Validate(out Participant p, out PatientChart chart)
        {
            p = null; chart = null;

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
                Sex = SelectedChoice(SexPanel),
                Contact = T(HomeCellTxt) ?? T(HomeTelTxt) ?? T(EmailTxt) ?? _legacyContact,      // the short contact shown in lists
                OperatorID = T(OperatorTxt),
                Status = _editMode ? (Selected(StatusCombo) ?? "Active") : "Active"
            };

            bool student = StudentYes.IsChecked == true, female = SelectedChoice(SexPanel) == "Female", multiple = MultipleChip.IsChecked == true;
            chart = new PatientChart
            {
                Birthplace = T(BirthplaceTxt),
                CivilStatus = Selected(CivilCombo),
                Citizenship = T(CitizenshipTxt),
                HomeAddress = T(HomeAddressTxt),
                HomeTel = T(HomeTelTxt),
                HomeCell = T(HomeCellTxt),
                ProvincialAddress = T(ProvAddressTxt),
                ProvincialTel = T(ProvTelTxt),
                ProvincialCell = T(ProvCellTxt),
                Occupation = T(OccupationTxt),
                Email = T(EmailTxt),
                BusinessAddress = T(BizAddressTxt),
                BusinessTel = T(BizTelTxt),
                BusinessCell = T(BizCellTxt),
                IsStudent = YesNo(StudentYes, StudentNo),
                GradeYear = student ? T(GradeTxt) : null,
                School = student ? T(SchoolTxt) : null,
                FatherName = T(FatherTxt),
                MotherName = T(MotherTxt),

                OnsetDate = T(OnsetTxt),
                AffectedArea = T(AreaTxt),
                DescribedLesions = T(DescribedTxt),
                PreviousConsultation = T(ConsultTxt),
                LabExams = T(LabTxt),
                TreatmentNotes = T(TreatmentTxt),
                Orals = T(OralsTxt),
                Topicals = T(TopicalsTxt),

                AllergyFood = T(AllergyFoodTxt),
                AllergyDrugs = T(AllergyDrugsTxt),
                LastMenstrualPeriod = female ? T(LmpTxt) : null,
                Pregnant = female && PregnantChk.IsChecked == true ? "Yes" : null,
                Breastfeeding = female && BreastfeedingChk.IsChecked == true ? "Yes" : null,
                Supplements = Null(Checked(SupplementPanel)),
                SupplementsOther = T(SupplementOtherTxt),
                Conditions = Null(Checked(ConditionPanel)),
                ConditionsOther = T(ConditionOtherTxt),
                Surgery = T(SurgeryTxt),
                SurgeryYear = T(SurgeryYearTxt),

                FamilyHistory = Null(FamilyText()),
                FamilyHistoryOther = T(FamilyOtherTxt),

                LesionCount = SelectedChoice(CountPanel),
                LesionNumber = multiple ? T(LesionNumberTxt) : null,
                Pruritus = SelectedChoice(ItchPanel),
                Erythema = SelectedChoice(RednessPanel),
                LesionColor = SelectedChoice(ColorPanel),
                LesionTypes = Null(Checked(LesionTypePanel)),
                Distribution = T(DistributionTxt),

                WoodLamp = T(WoodLampTxt),
                Koh = SelectedChoice(KohPanel),
                Biopsy = T(BiopsyTxt)
            };

            // a patient registered with an earlier form whose chart was left empty gets no empty chart row
            PatientChart c = chart;
            if (!_strict && typeof(PatientChart).GetProperties().All(x => x.Name == "ParticipantID" || x.Name == "UpdatedAt" || x.GetValue(c) == null))
                chart = null;
            return true;
        }

        // ---------------------------------------------------------- actions --

        /// <summary>Returns true when the data is saved (or already saved and unchanged).</summary>
        private bool Save()
        {
            if (!_editMode && _participantId != null && !_dirty) return true;

            Participant p; PatientChart chart;
            if (!Validate(out p, out chart)) return false;

            try
            {
                if (_editMode)
                {
                    StudyRepository.UpdateParticipant(p, _skin, EditReasonTxt.Text.Trim(), null, chart);
                }
                else if (_participantId == null)
                {
                    _participantId = StudyRepository.RegisterParticipant(p, _skin, null, chart);
                    PatientIdTxt.Text = _participantId;
                    AppSession.CurrentParticipantId = _participantId;
                    PageTitle.Text = "Patient registration · " + _participantId;
                    SaveBtn.Content = "Save changes";
                }
                else
                {
                    StudyRepository.UpdateParticipant(p, _skin, "Correction during registration", null, chart);
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
