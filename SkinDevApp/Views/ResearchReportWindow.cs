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
        private const string Navy = "#1F2A44", TextInk = "#1F2937", Muted = "#6B7280", RuleGray = "#D1D5DB",
                             PanelBg = "#F3F4F6", ImgBg = "#E5E7EB", WarnInk = "#92400E", WarnBg = "#FFF7ED", WarnLine = "#F5C99A";

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
                    MessageBox.Show("Scan " + scanSessionId + " is not in the database yet.", "LUMYVUE Report");
                    return;
                }
                new ResearchReportWindow(d) { Owner = Application.Current.MainWindow }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not build the report: " + ex.Message, "LUMYVUE Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ResearchReportWindow(SessionDetail d)
        {
            _d = d;
            _f = ReportFindings.Build(d);
            Title = "PrecisionSkin Research Analysis Report — " + d.Session.DisplayId;
            Width = 720;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Brush("#E5E7EB");

            var pagesPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
            foreach (FrameworkElement p in BuildPages()) { p.Margin = new Thickness(0, 0, 0, 16); pagesPanel.Children.Add(p); }

            var bar = new DockPanel { Margin = new Thickness(12), LastChildFill = false };
            _pdfBtn = Ui.Btn("Save as PDF / Print", (s, e) => Export(), "PrimaryButton", 220);
            DockPanel.SetDock(_pdfBtn, Dock.Right);
            bar.Children.Add(_pdfBtn);
            Button close = Ui.Btn("Close", (s, e) => Close(), "SecondaryButton", 110);
            close.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
            bar.Children.Add(Ui.Text("Choose \"Microsoft Print to PDF\" to save a PDF file.", 11.5, false, Ui.Muted, new Thickness(0, 12, 0, 0)));

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);
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
            s.ParticipantID ?? "unlinked" + (string.IsNullOrEmpty(s.LegacyPatientLabel) ? "" : " (" + s.LegacyPatientLabel + ")");

        // -------------------------------------------------------- page frame --

        private List<FrameworkElement> BuildPages()
        {
            var bodies = new List<FrameworkElement>
            {
                SummaryBody(),
                OriginalsBody()
            };
            bodies.AddRange(EvidenceBodies());
            bodies.Add(FormBody(true));      // licensed dermatologist first
            bodies.Add(FormBody(false));     // then the research team

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
            var right = T("Research Analysis Report", 9, false, Muted, 0);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);
            top.Children.Add(T("PrecisionSkin / LUMYVUE", 15, true, Navy, 0));
            head.Children.Add(top);
            head.Children.Add(T("Researchers: " + ResearchTeam, 7.5, false, Muted, 3));
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
            line.Children.Add(T("PatientID " + FirstUnlinked(_d.Session) + "   ·   ScanSessionID " + _d.Session.DisplayId +
                                "   ·   Research prototype — not a diagnostic report", 8, false, Muted, 0));
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

            sp.Children.Add(T("Research Analysis Report", 21, true, Navy, 0));
            sp.Children.Add(T("Multi-view AI skin-image classification  ·  generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 9, false, Muted, 8));

            // identification
            var two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string completeness = s.ScanMode == "Single" ? "Single capture"
                : string.Join("  ", StudyText.Views.Select(v => (_d.View(v)?.Scores != null ? "✓ " : "✗ ") + v)) + "   (" + s.CompletionText.Replace(" / ", " of ") + ")";
            var left = new StackPanel();
            left.Children.Add(Heading("Participant and scan", 0));
            left.Children.Add(Table(new[]
            {
                Ui.KV("PatientID", FirstUnlinked(s)),
                Ui.KV("ScanSessionID", s.DisplayId),
                Ui.KV("Scan date / time", s.StartedAt),
                Ui.KV("Scan completeness", completeness),
                Ui.KV("Scan status", s.ScanStatus)
            }, 104));
            two.Children.Add(left);

            string layer = _d.Views.SelectMany(v => v.Maps).Select(x => x.TargetLayer).FirstOrDefault(x => !string.IsNullOrEmpty(x));
            var right = new StackPanel();
            right.Children.Add(Heading("Model", 0));
            right.Children.Add(Table(new[]
            {
                Ui.KV("Model name", m == null ? "unknown" : m.ModelName + " (" + m.Architecture + ")"),
                Ui.KV("Model version", m == null ? "unknown" : "run " + m.RunName),
                Ui.KV("Model hash", m == null ? "unknown" : ModelVersion.Short(m.OnnxSha256) + "…  (ONNX SHA-256)"),
                Ui.KV("Grad-CAM++ service", m == null ? null : "v" + (m.ServiceVersion ?? "?") + " · " + ModelVersion.Short(m.ServiceSha256)),
                Ui.KV("Target layer", layer)
            }, 104));
            Grid.SetColumn(right, 2);
            two.Children.Add(right);
            sp.Children.Add(two);
            sp.Children.Add(Rule(10, 0));

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
                    if (v.Scores == null) { Row(false, v.ViewType, "not analysed", "", "", "", "", "", v.QualityStatus ?? ""); continue; }
                    double[] a = v.Scores.AsArray();
                    Row(false, v.ViewType, Lbl(v.Scores.PredictedClass), Pc(v.Scores.Confidence), Pc(a[0]), Pc(a[2]), Pc(a[1]), Pc(a[3]), v.QualityStatus ?? "");
                }
                sp.Children.Add(g);
            }

            // status
            sp.Children.Add(Heading("Consistency and review status"));
            if (s.ScanMode != "Single")
            {
                if (_f.ReviewRecommended)
                {
                    var w = new StackPanel();
                    foreach (string flag in _f.Flags) w.Children.Add(T("•  " + flag, 8.5, flag.StartsWith("View disagreement"), WarnInk, 2));
                    sp.Children.Add(Panel(w, WarnBg, WarnLine, 8));
                }
                else sp.Children.Add(T("Views consistent: every captured angle has the same primary class; no review flags were raised.", 8.5, false, TextInk, 2));
                sp.Children.Add(T("Fusion method: quality-weighted pooling of per-view scores. Heuristic; its validation is limited without paired multi-view labelled data.", 7.5, false, Muted, 2));
            }
            else if (_f.ReviewRecommended)
            {
                var w = new StackPanel();
                foreach (string flag in _f.Flags) w.Children.Add(T("•  " + flag, 8.5, false, WarnInk, 2));
                sp.Children.Add(Panel(w, WarnBg, WarnLine, 8));
            }

            ResearcherEvaluation ev = _d.Evaluations.FirstOrDefault();
            DermatologistValidation dv = _d.Validations.FirstOrDefault(x => x.ValidationStatus == "Completed") ?? _d.Validations.FirstOrDefault();
            SkinProfile skin = string.IsNullOrEmpty(s.ParticipantID) ? null : StudyRepository.GetSkinProfile(s.ParticipantID);
            sp.Children.Add(Heading("Human assessment and skin-tone module"));
            sp.Children.Add(Table(new[]
            {
                Ui.KV("Researcher assessment", ev == null ? "Pending" : ev.ResearcherClassification + " · " + ev.AgreementWithAI + " · " + ev.ResearcherID + " · " + ev.EvaluationDate),
                Ui.KV("Dermatologist validation", dv == null ? "Pending" : (dv.DermatologistAssessment ?? "—") + " · " + dv.ValidationStatus + " · " + dv.DermatologistID + " · " + dv.ValidationDate),
                Ui.KV("Fitzpatrick / skin-tone AI", "Not available in this model version"),
                Ui.KV("Fitzpatrick (manual)", skin == null || string.IsNullOrWhiteSpace(skin.FitzpatrickManual) || skin.FitzpatrickManual == "Not collected"
                    ? "Not collected" : skin.FitzpatrickManual + " (" + skin.FitzpatrickSource + ")")
            }, 150));

            // disclaimer + full hash
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(Panel(new StackPanel
            {
                Children =
                {
                    T("Research prototype disclaimer", 8.5, true, Navy, 2),
                    T(StudyText.PrototypeNotice + " " + StudyText.ScoresNote + " " + StudyText.AttributionNote, 8, false, TextInk, 0)
                }
            }));
            if (m != null && !string.IsNullOrEmpty(m.OnnxSha256))
                sp.Children.Add(T("Model identity (full): ONNX SHA-256 " + m.OnnxSha256, 6.5, false, Muted, 0));
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
            if (v?.Scores != null)
            {
                string q = v.QualityStatus ?? "—";
                cell.Children.Add(T("Capture quality: " + q, 8.5, false, q == "Good" ? TextInk : WarnInk, 1));
                cell.Children.Add(T("Predicted class: " + Lbl(v.Scores.PredictedClass), 8.5, false, TextInk, 1));
                cell.Children.Add(T("Model Class Score: " + Pc(v.Scores.Confidence), 8.5, false, TextInk, 0));
            }
            else cell.Children.Add(T(v == null ? "Not captured" : "Not analysed", 8.5, false, WarnInk, 0));
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

        private FrameworkElement FindingsBox()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("Major Findings", 10.5, true, Navy, 5));
            foreach (string line in _f.Lines)
                sp.Children.Add(T("•  " + line, 8.5, false, TextInk, 4));
            var b = Panel(sp, PanelBg, null, 10);
            b.Width = ImgW;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.MinHeight = _f.ReviewRecommended ? OriginalHWithBanner : OriginalH;
            return b;
        }

        // ------------------------------------------------------- page 3 onward --

        private List<FrameworkElement> EvidenceBodies()
        {
            var pages = new List<FrameworkElement>();
            List<EvidenceItem> items = _f.Evidence;

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
                sp.Children.Add(T("Selected AI Attribution Evidence" + (pageCount > 1 ? "  (" + (p + 1) + " of " + pageCount + ")" : ""), 17, true, Navy, 0));
                sp.Children.Add(T("Only the maps relevant to understanding the AI result are printed. " + StudyText.AttributionNote.Replace("Grad-CAM++ maps show model attribution (which image areas influenced each class score). They are not lesion segmentation.", ""),
                                  8.5, false, Muted, 4));
                sp.Children.Add(T("Grad-CAM++ indicates image regions that influenced the model's class score. It is not lesion segmentation and should not be interpreted as a map of diseased tissue.",
                                  8, false, Muted, 8));

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
            title.Children.Add(T(it.View.ViewType + " view  —  " + Lbl(it.ClassName) + " attribution", 10, true, Navy, 0));
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
            left.Children.Add(T("View: " + it.View.ViewType + "  ·  Predicted class: " + Lbl(it.View.Scores.PredictedClass), 8, false, Muted, 0));
            row.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(ImageBox(it.Map.OverlayPath, "Grad-CAM++ image not available", imgH, Ui.ClassColor(it.ClassName)));
            var cap = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
            cap.Children.Add(T("Grad-CAM++ attribution", 8.5, true, TextInk, 0));
            cap.Children.Add(T("Target class: " + Lbl(it.ClassName) + "   ·   Model Class Score: " + Pc(it.Score) + "   ·   View: " + it.View.ViewType, 8, false, TextInk, 0));
            cap.Children.Add(T("Attribution: " + Attribution(it.Map) + "  (relative strength " + it.Map.RelativeStrength.ToString("0.00", CultureInfo.InvariantCulture) + ", compares the four classes in this frame; not a probability)", 7.5, false, Muted, 0));
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
        private static WrapPanel Boxes(IEnumerable<string> options)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (string o in options)
            {
                var t = T("☐  " + o, 9, false, TextInk, 0);
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
            sp.Children.Add(T(dermatologist ? "Licensed Dermatologist Validation" : "Researcher Reference Assessment", 17, true, Navy, 0));
            sp.Children.Add(T("Printed form — to be completed by hand and signed", 8.5, false, Muted, 6));

            sp.Children.Add(Table(new[]
            {
                Ui.KV("PatientID", FirstUnlinked(s)),
                Ui.KV("ScanSessionID", s.DisplayId),
                Ui.KV("Date / time of scan", s.StartedAt),
                Ui.KV("AI result (for reference)", (s.OverallPredictedClass == null ? "no usable result" : Lbl(s.OverallPredictedClass)) + (s.OverallScore.HasValue ? "  (" + s.ScoreText + ")" : ""))
            }, 130, 9));
            sp.Children.Add(Rule(4, 6));
            sp.Children.Add(T(dermatologist
                ? "A separate validation layer. It does not replace the researcher assessment and never overwrites the AI prediction."
                : "Select the reference label(s) you assign to this scan. More than one label may be selected if the protocol allows it.",
                8, false, Muted, 8));

            if (dermatologist)
            {
                sp.Children.Add(FieldRow(Tuple.Create("Validator ID *", (string)null),
                                         Tuple.Create("Validator Name (where permitted by protocol)", (string)null)));
                sp.Children.Add(FieldRow(Tuple.Create("Professional Role", "Licensed Dermatologist"),
                                         Tuple.Create("License / Credential No. (if required by protocol)", (string)null)));
            }

            sp.Children.Add(FormLabel((dermatologist ? "Dermatologist reference assessment" : "Reference assessment") + " *"));
            sp.Children.Add(Boxes(FormLabels));

            sp.Children.Add(FormLabel("Agreement with AI *"));
            sp.Children.Add(Boxes(FormAgreement));

            if (dermatologist)
            {
                sp.Children.Add(FormLabel("Agreement with Researcher (if applicable)"));
                sp.Children.Add(Boxes(FormAgreement.Concat(new[] { "No researcher assessment yet" })));
            }

            sp.Children.Add(WriteArea(dermatologist ? "Dermatologist notes" : "Researcher notes", dermatologist ? 8 : 5));

            if (!dermatologist)
            {
                sp.Children.Add(FormLabel("Per-view Comments (if any)"));
                sp.Children.Add(FieldRow(Tuple.Create("Front", (string)null), Tuple.Create("Left", (string)null), Tuple.Create("Right", (string)null)));
                sp.Children.Add(FieldRow(Tuple.Create("Researcher ID *", (string)null)));
            }
            else
            {
                sp.Children.Add(FormLabel("Validation status *"));
                sp.Children.Add(Boxes(new[] { "Completed", "Pending" }));
            }

            // signature block
            sp.Children.Add(Rule(4, 6));
            if (dermatologist)
            {
                sp.Children.Add(FieldRow(
                    Tuple.Create("Signature of licensed dermatologist", (string)null),
                    Tuple.Create("Printed name", (string)null),
                    Tuple.Create("Date", (string)null)));
                sp.Children[sp.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 22, 0, 8));
            }
            else
            {
                // every member of the research team signs
                sp.Children.Add(T("Signed by the research team (all members)", 8.5, true, Navy, 0));
                foreach (string name in ResearchTeamMembers)
                {
                    sp.Children.Add(FieldRow(
                        Tuple.Create("Signature", (string)null),
                        Tuple.Create("Printed name", name),
                        Tuple.Create("Date", (string)null)));
                    sp.Children[sp.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 20, 0, 4));
                }
            }

            sp.Children.Add(T("After completion, the research team enters these answers in the system (Researcher Verification window, tab \"" +
                              (dermatologist ? "Licensed Dermatologist Validation" : "Researcher Assessment") +
                              "\"). Keep the signed sheet with the study records. Human assessments are saved separately and never overwrite the AI prediction.",
                              7.5, false, Muted, 0));
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
                dlg.PrintDocument(doc.DocumentPaginator, "PrecisionSkin_Research_Report_" + _d.Session.DisplayId);
                StudyRepository.Audit("Report generated", "ScanSession", _d.Session.ScanSessionID, dlg.PrintQueue != null ? dlg.PrintQueue.Name : "");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not print / save the report: " + ex.Message, "LUMYVUE Report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
