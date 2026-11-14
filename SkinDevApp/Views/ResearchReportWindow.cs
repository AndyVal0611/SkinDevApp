// ============================================================================
// ResearchReportWindow.cs  -  namespace SkinDevApp.Views
//
// "PrecisionSkin Research Analysis Report" for one saved scan session. Built only
// from the database and the saved image files, so a report generated later shows
// exactly what was stored. Readability of the skin images comes before showing
// every image: the complete 15-image set stays in the scan record and in the
// Comparison Gallery.
//
//   Page 1    Analysis summary (identity, model, overall + per-view scores, status)
//   Page 2    Original captures (Front / Left / Right, large, no overlay) + Major Findings
//   Page 3+   Selected AI attribution evidence: Original | Grad-CAM++ side by side,
//             only the maps that matter (see ReportFindings), two findings per page
//   Then      Licensed dermatologist validation form, researcher assessment form
//             (printed, completed by hand and signed; staff transcribe them in the
//             Researcher Verification window)
//
// Pages are plain WPF elements. For the PDF they are placed into the document as real
// content (selectable text, full-resolution photographs) rather than as a flat picture.
// Not titled as a medical / diagnostic report.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public sealed class ResearchReportWindow : Window
    {
        // Pages are laid out on a 595 x 842 canvas (A4 proportions) and scaled to A4 for printing.
        private const double PageW = 595, PageH = 842;
        private const double ImgW = 255;                       // widest inspection image (page content is 523 wide)
        private const double OriginalH = 255, OriginalHWithBanner = 232, EvidenceH2 = 226, EvidenceH1 = 255;

        // restrained palette
        private const string Navy = "#4F503B", TextInk = "#2F3024", Muted = "#6F6F58", RuleGray = "#DCD4C1",
                             PanelBg = "#F7F5EC", ImgBg = "#EFEADC", WarnInk = "#92400E", WarnBg = "#FAF7F0", WarnLine = "#F5C99A";

        private static readonly string[] ResearchTeamMembers =
        {
            "Elaiza Czarina Claire C. Bautista",
            "Francesca Nicolette S. Pineda",
            "Andrea M. Valdez"
        };
        private static readonly string ResearchTeam = string.Join(" · ", ResearchTeamMembers);

        private readonly SessionDetail _d;
        private readonly ReportFindings _f;
        private readonly Button _pdfBtn;

        public static void Open(string scanSessionId)
        {
            try
            {
                SessionDetail d = StudyRepository.GetSessionDetail(scanSessionId);
                if (d == null)
                {
                    LumyDialog.Show("Scan " + scanSessionId + " is not in the database yet.", "LUMYVUE Report");
                    return;
                }
                new ResearchReportWindow(d) { Owner = Application.Current.MainWindow }.ShowDialog();
            }
            catch (Exception ex)
            {
                LumyDialog.Show("Could not build the report: " + ex.Message, "LUMYVUE Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ResearchReportWindow(SessionDetail d)
        {
            _d = d;
            _f = ReportFindings.Build(d);
            Title = "LUMYVUE Analysis Report — " + d.Session.DisplayId;
            Width = 820;
            Height = 940;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("PageWash");
            FontFamily = (FontFamily)Application.Current.FindResource("BodyFont");

            var pagesPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
            List<FrameworkElement> pages = BuildPages();
            foreach (FrameworkElement p in pages)
            {
                p.Margin = new Thickness(0, 0, 0, 20);
                // each page sits on the screen like a sheet of paper (the shadow is on its own layer, so the page text stays sharp)
                var sheet = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                sheet.Children.Add(new Border
                {
                    Background = Brushes.White, Margin = new Thickness(0, 0, 0, 20), CornerRadius = new CornerRadius(2),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.16, Color = (Color)ColorConverter.ConvertFromString("#2F3024") }
                });
                sheet.Children.Add(p);
                pagesPanel.Children.Add(sheet);
            }

            var band = Ui.WindowBand("Analysis report", "Scan " + d.Session.DisplayId + "  ·  Patient " + (d.Session.ParticipantID ?? "not assigned") +
                                     "  ·  " + pages.Count + (pages.Count == 1 ? " page" : " pages"));
            band.Margin = new Thickness(16, 16, 16, 0);

            var bar = new DockPanel { LastChildFill = false };
            var saveBtn = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 50, Content = Ui.IconLabel("Icon.Folder", "Save to Reports", 18) };
            saveBtn.Click += (s, e) => SaveToReportsFolder();
            DockPanel.SetDock(saveBtn, Dock.Right);
            bar.Children.Add(saveBtn);
            _pdfBtn = new Button { Style = (Style)Application.Current.FindResource("SecondaryButton"), Height = 50, Margin = new Thickness(0, 0, 10, 0), Content = Ui.IconLabel("Icon.Report", "Print or save as…", 18) };
            _pdfBtn.Click += (s, e) => Export();
            DockPanel.SetDock(_pdfBtn, Dock.Right);
            bar.Children.Add(_pdfBtn);
            var close = new Button { Style = (Style)Application.Current.FindResource("GhostButton"), Height = 50, Width = 120, Content = "Close", Margin = new Thickness(0, 0, 10, 0) };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
            var hint = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hint.Children.Add(Ui.Icon("Icon.Info", 16, Ui.Muted));
            ((FrameworkElement)hint.Children[0]).Margin = new Thickness(0, 0, 8, 0);
            hint.Children.Add(Ui.Text("\"Save to Reports\" keeps a PDF in the Reports folder (menu > Reports), ready to copy to a USB drive.", 12, false, Ui.Muted, new Thickness(0)));
            bar.Children.Add(hint);
            var barCard = new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(18, 12, 12, 12), Margin = new Thickness(16, 0, 16, 16), Child = bar };

            var root = new DockPanel();
            DockPanel.SetDock(band, Dock.Top);
            root.Children.Add(band);
            DockPanel.SetDock(barCard, Dock.Bottom);
            root.Children.Add(barCard);
            root.Children.Add(new ScrollViewer { Content = pagesPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
        }

        // ------------------------------------------------------------ helpers --

        private static SolidColorBrush C(string hex) => Ui.Brush(hex);

        private static TextBlock T(string text, double size, bool bold = false, string color = TextInk, double bottom = 2)
        {
            return new TextBlock
            {
                Text = text ?? "—",
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = C(color),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, bottom)
            };
        }

        private static TextBlock Heading(string title, double top = 10)
        {
            var t = T(title.ToUpperInvariant(), 8.5, true, Navy, 4);
            t.Margin = new Thickness(0, top, 0, 4);
            return t;
        }

        private static Border Rule(double top = 4, double bottom = 6, string color = RuleGray) =>
            new Border { Height = 1, Background = C(color), Margin = new Thickness(0, top, 0, bottom) };

        private static string Lbl(string cls) => ReportFindings.Label(cls);
        private static string Pc(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "%";

        private static Grid Table(IEnumerable<KeyValuePair<string, string>> rows, double labelWidth = 110, double size = 8.5)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int i = 0;
            foreach (var kv in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = T(kv.Key, size, false, Muted, 3);
                var v = T(string.IsNullOrWhiteSpace(kv.Value) ? "—" : kv.Value, size, true, TextInk, 3);
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                g.Children.Add(k); g.Children.Add(v);
                i++;
            }
            return g;
        }

        private static Border Panel(UIElement child, string bg = PanelBg, string line = null, double pad = 10)
        {
            return new Border
            {
                Background = C(bg),
                BorderBrush = line == null ? null : C(line),
                BorderThickness = new Thickness(line == null ? 0 : 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(pad),
                Child = child
            };
        }

        private static string FirstUnlinked(ScanSessionRow s) =>
            s.ParticipantID ?? "not assigned" + (string.IsNullOrEmpty(s.LegacyPatientLabel) ? "" : " (" + s.LegacyPatientLabel + ")");

        // -------------------------------------------------------- page frame --

        private List<FrameworkElement> BuildPages()
        {
            var bodies = new List<FrameworkElement>
            {
                SummaryBody(),
                OriginalsBody()
            };
            if (_f.V6 != null)
                bodies.Add(V6MarkedAreasBody());         // V6 flag on: the boxes that supported the unified assessment
            else if (_d.Views.Any(v => v.Localization != null && v.Localization.Status == "OK"))
                bodies.Add(LocalizationBody());          // candidate lesion boxes (separate detector), same frames
            bodies.AddRange(EvidenceBodies());
            bodies.Add(FormBody(true));      // licensed dermatologist first

            var pages = new List<FrameworkElement>();
            for (int i = 0; i < bodies.Count; i++) pages.Add(Wrap(bodies[i], i + 1, bodies.Count));
            return pages;
        }

        private Border Wrap(FrameworkElement body, int n, int total)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // header
            var head = new StackPanel();
            var top = new DockPanel();
            var right = T("Analysis Report", 9, false, Muted, 0);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);
            top.Children.Add(T("LUMYVUE", 15, true, Navy, 0));
            head.Children.Add(top);
            head.Children.Add(new Border { Height = 1.5, Background = C(Navy), Margin = new Thickness(0, 0, 0, 10) });
            Grid.SetRow(head, 0);
            g.Children.Add(head);

            body.VerticalAlignment = VerticalAlignment.Top;
            var host = new Border { Child = body, ClipToBounds = true };
            Grid.SetRow(host, 1);
            g.Children.Add(host);

            // footer
            var foot = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foot.Children.Add(new Border { Height = 1, Background = C(RuleGray), Margin = new Thickness(0, 0, 0, 4) });
            var line = new DockPanel();
            var page = T("Page " + n + " of " + total, 8, false, Muted, 0);
            DockPanel.SetDock(page, Dock.Right);
            line.Children.Add(page);
            line.Children.Add(T("Patient " + FirstUnlinked(_d.Session) + "   ·   Scan " + _d.Session.DisplayId +
                                "", 8, false, Muted, 0));
            foot.Children.Add(line);
            Grid.SetRow(foot, 2);
            g.Children.Add(foot);

            return new Border
            {
                Width = PageW,
                Height = PageH,
                Background = Brushes.White,
                Padding = new Thickness(36, 26, 36, 20),
                Child = g
            };
        }

        // ------------------------------------------------------------- page 1 --

        private FrameworkElement SummaryBody()
        {
            ScanSessionRow s = _d.Session;
            ModelVersion m = _d.Model;
            var sp = new StackPanel();

            sp.Children.Add(T("Analysis Report", 21, true, Navy, 0));
            sp.Children.Add(T("Skin screening  ·  generated " + DateTime.Now.ToString("MMM d, yyyy · h:mm tt", System.Globalization.CultureInfo.InvariantCulture), 9, false, Muted, 8));
            if (_d.Participant != null && _d.Participant.Status == "Withdrawn")
            {
                sp.Children.Add(Panel(T("This patient is marked as withdrawn. Do not include this scan in any analysis.", 9, true, WarnInk, 0), WarnBg, WarnLine, 7));
                sp.Children.Add(new Border { Height = 8 });
            }

            // identification
            var two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string completeness = s.ScanMode == "Single" ? "Single capture"
                : string.Join("  ", StudyText.Views.Select(v => (_d.View(v)?.Scores != null ? "✓ " : "✗ ") + v)) + "   (" + s.CompletionText.Replace(" / ", " of ") + ")";
            var left = new StackPanel();
            left.Children.Add(Heading("Patient and scan", 0));
            left.Children.Add(Table(new[]
            {
                Ui.KV("Patient ID", FirstUnlinked(s)),
                Ui.KV("Scan", s.DisplayId),
                Ui.KV("Scan date and time", Fmt.When(s.StartedAt)),
                Ui.KV("Scan completeness", completeness),
                Ui.KV("Scan status", s.ScanStatus)
            }, 104));
            two.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(Heading("Result at a glance", 0));
            right.Children.Add(Table(new[]
            {
                Ui.KV("Screening result", _f.V6 != null ? _f.V6.Clinical.CategoryLabel : "—"),
                Ui.KV("Review status", _f.V6 != null ? _f.V6.ReviewStatus : (s.ReviewStatus ?? ReviewStatus.Pending)),
                Ui.KV("Images read", s.ScanMode == "Single" ? "1" : s.CompletionText.Replace(" / ", " of "))
            }, 104));
            Grid.SetColumn(right, 2);
            two.Children.Add(right);
            sp.Children.Add(two);
            sp.Children.Add(Rule(10, 0));

            if (_f.V6 != null)
            {
                // The final model's clinical reading: outcome, findings table (result, certainty, facial regions), review reasons, verification status
                SkinDevApp.V6.ClinicalSummary cs = _f.V6.Clinical;
                sp.Children.Add(Heading("Screening result"));
                sp.Children.Add(T(cs.CategoryLabel.ToUpperInvariant() + "  ·  Verification: " + _f.V6.ReviewStatus, 8.5, true,
                                  cs.Category == SkinDevApp.V6.ClinicalCategory.NoConditionDetected ? TextInk : WarnInk, 1));
                sp.Children.Add(T(cs.Title, 13, true, Navy, 2));
                sp.Children.Add(T(cs.Explanation.Replace("The AI found", "The screening found").Replace("The AI ", "The screening "), 9, false, TextInk, 5));
                var ft = new Grid();
                foreach (double w in new[] { 120.0, 110.0, 80.0 }) ft.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                ft.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string[] fh = { "Condition", "Result", "Certainty", "Facial regions" };
                ft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int ci = 0; ci < fh.Length; ci++) { var h = T(fh[ci], 8, true, Muted, 2); Grid.SetColumn(h, ci); ft.Children.Add(h); }
                int fr = 1;
                foreach (SkinDevApp.V6.ClinicalFinding cf in cs.Findings)
                {
                    ft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var cells = new[]
                    {
                        T(cf.Condition, 9, true, TextInk, 1),
                        T(cf.Status + (cf.NeedsReview ? " (review)" : ""), 9, cf.IsDetected, cf.IsDetected ? Navy : cf.NeedsReview ? WarnInk : TextInk, 1),
                        T(cf.Status == SkinDevApp.V6.ClinicalSummary.Detected || cf.Status == SkinDevApp.V6.ClinicalSummary.NotDetected ? cf.Certainty : "—", 9, false, TextInk, 1),
                        T(cf.Regions.Count == 0 ? "—" : string.Join("; ", cf.Regions), 9, false, TextInk, 1)
                    };
                    for (int ci = 0; ci < cells.Length; ci++) { Grid.SetRow(cells[ci], fr); Grid.SetColumn(cells[ci], ci); ft.Children.Add(cells[ci]); }
                    fr++;
                }
                sp.Children.Add(ft);
                foreach (string rr in cs.ReviewReasons.Distinct()) sp.Children.Add(T("•  " + rr, 8.5, false, WarnInk, 1));
            }
            else
            {
            // overall result
            sp.Children.Add(Heading(s.ScanMode == "Single" ? "AI result" : "Overall multi-view AI result"));
            var res = new Grid();
            res.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            res.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            res.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var big = new StackPanel();
            big.Children.Add(T("Primary AI classification", 8.5, false, Muted, 1));
            big.Children.Add(T(s.OverallPredictedClass == null ? "No usable result" : Lbl(s.OverallPredictedClass), 22, true, Navy, 1));
            if (s.OverallScore.HasValue)
                big.Children.Add(T(s.ScoreText + (s.ScanMode == "Single" ? " Model Class Score" : " pooled Model Class Score"), 10, false, TextInk, 6));
            big.Children.Add(T(StudyText.ScoresNote, 7.5, false, Muted, 0));
            res.Children.Add(big);

            double[] overall = s.ScanMode == "Single" || _d.Fusion == null || _d.Fusion.PooledPercent == null
                ? (_d.Views.FirstOrDefault(v => v.Scores != null)?.Scores?.AsArray() ?? new double[4])
                : StudyText.Classes.Select(c => { double v; return _d.Fusion.PooledPercent.TryGetValue(c, out v) ? v : 0; }).ToArray();
            var bars = new StackPanel();
            foreach (string cls in new[] { "Acne", "Eczema", "Hyperpigmentation", "Normal" })
            {
                double v = overall[Array.IndexOf(StudyText.Classes, cls)];
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };
                var val = T(Pc(v), 8.5, true, TextInk, 0);
                DockPanel.SetDock(val, Dock.Right);
                row.Children.Add(val);
                row.Children.Add(T(Lbl(cls), 8.5, false, TextInk, 0));
                bars.Children.Add(row);
                bars.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = v, Height = 4, Foreground = C(Ui.ClassColor(cls)), Background = C("#EEF0F2"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 6) });
            }
            Grid.SetColumn(bars, 2);
            res.Children.Add(bars);
            sp.Children.Add(res);

            // per view
            if (_d.Views.Count > 0)
            {
                sp.Children.Add(Heading("Per-view Model Class Scores"));
                var g = new Grid();
                string[] head = { "View", "Primary class", "Score", "Acne", "Eczema", "Hyperpig.", "Normal Skin", "Capture quality" };
                double[] widths = { 40, 84, 44, 40, 42, 52, 62, 1 };
                for (int c = 0; c < head.Length; c++)
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = c == head.Length - 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(widths[c]) });
                int r = 0;
                void Row(bool header, params string[] cells)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (int c = 0; c < cells.Length; c++)
                    {
                        var t = T(cells[c], 8.5, header || c == 0, header ? Muted : TextInk, 3);
                        t.Margin = new Thickness(0, 2, 6, 3);
                        Grid.SetRow(t, r); Grid.SetColumn(t, c);
                        g.Children.Add(t);
                    }
                    r++;
                    var line = new Border { Height = 1, Background = C(header ? Navy : "#E5E7EB"), VerticalAlignment = VerticalAlignment.Bottom };
                    Grid.SetRow(line, r - 1); Grid.SetColumnSpan(line, head.Length);
                    g.Children.Add(line);
                }
                Row(true, head);
                foreach (CaptureViewRow v in _d.Views)
                {
                    if (v.Scores == null) { Row(false, v.ViewType, "not analyzed", "", "", "", "", "", v.QualityStatus ?? ""); continue; }
                    double[] a = v.Scores.AsArray();
                    Row(false, v.ViewType, Lbl(v.Scores.PredictedClass), Pc(v.Scores.Confidence), Pc(a[0]), Pc(a[2]), Pc(a[1]), Pc(a[3]), v.QualityStatus ?? "");
                }
                sp.Children.Add(g);
            }
            }

            // status
            sp.Children.Add(Heading("Consistency and review status"));
            if (_f.V6 == null && s.ScanMode != "Single")
            {
                if (_f.ReviewRecommended)
                {
                    var w = new StackPanel();
                    foreach (string flag in _f.Flags) w.Children.Add(T("•  " + flag, 8.5, flag.StartsWith("View disagreement"), WarnInk, 2));
                    sp.Children.Add(Panel(w, WarnBg, WarnLine, 8));
                }
                else sp.Children.Add(T("Views consistent: every captured angle has the same primary class; no review flags were raised.", 8.5, false, TextInk, 2));
                sp.Children.Add(T("Fusion method: quality-weighted pooling of the per-view scores. It has not yet been clinically validated.", 7.5, false, Muted, 2));
            }
            else if (_f.ReviewRecommended)
            {
                var w = new StackPanel();
                foreach (string flag in _f.Flags) w.Children.Add(T("•  " + flag, 8.5, false, WarnInk, 2));
                sp.Children.Add(Panel(w, WarnBg, WarnLine, 8));
            }
            else if (_f.V6 != null) sp.Children.Add(T("No review flags were raised. Every result still waits for dermatologist review.", 8.5, false, TextInk, 2));

            DermatologistValidation dv = _d.Validations.FirstOrDefault(x => x.ValidationStatus != ReviewStatus.ValidationDraft) ?? _d.Validations.FirstOrDefault();
            SkinProfile skin = string.IsNullOrEmpty(s.ParticipantID) ? null : StudyRepository.GetSkinProfile(s.ParticipantID);
            sp.Children.Add(Heading("Human assessment"));
            var humanRows = new List<KeyValuePair<string, string>>
            {
                Ui.KV("Dermatologist review", dv == null ? "Pending" : (dv.DermatologistAssessment ?? "—") + " · " + (ReviewStatus.FromValidation(dv.ValidationStatus) ?? "draft") + " · " + dv.DermatologistID + " · " + Fmt.When(dv.ValidationDate)),
                Ui.KV("Verification status", s.ReviewStatus ?? ReviewStatus.Pending)
            };
            if (dv != null)
            {
                // what the dermatologist recorded in the review window: who, each condition's verdict, and the notes (read from the saved review)
                string who = string.Join(" · ", new[] { dv.ValidatorName, string.IsNullOrWhiteSpace(dv.CredentialReference) ? null : "Reg. no. " + dv.CredentialReference }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (who.Length > 0) humanRows.Add(Ui.KV("Reviewed by", who));
                foreach (var cr in StudyRepository.LatestConditionReviews(s.ScanSessionID))
                    humanRows.Add(Ui.KV(cr.Key, (cr.Value.Status ?? "—") + (string.IsNullOrWhiteSpace(cr.Value.Observation) ? "" : " — " + cr.Value.Observation)));
                if (!string.IsNullOrWhiteSpace(dv.Notes)) humanRows.Add(Ui.KV("Dermatologist notes", dv.Notes));
            }
            sp.Children.Add(Table(humanRows, 150));

            // disclaimer + full hash
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(Panel(new StackPanel
            {
                Children =
                {
                    T("Note", 8.5, true, Navy, 2),
                    T(_f.V6 != null ? "Every result must be confirmed by a dermatologist." : StudyText.PrototypeNotice + " " + StudyText.ScoresNote + " " + StudyText.AttributionNote, 8, false, TextInk, 0)
                }
            }));
            sp.Children[sp.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
            return sp;
        }

        // ------------------------------------------------------------- page 2 --

        /// <summary>The box fits the photograph (aspect ratio preserved, no stretching, no grey bars).</summary>
        private StackPanel ImageBox(string path, string missing, double height, string borderHex = null)
        {
            var host = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            var img = Ui.LoadImage(path, 0);
            UIElement content;
            double width = ImgW;
            if (img != null)
            {
                var image = new Image { Source = img, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                content = image;
                if (img.PixelHeight > 0) width = Math.Min(ImgW, height * img.PixelWidth / img.PixelHeight);
            }
            else content = new TextBlock { Text = missing, FontSize = 9, Foreground = C(Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
            host.Children.Add(new Border
            {
                Width = width,
                Height = height,
                Background = C(ImgBg),
                BorderBrush = C(borderHex ?? "#9CA3AF"),
                BorderThickness = new Thickness(borderHex == null ? 1 : 1.5),
                Child = content
            });
            return host;
        }

        private FrameworkElement OriginalCell(CaptureViewRow v, string viewName, double imgH)
        {
            var cell = new StackPanel { Width = ImgW };
            string path = v == null ? null : (!string.IsNullOrEmpty(v.OriginalImagePath) && System.IO.File.Exists(v.OriginalImagePath) ? v.OriginalImagePath : v.AnalysedImagePath);
            cell.Children.Add(ImageBox(path, v == null ? viewName + " view was not captured" : "image not available on disk", imgH));
            cell.Children.Add(T(viewName == "Single" ? "Captured image" : viewName + " View", 9.5, true, Navy, 1));
            if (_f.V6 != null)
            {
                // V6 flag on: no V5 predicted class or Model Class Score under the photograph, only the capture quality and the unified headline
                if (v != null && !string.IsNullOrEmpty(v.QualityStatus)) cell.Children.Add(T("Capture quality: " + v.QualityStatus, 8.5, false, v.QualityStatus == "Good" ? TextInk : WarnInk, 1));
                SkinDevApp.V6.V6ViewSummary vs = _f.V6.Views.FirstOrDefault(x => x.View == (v?.ViewType ?? viewName));
                cell.Children.Add(T(vs != null ? vs.Headline : "No AI result is stored for this image", 8.5, false, vs != null && vs.ReviewRequired ? WarnInk : TextInk, 0));
            }
            else if (v?.Scores != null)
            {
                string q = v.QualityStatus ?? "—";
                cell.Children.Add(T("Capture quality: " + q, 8.5, false, q == "Good" ? TextInk : WarnInk, 1));
                cell.Children.Add(T("Predicted class: " + Lbl(v.Scores.PredictedClass), 8.5, false, TextInk, 1));
                cell.Children.Add(T("Model Class Score: " + Pc(v.Scores.Confidence), 8.5, false, TextInk, 0));
            }
            else cell.Children.Add(T(v == null ? "Not captured" : "Not analyzed", 8.5, false, WarnInk, 0));
            return cell;
        }

        private FrameworkElement OriginalsBody()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Major Visual Findings", 17, true, Navy, 0));
            sp.Children.Add(T("Original captured frames, shown without any overlay at inspection size. Aspect ratio is preserved.", 8.5, false, Muted, 6));

            if (_f.ReviewRecommended)
            {
                var w = new StackPanel();
                w.Children.Add(T(_f.ViewDisagreement ? "View disagreement detected — review recommended" : "Review recommended", 9, true, WarnInk, 2));
                foreach (string flag in _f.Flags.Where(x => !x.StartsWith("View disagreement")).Take(3)) w.Children.Add(T("•  " + flag, 8, false, WarnInk, 1));
                sp.Children.Add(Panel(w, WarnBg, WarnLine, 7));
                sp.Children.Add(new Border { Height = 6 });
            }

            double imgH = _f.ReviewRecommended ? OriginalHWithBanner : OriginalH;
            var cells = new List<FrameworkElement>();
            if (_d.Session.ScanMode == "Single") cells.Add(OriginalCell(_d.Views.FirstOrDefault(), "Single", imgH));
            else foreach (string name in StudyText.Views) cells.Add(OriginalCell(_d.View(name), name, imgH));
            cells.Add(FindingsBox());

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            for (int i = 0; i < cells.Count; i++)
            {
                if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cells[i].Margin = new Thickness(0, 0, 0, 12);
                Grid.SetRow(cells[i], i / 2);
                Grid.SetColumn(cells[i], i % 2 == 0 ? 0 : 2);
                grid.Children.Add(cells[i]);
            }
            sp.Children.Add(grid);
            return sp;
        }

        // ------------------------------------------- candidate lesion localization --

        private FrameworkElement LocalizationCell(CaptureViewRow v, string viewName)
        {
            var cell = new StackPanel { Width = ImgW };
            LocalizationRunRow r = v == null ? null : v.Localization;
            bool ok = r != null && r.Status == "OK";
            cell.Children.Add(ImageBox(ok ? r.OverlayPath : null,
                v == null ? viewName + " view was not captured" : r == null ? "localization not recorded (older scan)" : ok ? "image not available on disk" : "localization " + (r.Status ?? "not run"),
                OriginalH, "#6B7280"));
            cell.Children.Add(T(viewName == "Single" ? "Captured image" : viewName + " View", 9.5, true, Navy, 1));
            if (ok)
            {
                if (v.Lesions.Count == 0) cell.Children.Add(T("No candidate boxes above the thresholds", 8.5, false, TextInk, 0));
                else cell.Children.Add(T(v.Lesions.Count + " candidate box" + (v.Lesions.Count == 1 ? "" : "es") + ": " +
                    string.Join(", ", v.Lesions.GroupBy(x => x.ClassName).Select(g => g.Key + " " + g.Count())), 8.5, false, TextInk, 0));
            }
            return cell;
        }

        private FrameworkElement LocalizationBody()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Candidate Lesion Localization", 17, true, Navy, 0));
            sp.Children.Add(T("Boxes proposed by a separate lesion detector on the same frames the classifier and Grad-CAM++ analyzed. Acne red, hyperpigmentation blue, eczema orange.", 8.5, false, Muted, 6));

            var cells = new List<FrameworkElement>();
            if (_d.Session.ScanMode == "Single") cells.Add(LocalizationCell(_d.Views.FirstOrDefault(), "Single"));
            else foreach (string name in StudyText.Views) cells.Add(LocalizationCell(_d.View(name), name));

            LocalizationRunRow any = _d.Views.Select(x => x.Localization).FirstOrDefault(x => x != null && x.Status == "OK");
            var note = new StackPanel();
            note.Children.Add(T("About these boxes", 10.5, true, Navy, 5));
            note.Children.Add(T("•  These are possible locations only: not an outline of a lesion, not a count and not a measure of severity.", 8.5, false, TextInk, 4));
            note.Children.Add(T("•  Lesions can be missed and a box can be wrong. Eczema findings are less established than acne and hyperpigmentation.", 8.5, false, TextInk, 4));
            note.Children.Add(T("•  Independent of Grad-CAM++ and of the class scores: neither is changed by the other.", 8.5, false, TextInk, 4));
            if (any != null)
            {
                note.Children.Add(T("Detector: " + any.ModelTag + ", " + any.ImageSize + " px" +
                                    (string.IsNullOrEmpty(any.ModelSha256) ? "" : ", SHA-256 " + any.ModelSha256.Substring(0, Math.Min(12, any.ModelSha256.Length)) + "…"), 8, false, Muted, 3));
                if (!string.IsNullOrEmpty(any.ThresholdsJson))
                    note.Children.Add(T("Confidence thresholds: " + any.ThresholdsJson.Replace("\"", "").Replace("{", "").Replace("}", "").Replace(":", " ").Replace(",", ", "), 8, false, Muted, 0));
            }
            var nb = Panel(note, PanelBg, null, 10);
            nb.Width = ImgW;
            nb.VerticalAlignment = VerticalAlignment.Top;
            nb.MinHeight = OriginalH;
            cells.Add(nb);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            for (int i = 0; i < cells.Count; i++)
            {
                if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cells[i].Margin = new Thickness(0, 0, 0, 12);
                Grid.SetRow(cells[i], i / 2);
                Grid.SetColumn(cells[i], i % 2 == 0 ? 0 : 2);
                grid.Children.Add(cells[i]);
            }
            sp.Children.Add(grid);
            return sp;
        }

        /// <summary>A full-width band under the marked-area photographs: the color key, then three short notes side by side.</summary>
        private FrameworkElement MarkedAreasNotes()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("How to read the marked areas", 13, true, Navy, 10));

            string[][] notes =
            {
                new[] { "Possible locations", "Boxes show where a lesion may be. They are not an outline, a count or a measure of severity." },
                new[] { "Lesions can be missed", "A box can be wrong. Eczema findings are less established than those for acne and hyperpigmentation." },
                new[] { "No box is not proof", "An image with no box does not prove that a condition is absent." }
            };
            var g = new Grid();
            for (int i = 0; i < 3; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (i < 2) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            }
            for (int i = 0; i < 3; i++)
            {
                var col = new StackPanel();
                col.Children.Add(T(notes[i][0], 11, true, Navy, 4));
                col.Children.Add(T(notes[i][1], 10, false, TextInk, 0));
                Grid.SetColumn(col, i * 2);
                g.Children.Add(col);
            }
            sp.Children.Add(g);
            var band = Panel(sp, PanelBg, null, 16);
            band.Margin = new Thickness(0, 6, 0, 0);
            return band;
        }

        /// <summary>Under each marked-areas photograph: one short line per condition that has marked areas (color key, condition, where), or "No areas marked".</summary>
        private FrameworkElement AreasCaption(SkinDevApp.V6.V6ViewSummary vs)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            var marked = vs.Clinical.Findings.Where(f => f.Regions.Count > 0 || f.BoxCount > 0).ToList();
            if (marked.Count == 0) { sp.Children.Add(T("No areas marked", 8.5, false, Muted, 0)); return sp; }
            foreach (SkinDevApp.V6.ClinicalFinding f in marked)
            {
                int idx = SkinDevApp.V6.V6Conditions.IndexOf(f.Condition);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var swatch = new Border { Width = 8, Height = 8, Background = C(idx >= 0 ? SkinDevApp.Imaging.ClassPalette.Hex[idx] : "#6B7280"), Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                var text = new StackPanel();
                text.Children.Add(T(f.Condition, 8.5, true, TextInk, 0));
                text.Children.Add(T(f.Regions.Count > 0 ? string.Join(", ", f.Regions) : "area marked", 8.5, false, TextInk, 0));
                Grid.SetColumn(text, 1);
                row.Children.Add(swatch); row.Children.Add(text);
                sp.Children.Add(row);
            }
            return sp;
        }

        private FrameworkElement V6MarkedAreasBody()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Marked Areas", 17, true, Navy, 0));
            sp.Children.Add(T("Places on each image where a lesion may be. Acne is marked red, hyperpigmentation blue and eczema orange.", 8.5, false, Muted, 6));

            // three photographs go side by side in one row (smaller), so the notes below always fit on the page
            bool three = _f.V6.Views.Count >= 3;
            double cw = three ? 166 : ImgW, ch = three ? 166 : OriginalH;
            var cells = new List<FrameworkElement>();
            foreach (SkinDevApp.V6.V6ViewSummary vs in _f.V6.Views)
            {
                CaptureViewRow v = _d.View(vs.View) ?? _d.Views.FirstOrDefault();
                var cell = new StackPanel { Width = cw };
                cell.Children.Add(ImageBox(SkinDevApp.V6.V6ResultsView.MarkedAreasPath(v), "marked-areas image not available", ch, "#6B7280"));
                cell.Children.Add(T(vs.View == "Single" ? "Captured image" : vs.Label, 9.5, true, Navy, 1));
                cell.Children.Add(AreasCaption(vs));
                cells.Add(cell);
            }

            var grid = new Grid();
            if (three)
            {
                for (int i = 0; i < 3; i++)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cw) });
                    if (i < 2) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < Math.Min(3, cells.Count); i++)
                {
                    cells[i].Margin = new Thickness(0, 0, 0, 12);
                    Grid.SetColumn(cells[i], i * 2);
                    grid.Children.Add(cells[i]);
                }
                sp.Children.Add(grid);
                sp.Children.Add(MarkedAreasNotes());
                return sp;
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            for (int i = 0; i < cells.Count; i++)
            {
                if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cells[i].Margin = new Thickness(0, 0, 0, 12);
                Grid.SetRow(cells[i], i / 2);
                Grid.SetColumn(cells[i], i % 2 == 0 ? 0 : 2);
                grid.Children.Add(cells[i]);
            }
            sp.Children.Add(grid);
            sp.Children.Add(MarkedAreasNotes());
            return sp;
        }

        private FrameworkElement FindingsBox()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Major Findings", 10.5, true, Navy, 6));
            if (_f.V6 != null)
            {
                // structured: the outcome, the review status, then one short block per photograph (detected / for review)
                SkinDevApp.V6.ClinicalSummary cs = _f.V6.Clinical;
                sp.Children.Add(T(cs.Title, 10, true, cs.Category == SkinDevApp.V6.ClinicalCategory.NoConditionDetected ? TextInk : WarnInk, 2));
                sp.Children.Add(T("Review status: " + _f.V6.ReviewStatus, 8.5, false, Muted, 8));
                foreach (SkinDevApp.V6.V6ViewSummary v in _f.V6.Views)
                {
                    sp.Children.Add(new Border { Height = 1, Background = C(RuleGray), Margin = new Thickness(0, 0, 0, 5) });
                    sp.Children.Add(T(v.View == "Single" ? "Image" : v.Label, 9, true, Navy, 2));
                    SkinDevApp.V6.ClinicalSummary c = v.Clinical;
                    if (c.Category == SkinDevApp.V6.ClinicalCategory.Retake)
                    {
                        sp.Children.Add(T("Not clear enough to interpret. Please retake the image.", 8.5, false, TextInk, 6));
                        continue;
                    }
                    var det = c.Findings.Where(x => x.IsDetected)
                        .Select(x => x.Condition + " (" + x.Certainty.ToLowerInvariant() + " certainty" + (x.Regions.Count > 0 ? "; " + string.Join(", ", x.Regions) : "") + ")").ToList();
                    var rev = c.Findings.Where(x => x.Status == SkinDevApp.V6.ClinicalSummary.Possible || x.Status == SkinDevApp.V6.ClinicalSummary.Inconclusive)
                        .Select(x => x.Condition + (x.Regions.Count > 0 ? " (" + string.Join(", ", x.Regions) + ")" : "")).ToList();
                    sp.Children.Add(LabelledLine("Detected", det.Count > 0 ? string.Join("; ", det) : "None", det.Count > 0 ? TextInk : Muted));
                    if (rev.Count > 0) sp.Children.Add(LabelledLine("For review", string.Join("; ", rev), WarnInk));
                    sp.Children.Add(new Border { Height = 5 });
                }
                if (!_f.V6.Single && _f.Lines.Any(l => l.StartsWith("The images do not all show")))
                    sp.Children.Add(T("The findings differ between images; each is listed separately.", 8, false, Muted, 4));
                sp.Children.Add(T("A dermatologist must confirm this result.", 8, false, Muted, 0));
            }
            else
            {
                foreach (string line in _f.Lines)
                    sp.Children.Add(T("•  " + line, 8.5, false, TextInk, 4));
            }
            var b = Panel(sp, PanelBg, null, 10);
            b.Width = ImgW;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.MinHeight = _f.ReviewRecommended ? OriginalHWithBanner : OriginalH;
            return b;
        }

        /// <summary>"Detected   Acne (moderate certainty; nose)" - a small label column and a wrapping value.</summary>
        private FrameworkElement LabelledLine(string label, string value, string valueColor)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = T(label, 8, true, Muted, 0);
            var v = T(value, 8.5, false, valueColor, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v);
            return g;
        }

        // ------------------------------------------------------- page 3 onward --

        private List<FrameworkElement> EvidenceBodies()
        {
            var pages = new List<FrameworkElement>();
            List<EvidenceItem> items = _f.Evidence;

            if (items.Count == 0 && _f.V6 != null) return pages;          // the final model's report shows its marked-areas page instead of evidence-map pages

            if (items.Count == 0)
            {
                var sp = new StackPanel();
                sp.Children.Add(T("Selected AI Attribution Evidence", 17, true, Navy, 0));
                sp.Children.Add(T("Grad-CAM++ attribution maps were not available for this scan" +
                                  (_f.ViewsWithoutMaps.Count > 0 ? " (" + string.Join(", ", _f.ViewsWithoutMaps) + ")" : "") +
                                  ", for example because the Grad-CAM++ service was not running at capture time. The class scores and original images above are unaffected.",
                                  9, false, TextInk, 8));
                sp.Children.Add(T(RetentionNote(), 8, false, Muted, 0));
                pages.Add(sp);
                return pages;
            }

            int perPage = 2;
            int pageCount = (items.Count + perPage - 1) / perPage;
            for (int p = 0; p < pageCount; p++)
            {
                var sp = new StackPanel();
                sp.Children.Add(T((_f.V6 != null ? "Detector Evidence Maps" : "Selected AI Attribution Evidence") + (pageCount > 1 ? "  (" + (p + 1) + " of " + pageCount + ")" : ""), 17, true, Navy, 0));
                if (_f.V6 != null)
                {
                    sp.Children.Add(T("One map per image, for the condition the unified assessment ranks highest: where the lesion detectors found evidence (the same evidence the assessment uses). Color = the condition, strength = detector confidence.", 8.5, false, Muted, 4));
                    sp.Children.Add(T("This is an evidence map, not an explanation of the model, and not a lesion segmentation. Lesions can be missed and boxes can be wrong.", 8, false, Muted, 8));
                }
                else
                {
                sp.Children.Add(T("Only the maps relevant to understanding the AI result are printed. " + StudyText.AttributionNote.Replace("Grad-CAM++ maps show model attribution (which image areas influenced each class score). They are not lesion segmentation.", ""),
                                  8.5, false, Muted, 4));
                sp.Children.Add(T("Grad-CAM++ indicates image regions that influenced the model's class score. It is not lesion segmentation and should not be interpreted as a map of diseased tissue.",
                                  8, false, Muted, 8));
                }

                List<EvidenceItem> onPage = items.Skip(p * perPage).Take(perPage).ToList();
                foreach (EvidenceItem it in onPage)
                    sp.Children.Add(EvidenceBlock(it, onPage.Count == 1 ? EvidenceH1 : EvidenceH2));

                if (p == pageCount - 1)
                {
                    if (_f.Omitted > 0)
                        sp.Children.Add(T(_f.Omitted + " further map(s) met the selection rules but are not printed.", 8, false, Muted, 2));
                    sp.Children.Add(T(RetentionNote(), 8, false, Muted, 0));
                }
                pages.Add(sp);
            }
            return pages;
        }

        private string RetentionNote() =>
            "Complete class-specific attribution maps (all four classes, every view) are retained in the electronic scan record under ScanSessionID: " + _d.Session.DisplayId + ".";

        private static string Attribution(AttributionMapRow m)
        {
            string kind = m.Diffuse ? "diffuse (spread across the face, no focal area)"
                        : (string.IsNullOrEmpty(m.TopZone) ? "focal" : "focal, strongest near the " + m.TopZone);
            if (m.RelativeStrength < 0.5) kind += "; weak relative to the other classes";
            return kind;
        }

        private FrameworkElement EvidenceBlock(EvidenceItem it, double imgH)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var swatch = new Border { Width = 9, Height = 9, Background = C(Ui.ClassColor(it.ClassName)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(1) };
            DockPanel.SetDock(swatch, Dock.Left);
            title.Children.Add(swatch);
            title.Children.Add(T(it.View.ViewType + " view  —  " + Lbl(it.ClassName) + (_f.V6 != null ? " detector evidence" : " attribution"), 10, true, Navy, 0));
            sp.Children.Add(title);
            sp.Children.Add(T(it.Reason, 8.5, false, it.IsPrimary ? Muted : WarnInk, 4));

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ImgW) });

            var left = new StackPanel();
            string original = !string.IsNullOrEmpty(it.View.AnalysedImagePath) && System.IO.File.Exists(it.View.AnalysedImagePath) ? it.View.AnalysedImagePath : it.View.OriginalImagePath;
            left.Children.Add(ImageBox(original, "image not available", imgH));
            left.Children.Add(T("Original image", 8.5, true, TextInk, 0));
            left.Children[left.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
            left.Children.Add(T("View: " + it.View.ViewType + (_f.V6 != null || it.View.Scores == null ? "" : "  ·  Predicted class: " + Lbl(it.View.Scores.PredictedClass)), 8, false, Muted, 0));
            row.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(ImageBox(it.Map.OverlayPath, _f.V6 != null ? "evidence map not available" : "Grad-CAM++ image not available", imgH, Ui.ClassColor(it.ClassName)));
            var cap = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
            if (_f.V6 != null)
            {
                cap.Children.Add(T("Detector evidence map", 8.5, true, TextInk, 0));
                cap.Children.Add(T("Target condition: " + Lbl(it.ClassName) + "   ·   Highest detector confidence: " + Pc(it.Score) + "   ·   View: " + it.View.ViewType, 8, false, TextInk, 0));
                cap.Children.Add(T("Color = the condition, strength = detector confidence (full strength from 0.70). Boxes centered outside the face region are not drawn.", 8, false, Muted, 0));
            }
            else
            {
            cap.Children.Add(T("Grad-CAM++ attribution", 8.5, true, TextInk, 0));
            cap.Children.Add(T("Target class: " + Lbl(it.ClassName) + "   ·   " + (_f.V6 != null ? "V5 class score: " : "Model Class Score: ") + Pc(it.Score) + "   ·   View: " + it.View.ViewType, 8, false, TextInk, 0));
            cap.Children.Add(T("Attribution: " + Attribution(it.Map) + "  (relative strength " + it.Map.RelativeStrength.ToString("0.00", CultureInfo.InvariantCulture) + ", compares the four classes in this frame; not a probability)", 7.5, false, Muted, 0));
            }
            right.Children.Add(cap);
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
            sp.Children.Add(row);
            return sp;
        }

        // ------------------------------------------------- printable forms --

        private static readonly string[] FormLabels =
            { "Acne", "Eczema", "Hyperpigmentation", "Normal", "Other / uncertain", "Unusable image" };
        private static readonly string[] FormAgreement = { "Agree", "Partially agree", "Disagree", "Uncertain" };

        private static TextBlock FormLabel(string text) => T(text, 8.5, true, Navy, 3);

        /// <summary>Wrapping row of "box + option" entries.</summary>
        private static WrapPanel Boxes(IEnumerable<string> options, ISet<string> ticked = null)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (string o in options)
            {
                bool on = ticked != null && ticked.Contains(o);
                var t = T((on ? "☑  " : "☐  ") + o, 9, on, on ? Navy : TextInk, 0);
                t.Margin = new Thickness(0, 0, 18, 4);
                w.Children.Add(t);
            }
            return w;
        }

        /// <summary>Caption + empty writing area with a bottom rule (1 line = 22 px).</summary>
        private static StackPanel WriteArea(string caption, int lines)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(FormLabel(caption));
            for (int i = 0; i < lines; i++)
                sp.Children.Add(new Border { Height = 22, BorderBrush = C("#9CA3AF"), BorderThickness = new Thickness(0, 0, 0, 1) });
            return sp;
        }

        /// <summary>Side-by-side fields with relative widths (a long signature line and a short date line); value == null leaves the line blank for handwriting.</summary>
        private static Grid FieldRowWeighted(double[] weights, params Tuple<string, string>[] fields)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < fields.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weights[i], GridUnitType.Star) });
                var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 18, 0, 0, 0) };
                cell.Children.Add(FormLabel(fields[i].Item1));
                cell.Children.Add(new Border { Height = 26, BorderBrush = C("#9CA3AF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = fields[i].Item2 == null ? null : T(fields[i].Item2, 9, true, TextInk, 0) });
                Grid.SetColumn(cell, i);
                g.Children.Add(cell);
            }
            return g;
        }

        /// <summary>Two or three side-by-side fields; value == null leaves the line blank for handwriting.</summary>
        private static Grid FieldRow(params Tuple<string, string>[] fields)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < fields.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0) };
                cell.Children.Add(FormLabel(fields[i].Item1));
                cell.Children.Add(new Border
                {
                    Height = 22,
                    BorderBrush = C("#9CA3AF"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = fields[i].Item2 == null ? null : T(fields[i].Item2, 9, true, TextInk, 0)
                });
                Grid.SetColumn(cell, i);
                g.Children.Add(cell);
            }
            return g;
        }

        private FrameworkElement FormBody(bool dermatologist)
        {
            ScanSessionRow s = _d.Session;
            var sp = new StackPanel();
            // the dermatologist form shows what was saved in the review window; with no saved review it stays a blank form
            DermatologistValidation dv = dermatologist ? (_d.Validations.FirstOrDefault(x => x.ValidationStatus != ReviewStatus.ValidationDraft) ?? _d.Validations.FirstOrDefault()) : null;
            sp.Children.Add(T(dermatologist ? "Dermatologist Review" : "Reference Assessment", 17, true, Navy, 0));
            sp.Children.Add(T(dv != null ? "Recorded in LUMYVUE by the reviewing dermatologist · " + Fmt.When(dv.ValidationDate)
                                         : dermatologist ? "Printed form — to be completed by hand" : "Printed form — to be completed by hand and signed", 8.5, false, Muted, 6));

            sp.Children.Add(Table(new[]
            {
                Ui.KV("Patient ID", FirstUnlinked(s)),
                Ui.KV("Scan", s.DisplayId),
                Ui.KV("Date and time of scan", Fmt.When(s.StartedAt)),
                Ui.KV("AI result (for reference)", _f.V6 != null ? _f.V6.Headline : (s.OverallPredictedClass == null ? "no usable result" : Lbl(s.OverallPredictedClass)) + (s.OverallScore.HasValue ? "  (" + s.ScoreText + ")" : ""))
            }, 130, 9));
            sp.Children.Add(Rule(4, 6));
            sp.Children.Add(T(dermatologist
                ? "This review is recorded separately and never replaces the AI result."
                : "Select the reference label or labels you assign to this scan. More than one label may be selected if permitted.",
                8, false, Muted, 8));

            if (dermatologist)
            {
                sp.Children.Add(FieldRow(Tuple.Create("Reviewer ID *", dv == null ? null : dv.DermatologistID),
                                         Tuple.Create("Reviewer name (where permitted)", dv == null ? null : dv.ValidatorName)));
                sp.Children.Add(FieldRow(Tuple.Create("Professional role", "Dermatologist"),
                                         Tuple.Create("Registration number (optional)", dv == null ? null : dv.CredentialReference)));
            }

            // ticks come only from the saved answers: a condition is ticked when it was Confirmed; "Normal" when every condition was Not Confirmed;
            // "Other / uncertain" when any condition still needs further assessment
            ISet<string> labelTicks = null, agreeTicks = null;
            if (dv != null)
            {
                var verdicts = StudyRepository.LatestConditionReviews(_d.Session.ScanSessionID);
                // the dermatologist's own ticked labels (saved in the review window); reviews saved before that question existed fall back to the condition verdicts
                labelTicks = new HashSet<string>((dv.DermatologistAssessment ?? "").Split(',').Select(x => x.Trim()).Where(x => FormLabels.Contains(x, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
                if (labelTicks.Count == 0)
                {
                    labelTicks = new HashSet<string>(verdicts.Where(kv => kv.Value.Status == ConditionReview.Confirmed).Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
                    if (verdicts.Count > 0 && verdicts.All(kv => kv.Value.Status == ConditionReview.NotConfirmed)) labelTicks.Add("Normal");
                    if (verdicts.Any(kv => kv.Value.Status == ConditionReview.FurtherAssessment || kv.Value.Status == ConditionReview.Pending)) labelTicks.Add("Other / uncertain");
                }
                agreeTicks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { string.IsNullOrWhiteSpace(dv.AgreementWithAI) ? "Uncertain" : dv.AgreementWithAI };
            }

            sp.Children.Add(FormLabel((dermatologist ? "Dermatologist assessment" : "Reference assessment") + " *"));
            sp.Children.Add(Boxes(FormLabels, labelTicks));

            sp.Children.Add(FormLabel("Agreement with the AI result *"));
            sp.Children.Add(Boxes(FormAgreement, agreeTicks));

            if (dv != null && !string.IsNullOrWhiteSpace(dv.Notes))
            {
                var notes = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                notes.Children.Add(FormLabel("Dermatologist notes"));
                var nt = T(dv.Notes, 9, false, TextInk, 0); nt.TextWrapping = TextWrapping.Wrap; nt.Margin = new Thickness(0, 2, 0, 6);
                notes.Children.Add(nt);
                notes.Children.Add(new Border { Height = 1, Background = C("#9CA3AF") });
                sp.Children.Add(notes);
            }
            else sp.Children.Add(WriteArea(dermatologist ? "Dermatologist notes" : "Notes", dermatologist ? (dv != null ? 2 : 8) : 5));

            if (!dermatologist)
            {
                sp.Children.Add(FormLabel("Comments for each view (if any)"));
                sp.Children.Add(FieldRow(Tuple.Create("Front", (string)null), Tuple.Create("Left", (string)null), Tuple.Create("Right", (string)null)));
                sp.Children.Add(FieldRow(Tuple.Create("Reviewer ID *", (string)null)));
            }
            else
            {
                sp.Children.Add(FormLabel("Review status *"));
                sp.Children.Add(Boxes(new[] { "Completed", "Pending" }, dv == null ? null :
                    new HashSet<string> { string.Equals(dv.ValidationStatus, ReviewStatus.ValidationCompleted, StringComparison.OrdinalIgnoreCase) ? "Completed" : "Pending" }));
            }

            // signature block (research team only: the dermatologist form has no signature or date line)
            if (!dermatologist)
            {
                sp.Children.Add(Rule(4, 6));
                // every member of the research team signs
                sp.Children.Add(T("Signed by the review team (all members)", 8.5, true, Navy, 0));
                foreach (string name in ResearchTeamMembers)
                {
                    sp.Children.Add(FieldRow(
                        Tuple.Create("Signature", (string)null),
                        Tuple.Create("Printed name", name),
                        Tuple.Create("Date", (string)null)));
                    sp.Children[sp.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 20, 0, 4));
                }
            }

            return sp;
        }

        // ------------------------------------------------------------ export --

        /// <summary>A4 document whose pages are real content (text + full-resolution photographs), not a flat picture.</summary>
        internal FixedDocument BuildDocument(out Size pageSize)
        {
            const double ph = 1122.5;                   // A4 at 96 dpi
            double scale = ph / PageH;
            double pw = PageW * scale;
            pageSize = new Size(pw, ph);

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = pageSize;
            foreach (FrameworkElement page in BuildPages())
            {
                page.RenderTransform = new ScaleTransform(scale, scale);
                var fixedPage = new FixedPage { Width = pw, Height = ph, Background = Brushes.White };
                fixedPage.Children.Add(page);
                var content = new PageContent();
                ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
                doc.Pages.Add(content);
            }
            return doc;
        }

        /// <summary>Saves the A4 report as a PDF in the Reports folder (no dialog).</summary>
        private void SaveToReportsFolder()
        {
            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                string path = ReportPdf.Save(BuildPages(), PageW, PageH, _d.Session.DisplayId);
                StudyRepository.Audit("Report saved", "ScanSession", _d.Session.ScanSessionID, System.IO.Path.GetFileName(path));
                Toast.Show("Report saved to the Reports folder");
            }
            catch (Exception ex)
            {
                LumyDialog.Show("The report could not be saved: " + ex.Message, "LUMYVUE Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; }
        }

        private void Export()
        {
            try
            {
                var dlg = new PrintDialog();
                try
                {
                    PrintQueue pdf = new LocalPrintServer().GetPrintQueues().Cast<PrintQueue>()
                        .FirstOrDefault(q => q.Name.Contains("Print to PDF"));
                    if (pdf != null) dlg.PrintQueue = pdf;
                }
                catch { }
                if (dlg.ShowDialog() != true) return;
                dlg.PrintTicket.PageOrientation = PageOrientation.Portrait;

                Size size;
                FixedDocument doc = BuildDocument(out size);

                // Job name = default PDF file name: IDs only, never the participant's name.
                dlg.PrintDocument(doc.DocumentPaginator, "LUMYVUE_Research_Report_" + _d.Session.DisplayId);
                StudyRepository.Audit("Report generated", "ScanSession", _d.Session.ScanSessionID, dlg.PrintQueue != null ? dlg.PrintQueue.Name : "");
            }
            catch (Exception ex)
            {
                LumyDialog.Show("Could not print / save the report: " + ex.Message, "LUMYVUE Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
