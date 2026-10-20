// ParticipantPickerWindow.cs - "Skin Analysis" needs an existing participant: search by PatientID or name,
// pick the right one from the matches, then Continue. Nothing is chosen automatically and searching never creates a record.
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class ParticipantPickerWindow : Window
    {
        private readonly TextBox _searchBox;
        private readonly ListBox _matches;
        private readonly TextBlock _message;
        private readonly TextBlock _summary;
        private readonly Border _summaryBox;
        private readonly Button _continue;
        private readonly DispatcherTimer _debounce;
        private Participant _selected;

        /// <summary>The participant the researcher explicitly selected (null until one is selected).</summary>
        public string SelectedParticipantId => _selected != null ? _selected.ParticipantID : null;

        public ParticipantPickerWindow()
        {
            Title = "Choose a patient";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var sp = new StackPanel();

            // header: icon, title, close
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var close = new Button
            {
                Style = (Style)Application.Current.FindResource("GhostButton"), Width = 44, Height = 44, Padding = new Thickness(0),
                Content = Ui.Icon("Icon.Close", 18), IsCancel = true, ToolTip = "Close", VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            Border badge = Ui.IconBadge("Icon.Users", 52, "#E7E8CF", "#5F6048");
            badge.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Ui.Text("SKIN ANALYSIS", 11, true, Ui.Brush("#8D8D76"), new Thickness(0)));
            titles.Children.Add(Ui.Text("Choose a patient", 21, true, Ui.Brush("#2F3024"), new Thickness(0, 1, 0, 0)));
            head.Children.Add(titles);
            sp.Children.Add(head);
            sp.Children.Add(Ui.Text("Search by Patient ID or name (for example PS-0001 or Elaiza). Partial text works.", 13, false, Ui.Muted, new Thickness(0, 0, 0, 12)));

            // search box with an icon inside, and a search button
            var row = new DockPanel();
            var search = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 50, Margin = new Thickness(10, 0, 0, 0), Content = Ui.IconLabel("Icon.Search", "Search", 17) };
            search.Click += (s, e) => Search();
            DockPanel.SetDock(search, Dock.Right);
            row.Children.Add(search);
            var box = new Grid();
            _searchBox = new TextBox { Style = (Style)Application.Current.FindResource("Field"), Height = 50, FontSize = 15, Padding = new Thickness(38, 0, 10, 0), ToolTip = "Search by Patient ID or name" };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Search();
                else if (e.Key == Key.Down && _matches.Items.Count > 0) { _matches.Focus(); if (_matches.SelectedIndex < 0) _matches.SelectedIndex = 0; }
            };
            _searchBox.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };      // search while typing, after a short pause
            box.Children.Add(_searchBox);
            var glass = Ui.Icon("Icon.Search", 18, Ui.Brush("#9B9B84"));
            glass.HorizontalAlignment = HorizontalAlignment.Left;
            glass.Margin = new Thickness(14, 0, 0, 0);
            glass.IsHitTestVisible = false;
            box.Children.Add(glass);
            row.Children.Add(box);
            sp.Children.Add(row);

            _message = Ui.Text("", 12.5, false, Ui.Muted, new Thickness(2, 10, 0, 6));
            sp.Children.Add(_message);

            _matches = new ListBox { MaxHeight = 280, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(_matches, ScrollBarVisibility.Disabled);
            _matches.SelectionChanged += (s, e) => OnSelected();
            _matches.MouseDoubleClick += (s, e) => { if (_continue.IsEnabled) DialogResult = true; };
            sp.Children.Add(_matches);

            _summary = Ui.Text("", 13, false, Ui.Ink, new Thickness(0));
            _summary.LineHeight = 20;
            _summaryBox = new Border { Style = (Style)Application.Current.FindResource("Panel"), Margin = new Thickness(0, 10, 0, 0), Child = _summary, Visibility = Visibility.Collapsed };
            sp.Children.Add(_summaryBox);

            var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
            var register = new Button { Style = (Style)Application.Current.FindResource("GhostButton"), Height = 50, Content = Ui.IconLabel("Icon.UserPlus", "Register new patient", 18) };
            register.Click += (s, e) => { DialogResult = false; Nav.Go(new RegistrationPage()); };
            DockPanel.SetDock(register, Dock.Left);
            buttons.Children.Add(register);
            _continue = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 50, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Right };
            var cont = new StackPanel { Orientation = Orientation.Horizontal };
            cont.Children.Add(new TextBlock { Text = "Continue", VerticalAlignment = VerticalAlignment.Center });
            var chev = Ui.Icon("Icon.ChevronRight", 18);
            chev.Margin = new Thickness(8, 0, 0, 0);
            cont.Children.Add(chev);
            _continue.Content = cont;
            _continue.Click += (s, e) => DialogResult = true;
            _continue.IsEnabled = false;                              // only after a participant is selected
            buttons.Children.Add(_continue);
            sp.Children.Add(buttons);

            _debounce = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(350) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Search(); };

            LumyDialog.Frame(this, sp, 600);
            Loaded += (s, e) => _searchBox.Focus();
        }

        /// <summary>One patient in the result list: initials, ID and name, age / sex / registration, and the record status.</summary>
        private static UIElement PatientRow(Participant p)
        {
            var dock = new DockPanel();
            string initials = string.Concat((p.FullName ?? p.ParticipantID ?? "?").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            var avatar = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Ui.Brush("#EFD7CF"), Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = initials, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("#2F3024"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            DockPanel.SetDock(avatar, Dock.Left);
            dock.Children.Add(avatar);

            bool withdrawn = p.Status == "Withdrawn";
            var chip = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Ui.Brush(withdrawn ? "#F8E6E2" : "#EAF1E4"),
                Child = new TextBlock { Text = withdrawn ? "Withdrawn" : (p.Status ?? "Active"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = withdrawn ? Ui.Bad : Ui.Good }
            };
            DockPanel.SetDock(chip, Dock.Right);
            dock.Children.Add(chip);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new TextBlock { FontSize = 14, Foreground = Ui.Brush("#2F3024"), TextTrimming = TextTrimming.CharacterEllipsis };
            top.Inlines.Add(new System.Windows.Documents.Run(p.ParticipantID) { FontWeight = FontWeights.Bold });
            top.Inlines.Add(new System.Windows.Documents.Run("   " + (p.FullName ?? "")));
            text.Children.Add(top);
            string sub = (p.Age.HasValue ? p.Age + " years" : "Age not recorded") + (string.IsNullOrEmpty(p.Sex) ? "" : "  ·  " + p.Sex)
                         + "  ·  Registered " + Fmt.Date((p.RegisteredAt ?? "").Split(' ')[0]);
            text.Children.Add(Ui.Text(sub, 12, false, Ui.Muted, new Thickness(0, 2, 0, 0)));
            dock.Children.Add(text);
            return dock;
        }

        private void Search()
        {
            _debounce.Stop();
            _selected = null;
            _continue.IsEnabled = false;
            _summary.Text = "";
            _summaryBox.Visibility = Visibility.Collapsed;
            _matches.Items.Clear();

            string text = _searchBox.Text.Trim();
            if (text.Length == 0)
            {
                _matches.Visibility = Visibility.Collapsed;
                _message.Foreground = Ui.Muted;
                _message.Text = "Type a Patient ID or a name.";
                return;
            }

            List<Participant> found;
            try { found = StudyRepository.SearchParticipants(text, "", ""); }
            catch (System.Exception ex)
            {
                _matches.Visibility = Visibility.Collapsed;
                _message.Foreground = Ui.Bad;
                _message.Text = "Database error: " + ex.Message;
                return;
            }

            if (found.Count == 0)
            {
                _matches.Visibility = Visibility.Collapsed;
                _message.Foreground = Ui.Bad;
                _message.Text = "No patient matches \"" + text + "\". Check the spelling, or register a new patient.";
                return;
            }

            foreach (Participant p in found.Take(50))
            {
                _matches.Items.Add(new ListBoxItem { Content = PatientRow(p), Tag = p, Padding = new Thickness(10, 9, 12, 9) });
            }
            _matches.Visibility = Visibility.Visible;
            _message.Foreground = Ui.Muted;
            _message.Text = found.Count == 1 ? "1 match. Click it to select." : found.Count + " matches" + (found.Count > 50 ? " (showing 50)" : "") + ". Click the right patient.";
        }

        private void OnSelected()
        {
            var item = _matches.SelectedItem as ListBoxItem;
            _selected = null;
            _continue.IsEnabled = false;
            if (item == null) { _summary.Text = ""; _summaryBox.Visibility = Visibility.Collapsed; return; }
            _summaryBox.Visibility = Visibility.Visible;

            var p = (Participant)item.Tag;
            Consent k = StudyRepository.GetLatestConsent(p.ParticipantID);
            string head = "Selected:  " + p.ParticipantID + "  ·  " + p.FullName +
                          (p.Age.HasValue ? "  ·  age " + p.Age : "") + (string.IsNullOrEmpty(p.Sex) ? "" : "  ·  " + p.Sex) +
                          "\nRegistered " + (p.RegisteredAt ?? "").Split(' ')[0] + "  ·  " + p.Status + "\n";

            if (p.Status == "Withdrawn")
            {
                _summary.Foreground = Ui.Bad;
                _summary.Text = head + "This patient has withdrawn from the study. No new scans can be recorded.";
                return;                                            // Continue stays disabled
            }

            _summary.Foreground = Ui.Ink;
            _summary.Text = head + "Consent: " + (k == null ? "not recorded — the consent screen will open next"
                                                  : k.AllowsScanning ? "given " + k.RecordedAt
                                                  : k.Decision + " " + k.RecordedAt + " — consent will be asked again");
            _selected = p;
            _continue.IsEnabled = true;
        }
    }
}
