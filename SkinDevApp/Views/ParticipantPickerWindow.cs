// ParticipantPickerWindow.cs - "Skin Analysis" needs an existing participant: search by PatientID or name,
// pick the right one from the matches, then Continue. Nothing is chosen automatically and searching never creates a record.
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly Button _continue;
        private readonly DispatcherTimer _debounce;
        private Participant _selected;

        /// <summary>The participant the researcher explicitly selected (null until one is selected).</summary>
        public string SelectedParticipantId => _selected != null ? _selected.ParticipantID : null;

        public ParticipantPickerWindow()
        {
            Title = "Select participant";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Ui.Brush("#FBF9F6");

            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(Ui.Text("Select an existing participant", 18, true));
            sp.Children.Add(Ui.Text("Search by Patient ID or participant name (for example PS-0001 or Elaiza). Partial text works.", 12, false, Ui.Muted));

            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var search = Ui.Btn("Search", (s, e) => Search(), "SecondaryButton");
            search.Height = 36;
            search.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(search, Dock.Right);
            row.Children.Add(search);
            _searchBox = new TextBox { Style = (Style)Application.Current.FindResource("Field"), ToolTip = "Search by Patient ID or participant name" };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Search();
                else if (e.Key == Key.Down && _matches.Items.Count > 0) { _matches.Focus(); if (_matches.SelectedIndex < 0) _matches.SelectedIndex = 0; }
            };
            _searchBox.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };      // search while typing, after a short pause
            row.Children.Add(_searchBox);
            sp.Children.Add(row);

            _message = Ui.Text("", 12, false, Ui.Muted, new Thickness(0, 10, 0, 4));
            sp.Children.Add(_message);

            _matches = new ListBox { MaxHeight = 190, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = System.Windows.Media.Brushes.White, Visibility = Visibility.Collapsed };
            _matches.SelectionChanged += (s, e) => OnSelected();
            _matches.MouseDoubleClick += (s, e) => { if (_continue.IsEnabled) DialogResult = true; };
            sp.Children.Add(_matches);

            _summary = Ui.Text("", 13, false, Ui.Ink, new Thickness(0, 12, 0, 0));
            sp.Children.Add(_summary);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var register = Ui.Btn("Register new participant", (s, e) => { DialogResult = false; Nav.Go(new RegistrationPage()); }, "SecondaryButton");
            register.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(register);
            _continue = Ui.Btn("Continue", (s, e) => DialogResult = true, "PrimaryButton");
            _continue.IsEnabled = false;                              // only after a participant is selected
            buttons.Children.Add(_continue);
            sp.Children.Add(buttons);

            _debounce = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(350) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Search(); };

            Content = sp;
            Loaded += (s, e) => _searchBox.Focus();
        }

        private void Search()
        {
            _debounce.Stop();
            _selected = null;
            _continue.IsEnabled = false;
            _summary.Text = "";
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
                _message.Text = "No participant matches \"" + text + "\". Check the spelling, or register a new participant.";
                return;
            }

            foreach (Participant p in found.Take(50))
            {
                string line = p.ParticipantID + " — " + p.FullName + (p.Age.HasValue ? " — Age " + p.Age : "") + (string.IsNullOrEmpty(p.Sex) ? "" : " — " + p.Sex)
                              + (p.Status == "Withdrawn" ? " — WITHDRAWN" : "");
                _matches.Items.Add(new ListBoxItem { Content = line, Tag = p, Padding = new Thickness(8, 6, 8, 6), FontSize = 13 });
            }
            _matches.Visibility = Visibility.Visible;
            _message.Foreground = Ui.Muted;
            _message.Text = found.Count == 1 ? "1 match. Click it to select." : found.Count + " matches" + (found.Count > 50 ? " (showing 50)" : "") + ". Click the right participant.";
        }

        private void OnSelected()
        {
            var item = _matches.SelectedItem as ListBoxItem;
            _selected = null;
            _continue.IsEnabled = false;
            if (item == null) { _summary.Text = ""; return; }

            var p = (Participant)item.Tag;
            Consent k = StudyRepository.GetLatestConsent(p.ParticipantID);
            string head = "Selected: " + p.ParticipantID + "  ·  " + p.FullName +
                          (p.Age.HasValue ? "  ·  age " + p.Age : "") + (string.IsNullOrEmpty(p.Sex) ? "" : "  ·  " + p.Sex) +
                          "\nRegistered " + (p.RegisteredAt ?? "").Split(' ')[0] + "  ·  " + p.Status + "\n";

            if (p.Status == "Withdrawn")
            {
                _summary.Foreground = Ui.Bad;
                _summary.Text = head + "This participant has withdrawn from the study. No new scans can be recorded.";
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
