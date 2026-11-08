// AboutWindow.cs - "About LUMYVUE": what the system does, how to read its results, and its limits.
// Written for the dermatologist and clinic assistants who use it, in the same warm olive and cream style as the app.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SkinDevApp.Views
{
    public sealed class AboutWindow : Window
    {
        public AboutWindow()
        {
            Title = "About LUMYVUE";
            Width = 780;
            Height = 860;
            MinWidth = 560;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Ui.Brush("#F6F3EA");
            FontFamily = (FontFamily)Application.Current.FindResource("BodyFont");

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var page = new StackPanel { Margin = new Thickness(26, 24, 26, 14) };
            page.Children.Add(Banner());

            // ---------------------------------------------------------------- about
            var about = new StackPanel();
            about.Children.Add(Para("LUMYVUE is an AI-assisted facial skin health monitoring system designed to support dermatological assessment through automated image analysis. " +
                                    "The system examines facial photographs captured from three angles to identify visual patterns associated with acne, hyperpigmentation and eczema."));
            about.Children.Add(Para("For each photograph, the system combines a whole-face analysis with three specialized lesion detectors (YOLOv8). " +
                                    "A trained fusion model combines these readings to produce a separate finding for each supported condition."));
            about.Children.Add(Para("The system also provides visual evidence through marked areas on the photograph, condition-specific certainty levels and approximate facial regions. " +
                                    "These results help dermatologists examine the photographs, understand the AI-generated findings and make their own professional assessment."));
            page.Children.Add(Section("About LUMYVUE", about));

            // ---------------------------------------------------------------- how it works
            page.Children.Add(Section("How LUMYVUE works", Steps(
                new[] { "1", "Facial image capture", "The system captures three facial photographs in sequence: front, left side and right side. Proper positioning, sufficient lighting and clear facial visibility are important for usable images." },
                new[] { "2", "AI-powered image analysis", "Before analysis, the system checks each photograph for sharpness, lighting and whether the face is visible. A photograph that is too poor to read is flagged for recapture. " +
                             "The whole-face reading and the three lesion detectors are then combined to give a separate result for each condition, with its certainty and visual evidence." },
                new[] { "3", "Dermatologist review and verification", "The dermatologist reviews the three photographs, inspects the marked areas and the findings for each condition, and records an independent assessment. " +
                             "AI-generated findings remain separate from the dermatologist's assessment." })));

            // ---------------------------------------------------------------- how to read the results
            var read = new StackPanel();
            read.Children.Add(Para("LUMYVUE presents its findings in a structured format. The following terms explain what each result means."));

            read.Children.Add(Sub("1. Overall assessment outcome"));
            read.Children.Add(Bullets(
                new[] { "Condition detected", "at least one supported condition was identified, based on the system's configured criteria." },
                new[] { "Needs review", "the system found uncertainty, conflicting evidence, or findings that need closer examination. The results should not be interpreted without professional review." },
                new[] { "No condition detected", "no supported condition was identified. This does not guarantee that the skin is free from dermatological conditions." },
                new[] { "Photograph must be retaken", "a photograph was not clear enough to read reliably, so no condition is reported for it." },
                new[] { "Analysis not completed", "the analysis could not be completed." }));

            read.Children.Add(Sub("2. Condition-specific results"));
            read.Children.Add(Para("Acne, hyperpigmentation and eczema are evaluated independently, and each has its own detection status. More than one condition can be identified in the same photograph or scan."));
            read.Children.Add(Bullets(
                new[] { "Detected", "the system found evidence that meets the configured criteria for the condition." },
                new[] { "Possible", "a lesion detector marked an area, but the combined result did not confirm the condition. Dermatologist review is recommended." },
                new[] { "Inconclusive", "the available evidence is insufficient, uncertain or conflicting." },
                new[] { "Not detected", "the system did not find sufficient evidence for the condition." }));
            read.Children.Add(Note("Hyperpigmentation is detector-led: it is reported as Detected when its lesion detector marks an area on the face, at low or moderate certainty. " +
                                   "Eczema findings are less established than those for acne and hyperpigmentation."));

            read.Children.Add(Sub("3. Certainty levels"));
            read.Children.Add(Para("A certainty level is shown for Detected and Not detected results."));
            read.Children.Add(Bullets(
                new[] { "High", "a strong and consistent result against the system's decision limit." },
                new[] { "Moderate", "some supporting evidence, but uncertainty remains." },
                new[] { "Low", "limited confidence. Further professional examination is recommended." }));
            read.Children.Add(Note("Hyperpigmentation never receives High certainty. Certainty reflects the system's own confidence measure. It is not the probability that a patient has a condition, and it does not establish diagnostic accuracy. " +
                                   "The confidence figures can be shown on the Results screen under Show confidence details."));

            read.Children.Add(Sub("4. Marked areas"));
            read.Children.Add(Bullets(
                new[] { "Possible locations", "boxes mark where a lesion detector found features associated with a supported condition." },
                new[] { "Not an outline", "a box does not show the exact boundary of the affected skin or a complete outline of a lesion." },
                new[] { "Not a count", "a box is not necessarily a single lesion, and the boxes are not a lesion count." },
                new[] { "No box", "the absence of a box does not mean that a condition is absent." },
                new[] { "Facial regions", "regions are estimated from the detected face area and are approximate." }));

            read.Children.Add(Sub("5. Dermatologist verification status"));
            read.Children.Add(Bullets(
                new[] { "Pending verification", "the analysis is complete, but no dermatologist has reviewed it yet." },
                new[] { "Verified", "the dermatologist completed the review and answered each detected condition as Confirmed or Not confirmed." },
                new[] { "Requires further review", "further examination, clarification or assessment is needed." }));
            read.Children.Add(Note("For each detected condition, the dermatologist records Confirmed, Not confirmed, Requires further assessment or Pending review, with an optional comment. " +
                                   "Verified does not mean that every AI finding was confirmed."));
            page.Children.Add(Section("How to interpret the results", read));

            // ---------------------------------------------------------------- limitations
            var lim = new StackPanel();
            lim.Children.Add(Sub("1. Research prototype"));
            lim.Children.Add(Para("LUMYVUE is a research prototype exploring the use of artificial intelligence in facial skin health monitoring. It supports qualified healthcare professionals and does not replace their knowledge, judgment or decision-making."));
            lim.Children.Add(Sub("2. Not a medical diagnosis"));
            lim.Children.Add(Para("LUMYVUE does not diagnose conditions, prescribe medication, recommend treatment or replace an in-person consultation with a dermatologist. " +
                                  "All findings are preliminary screening information and must be evaluated by a qualified healthcare professional."));
            lim.Children.Add(Sub("3. Image quality"));
            lim.Children.Add(Para("The quality of the photographs affects how reliable the results are. For clear and consistent images:"));
            lim.Children.Add(Bullets(
                new[] { "Lighting", "use adequate, even lighting, and avoid strong shadows, glare or direct light." },
                new[] { "Accessories", "remove glasses, face masks and anything else that covers the face." },
                new[] { "Hair", "keep hair away from the forehead, cheeks and other visible areas." },
                new[] { "Position", "keep the face within the frame, at the recommended distance and position." },
                new[] { "Steadiness", "avoid movement, and make sure the photograph is in focus." }));
            lim.Children.Add(Note("A blurred, poorly lit or partly covered photograph can make the analysis unreliable. The system flags such photographs for recapture."));
            lim.Children.Add(Sub("4. Supported conditions and scope"));
            lim.Children.Add(Para("LUMYVUE is configured to analyze three categories: acne, hyperpigmentation and eczema. It does not identify other conditions and does not grade severity. " +
                                  "A result of no condition detected must not be read as confirmation of healthy skin."));
            lim.Children.Add(Sub("5. Model information"));
            lim.Children.Add(Para("Technical details and version information are on the Model information screen."));
            page.Children.Add(Section("Important information and limitations", lim));

            page.Children.Add(Ui.Text("LUMYVUE — AI-assisted facial skin health monitoring. Supporting dermatological assessment through artificial intelligence and visual analysis.",
                                      12, false, Ui.Muted, new Thickness(2, 2, 2, 0)));

            root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var close = Ui.Btn("Close", (s, e) => Close(), "PrimaryButton", 130);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(26, 6, 26, 20);
            Grid.SetRow(close, 1);
            root.Children.Add(close);

            Content = root;
        }

        private static UIElement Banner()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "LUMYVUE",
                FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
                FontStyle = FontStyles.Italic,
                FontSize = 40,
                Foreground = Brushes.White
            });
            sp.Children.Add(new TextBlock { Text = "AI skin health monitoring system", FontSize = 14, Foreground = Ui.Brush("#E8CCC2"), Margin = new Thickness(2, 0, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "Research prototype", FontSize = 12, Foreground = Ui.Brush("#DCD4C1"), Margin = new Thickness(2, 8, 0, 0) });

            // the skin-tone ribbon from the login screen
            var ribbon = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 14, 0, 0) };
            foreach (string c in new[] { "#F3DFD0", "#E9C6A8", "#D9A97F", "#BC8559", "#8C5A3C", "#5B3A29" })
                ribbon.Children.Add(new Border { Width = 38, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Brush(c), Margin = new Thickness(0, 0, 4, 0) });
            sp.Children.Add(ribbon);

            return new Border
            {
                Background = (Brush)Application.Current.FindResource("HeaderGradient"),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(28, 24, 28, 24),
                Margin = new Thickness(0, 0, 0, 14),
                Child = sp
            };
        }

        private static UIElement Section(string title, UIElement body)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
                FontStyle = FontStyles.Italic,
                FontSize = 22,
                Foreground = Ui.Ink,
                Margin = new Thickness(0, 0, 0, 10)
            });
            sp.Children.Add(body);
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        private static UIElement Para(string text) => Ui.Text(text, 13, false, Ui.Ink, new Thickness(0, 0, 0, 10));

        /// <summary>A sub-heading inside a section.</summary>
        private static UIElement Sub(string text) => Ui.Text(text, 14, true, Ui.Ink, new Thickness(0, 8, 0, 6));

        /// <summary>A short explanatory note under a list.</summary>
        private static UIElement Note(string text)
        {
            return new Border
            {
                Background = Ui.Brush("#F7F5EC"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8),
                Child = Ui.Text(text, 12.5, false, Ui.Ink, new Thickness(0))
            };
        }

        private static UIElement Steps(params string[][] steps)
        {
            var sp = new StackPanel();
            foreach (string[] st in steps)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Brush("#EFD7CF"),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = st[0], FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#8A5A3C"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                row.Children.Add(dot);
                var text = new StackPanel();
                text.Children.Add(Ui.Text(st[1], 14, true, Ui.Ink, new Thickness(0, 2, 0, 2)));
                text.Children.Add(Ui.Text(st[2], 13, false, Ui.Ink, new Thickness(0)));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            return sp;
        }

        private static UIElement Bullets(params string[][] items)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (string[] it in items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Brush("#C99C8E"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) });
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Ink };
                text.Inlines.Add(new System.Windows.Documents.Run(it[0]) { FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new System.Windows.Documents.Run(" — " + it[1]));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            return sp;
        }
    }
}
