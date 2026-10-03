// ============================================================================
// ResearchReportWindow.cs  -  namespace SkinDevApp.Views
//
// "PrecisionSkin Research Analysis Report" for one saved scan session. Built only
// from the database and the saved image files, so a report generated later shows
// exactly what was stored. Page 1: identity, model, overall + per-view scores,
// disagreement, human assessments, Fitzpatrick (reserved), disclaimer.
// Page 2: per-view original + four class-specific Grad-CAM++ overlays.
// Pages 3-4: blank printable forms (researcher / licensed dermatologist) to be completed
// by hand and signed; staff then transcribe them in the Researcher Verification window.
// Not titled as a medical / diagnostic report.
// ============================================================================

using System;
using System.Collections.Generic;
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
        private const double PageW = 595, PageH = 842;

        private readonly SessionDetail _d;
        private readonly List<FrameworkElement> _pages = new List<FrameworkElement>();
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
            Title = "PrecisionSkin Research Analysis Report — " + d.Session.DisplayId;
            Width = 720;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Brush("#E5E7EB");

            var pagesPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
            _pages.Add(SummaryPage());
            _pages.Add(ImagesPage());
            _pages.Add(FormPage(false));
            _pages.Add(FormPage(true));
            foreach (FrameworkElement p in _pages) pagesPanel.Children.Add(p);

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

        // ----------------------------------------------------------- building --

        private static TextBlock T(string text, double size, bool bold = false, string color = "#1A2328", double bottom = 2)
        {
            return new TextBlock
            {
                Text = text ?? "—",
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Ui.Brush(color),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, bottom)
            };
        }

        private static Border Page(StackPanel content)
        {
            return new Border
            {
                Width = PageW,
                Height = PageH,
                Background = Brushes.White,
                Padding = new Thickness(34, 32, 34, 28),
                Margin = new Thickness(0, 0, 0, 16),
                Child = content
            };
        }

        private StackPanel Head(string subtitle)
        {
            var sp = new StackPanel();
            var top = new DockPanel();
            var right = T("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 8, false, "#555555");
            right.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);
            top.Children.Add(T("PrecisionSkin / LUMYVUE", 18, true, "#1A2328", 0));
            sp.Children.Add(top);
            sp.Children.Add(T(subtitle, 10.5, true, "#8A827A", 4));
            sp.Children.Add(new Border { Height = 2, Background = Ui.Ink, Margin = new Thickness(0, 0, 0, 8) });
            return sp;
        }

        private static Grid Table(IEnumerable<KeyValuePair<string, string>> rows, double labelWidth = 150, double size = 8.5)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int i = 0;
            foreach (var kv in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = T(kv.Key, size, false, "#6B7280", 1);
                var v = T(string.IsNullOrWhiteSpace(kv.Value) ? "—" : kv.Value, size, true, "#1A2328", 1);
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                g.Children.Add(k); g.Children.Add(v);
                i++;
            }
            return g;
        }

        private static TextBlock Section(string title) => T(title, 10, true, "#1A2328", 3);

        private static Border Rule() => new Border { Height = 1, Background = Ui.Brush("#E2EADF"), Margin = new Thickness(0, 6, 0, 6) };

        private FrameworkElement SummaryPage()
        {
            ScanSessionRow s = _d.Session;
            ModelVersion m = _d.Model;
            var sp = Head("PrecisionSkin Research Analysis Report");

            sp.Children.Add(Table(new[]
            {
                Ui.KV("PatientID", s.ParticipantID ?? "unlinked" + (string.IsNullOrEmpty(s.LegacyPatientLabel) ? "" : " (" + s.LegacyPatientLabel + ")")),
                Ui.KV("ScanSessionID", s.DisplayId + "   (" + s.ScanSessionID + ")"),
                Ui.KV("Date / time", s.StartedAt),
                Ui.KV("Scan", s.ScanMode == "Single" ? "Single capture" : s.CompletionText + " views (Front / Left / Right) · status " + s.ScanStatus),
                Ui.KV("Model", m == null ? "unknown" : m.ModelName + " · " + m.Architecture + " · run " + m.RunName),
                Ui.KV("Model hash (ONNX)", m?.OnnxSha256),
                Ui.KV("Grad-CAM++ service", m == null ? null : (m.ServiceVersion ?? "") + " · " + ModelVersion.Short(m.ServiceSha256))
            }, 120));
            sp.Children.Add(Rule());

            // overall
            sp.Children.Add(Section(s.ScanMode == "Single" ? "AI result" : "Overall multi-view AI result"));
            sp.Children.Add(T("Primary AI classification: " + (s.OverallPredictedClass ?? "no usable result") +
                              (s.OverallScore.HasValue ? "  (" + s.ScoreText + (s.ScanMode == "Single" ? " model class score)" : " pooled model class score)") : ""),
                              10, true, "#1A2328", 4));
            double[] overall = s.ScanMode == "Single" || _d.Fusion == null || _d.Fusion.PooledPercent == null
                ? (_d.Views.FirstOrDefault(v => v.Scores != null)?.Scores?.AsArray() ?? new double[4])
                : StudyText.Classes.Select(c => { double v; return _d.Fusion.PooledPercent.TryGetValue(c, out v) ? v : 0; }).ToArray();
            var bars = Ui.ScoreBars(overall, 5, compact: true);
            bars.Width = 300;
            bars.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(bars);

            // per view
            if (_d.Views.Count > 0)
            {
                sp.Children.Add(Section("Per-view Model Class Scores"));
                var g = new Grid();
                string[] head = { "View", "Predicted", "Acne", "Hyperpig.", "Eczema", "Normal", "Capture quality" };
                double[] widths = { 50, 90, 50, 55, 50, 50, 1 };
                for (int c = 0; c < head.Length; c++)
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = c == head.Length - 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(widths[c]) });
                int r = 0;
                void Row(params string[] cells)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (int c = 0; c < cells.Length; c++)
                    {
                        var t = T(cells[c], 8, r == 0, r == 0 ? "#6B7280" : "#1A2328", 1);
                        Grid.SetRow(t, r); Grid.SetColumn(t, c);
                        g.Children.Add(t);
                    }
                    r++;
                }
                Row(head);
                foreach (CaptureViewRow v in _d.Views)
                {
                    if (v.Scores == null) { Row(v.ViewType, "not analysed", "", "", "", "", v.QualityStatus ?? ""); continue; }
                    double[] a = v.Scores.AsArray();
                    Row(v.ViewType, v.Scores.PredictedClass, Ui.Pct(a[0]), Ui.Pct(a[1]), Ui.Pct(a[2]), Ui.Pct(a[3]), v.QualityStatus ?? "");
                }
                sp.Children.Add(g);
            }

            if (s.ScanMode != "Single")
            {
                if (s.ViewDisagreement)
                {
                    sp.Children.Add(T("View disagreement detected — the individual angles should be reviewed.", 8.5, true, "#8A5A00", 1));
                    if (_d.Fusion != null) foreach (string n in _d.Fusion.Notes) sp.Children.Add(T("• " + n, 8, false, "#8A5A00", 1));
                }
                else sp.Children.Add(T("Views consistent: every captured angle has the same top class.", 8.5, false, "#3F6B3E", 1));
                sp.Children.Add(T("Fusion: quality-weighted pooling of per-view scores (heuristic; validation limited without paired multi-view labelled data).", 7.5, false, "#777777", 2));
            }
            sp.Children.Add(Rule());

            // human assessments
            sp.Children.Add(Section("Researcher reference assessment"));
            ResearcherEvaluation ev = _d.Evaluations.FirstOrDefault();
            if (ev == null) sp.Children.Add(T("Pending.", 8.5, false, "#555555"));
            else
            {
                var rows = new List<KeyValuePair<string, string>>
                {
                    Ui.KV("Assessment", ev.ResearcherClassification), Ui.KV("Agreement with AI", ev.AgreementWithAI),
                    Ui.KV("Researcher / date", ev.ResearcherID + " · " + ev.EvaluationDate), Ui.KV("Researcher notes", ev.Notes)
                };
                foreach (var c in new[] { Tuple.Create("Front", ev.FrontComment), Tuple.Create("Left", ev.LeftComment), Tuple.Create("Right", ev.RightComment) })
                    if (!string.IsNullOrWhiteSpace(c.Item2)) rows.Add(Ui.KV(c.Item1 + " comment", c.Item2));
                sp.Children.Add(Table(rows, 120));
            }

            sp.Children.Add(Section("Licensed dermatologist validation"));
            DermatologistValidation dv = _d.Validations.FirstOrDefault(x => x.ValidationStatus == "Completed") ?? _d.Validations.FirstOrDefault();
            if (dv == null) sp.Children.Add(T("Pending.", 8.5, false, "#555555"));
            else sp.Children.Add(Table(new[]
            {
                Ui.KV("Assessment", dv.DermatologistAssessment), Ui.KV("Agreement with AI", dv.AgreementWithAI),
                Ui.KV("Agreement w/ researcher", dv.AgreementWithResearcher),
                Ui.KV("Validator / date", dv.DermatologistID + " (" + dv.ProfessionalRole + ") · " + dv.ValidationDate),
                Ui.KV("Status", dv.ValidationStatus), Ui.KV("Notes", dv.Notes)
            }, 120));
            sp.Children.Add(Rule());

            // Fitzpatrick reserved
            SkinProfile skin = string.IsNullOrEmpty(s.ParticipantID) ? null : StudyRepository.GetSkinProfile(s.ParticipantID);
            sp.Children.Add(Section("Skin-tone / Fitzpatrick"));
            sp.Children.Add(Table(new[]
            {
                Ui.KV("AI assessment", "Not available in this model version"),
                Ui.KV("Manual / self-reported", skin == null || string.IsNullOrWhiteSpace(skin.FitzpatrickManual) ? "Not collected" : skin.FitzpatrickManual + " (" + skin.FitzpatrickSource + ")")
            }, 120));
            sp.Children.Add(Rule());

            sp.Children.Add(T("Research prototype disclaimer", 8.5, true, "#1A2328", 1));
            sp.Children.Add(T(StudyText.PrototypeNotice + " " + StudyText.ScoresNote + " " + StudyText.AttributionNote, 7.5, false, "#555555", 0));
            return Page(sp);
        }

        private FrameworkElement ImagesPage()
        {
            var sp = Head("Captured views and class-specific Grad-CAM++ attribution maps");
            sp.Children.Add(T("PatientID " + (_d.Session.ParticipantID ?? "unlinked") + "   ·   ScanSessionID " + _d.Session.DisplayId, 8.5, false, "#555555", 8));

            string[] order = { "Acne", "Eczema", "Hyperpigmentation", "Normal" };
            foreach (CaptureViewRow v in _d.Views)
            {
                string line = v.ViewType.ToUpperInvariant() + (v.Scores != null ? "   —   " + v.Scores.PredictedClass + " " + Ui.Pct(v.Scores.Confidence) : "   —   not analysed");
                sp.Children.Add(T(line, 9.5, true, "#1A2328", 3));

                var row = new UniformGrid5();
                row.Add(Cell("Original", v.AnalysedImagePath ?? v.OriginalImagePath, "#9CA3AF"));
                foreach (string cls in order)
                {
                    AttributionMapRow m = v.Maps.FirstOrDefault(x => x.TargetClass == cls);
                    double? score = v.Scores?.AsArray()[Array.IndexOf(StudyText.Classes, cls)];
                    row.Add(Cell((cls == "Normal" ? "Normal Skin" : cls) + (score.HasValue ? " " + Ui.Pct(score.Value) : ""),
                                 m?.OverlayPath, Ui.ClassColor(cls), m == null ? "unavailable" : null));
                }
                sp.Children.Add(row.Grid);
            }
            if (_d.Views.Count == 0) sp.Children.Add(T("No images were saved for this scan.", 9, false, "#555555"));

            sp.Children.Add(T("Colour identifies the class (Acne red, Hyperpigmentation blue, Eczema yellow/orange, Normal green); opacity shows attribution strength. " +
                              StudyText.AttributionNote, 7.5, false, "#555555", 0));
            return Page(sp);
        }

        private static StackPanel Cell(string caption, string path, string colorHex, string missing = null)
        {
            var cell = new StackPanel { Margin = new Thickness(2, 0, 2, 6) };
            cell.Children.Add(Ui.Thumb(path, 92, colorHex, missing ?? "not available"));
            cell.Children.Add(T(caption, 7, true, "#1A2328", 0));
            return cell;
        }

        private sealed class UniformGrid5
        {
            public readonly Grid Grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            private int _n;
            public UniformGrid5() { for (int i = 0; i < 5; i++) Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); }
            public void Add(UIElement e) { Grid.SetColumn(e, _n++); Grid.Children.Add(e); }
        }

        // ------------------------------------------------- printable forms --

        private static readonly string[] FormLabels =
            { "Acne", "Eczema", "Hyperpigmentation", "Normal", "Other / uncertain", "Unusable image" };
        private static readonly string[] FormAgreement = { "Agree", "Partially agree", "Disagree", "Uncertain" };

        private static TextBlock FormLabel(string text) => T(text, 8.5, true, "#1A2328", 3);

        /// <summary>Wrapping row of "box + option" entries.</summary>
        private static WrapPanel Boxes(IEnumerable<string> options)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (string o in options)
            {
                var t = T("☐  " + o, 9, false, "#1A2328", 0);
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
                sp.Children.Add(new Border { Height = 22, BorderBrush = Ui.Brush("#9CA3AF"), BorderThickness = new Thickness(0, 0, 0, 1) });
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
                    BorderBrush = Ui.Brush("#9CA3AF"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = fields[i].Item2 == null ? null : T(fields[i].Item2, 9, true, "#1A2328", 0)
                });
                Grid.SetColumn(cell, i);
                g.Children.Add(cell);
            }
            return g;
        }

        private FrameworkElement FormPage(bool dermatologist)
        {
            ScanSessionRow s = _d.Session;
            var sp = Head(dermatologist
                ? "Licensed dermatologist validation form (to be completed by hand)"
                : "Researcher reference assessment form (to be completed by hand)");

            sp.Children.Add(Table(new[]
            {
                Ui.KV("PatientID", s.ParticipantID ?? "unlinked"),
                Ui.KV("ScanSessionID", s.DisplayId),
                Ui.KV("Date / time of scan", s.StartedAt),
                Ui.KV("AI result (for reference)", (s.OverallPredictedClass ?? "no usable result") + (s.OverallScore.HasValue ? "  (" + s.ScoreText + ")" : ""))
            }, 130, 9));
            sp.Children.Add(Rule());
            sp.Children.Add(T(dermatologist
                ? "A separate validation layer. It does not replace the researcher assessment and never overwrites the AI prediction."
                : "Select the reference label(s) you assign to this scan. More than one label may be selected if the protocol allows it.",
                8, false, "#555555", 8));

            if (dermatologist)
            {
                sp.Children.Add(FieldRow(Tuple.Create("Validator ID (coded) *", (string)null),
                                         Tuple.Create("Validator name (only if the protocol stores it)", (string)null)));
                sp.Children.Add(FieldRow(Tuple.Create("Professional role", "Licensed Dermatologist"),
                                         Tuple.Create("License / credential reference (only if required)", (string)null)));
            }

            sp.Children.Add(FormLabel((dermatologist ? "Dermatologist reference assessment" : "Reference assessment") + " *"));
            sp.Children.Add(Boxes(FormLabels));

            sp.Children.Add(FormLabel("Agreement with AI *"));
            sp.Children.Add(Boxes(FormAgreement));

            if (dermatologist)
            {
                sp.Children.Add(FormLabel("Agreement with researcher (optional)"));
                sp.Children.Add(Boxes(FormAgreement.Concat(new[] { "No researcher assessment yet" })));
            }

            sp.Children.Add(WriteArea(dermatologist ? "Dermatologist notes" : "Researcher notes", dermatologist ? 5 : 4));

            if (!dermatologist)
            {
                sp.Children.Add(FormLabel("Per-view comments (optional)"));
                sp.Children.Add(FieldRow(Tuple.Create("Front", (string)null), Tuple.Create("Left", (string)null), Tuple.Create("Right", (string)null)));
                sp.Children.Add(FieldRow(Tuple.Create("Researcher ID *", (string)null)));
            }
            else
            {
                sp.Children.Add(FormLabel("Validation status *"));
                sp.Children.Add(Boxes(new[] { "Completed", "Pending" }));
            }

            // signature block
            sp.Children.Add(Rule());
            sp.Children.Add(FieldRow(
                Tuple.Create(dermatologist ? "Signature of licensed dermatologist" : "Signature of researcher", (string)null),
                Tuple.Create("Printed name", (string)null),
                Tuple.Create("Date", (string)null)));
            sp.Children[sp.Children.Count - 1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 22, 0, 8));

            sp.Children.Add(T("After completion, the research team enters these answers in the system (Researcher Verification window, tab \"" +
                              (dermatologist ? "Licensed Dermatologist Validation" : "Researcher Assessment") +
                              "\"). Keep the signed sheet with the study records. Human assessments are saved separately and never overwrite the AI prediction.",
                              7.5, false, "#555555", 0));
            return Page(sp);
        }

        // ------------------------------------------------------------ export --

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

                const double pw = 793.7, ph = 1122.5;      // A4 at 96 dpi
                var doc = new FixedDocument();
                doc.DocumentPaginator.PageSize = new Size(pw, ph);
                foreach (FrameworkElement page in _pages)
                {
                    var fixedPage = new FixedPage { Width = pw, Height = ph, Background = Brushes.White };
                    fixedPage.Children.Add(new System.Windows.Shapes.Rectangle
                    {
                        Width = pw,
                        Height = ph,
                        Fill = new VisualBrush(page) { Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Top }
                    });
                    var content = new PageContent();
                    ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
                    doc.Pages.Add(content);
                }

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
