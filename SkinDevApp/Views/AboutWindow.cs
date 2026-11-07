// AboutWindow.cs - "About PrecisionSkin": what the system does, how to read its output, and its limits.
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
            Width = 720;
            Height = 780;
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
            page.Children.Add(Section("What LUMYVUE does", Steps()));
            page.Children.Add(Section("How to read the results", Bullets(
                new[] { "Outcome", "each scan is labelled Condition detected, Needs review, or No condition detected. Needs review means part of the result is uncertain and a dermatologist should look closely." },
                new[] { "Per condition", "acne, hyperpigmentation and eczema are each reported as Detected, Possible, Inconclusive or Not detected, with a certainty (high, moderate or low)." },
                new[] { "Facial regions", "where the lesion detectors marked areas: forehead, right or left cheek, nose, mouth / chin. They are possible locations, not a lesion count." },
                new[] { "Heat map", "shows where the detectors found evidence for a condition. It is not an outline of the affected skin." },
                new[] { "Verification status", "Pending verification until a dermatologist reviews the scan; then Verified, or Requires further review." })));
            page.Children.Add(Section("Important to know", Bullets(
                new[] { "Research prototype", "LUMYVUE supports, and never replaces, the judgement of a qualified clinician." },
                new[] { "Not a diagnosis", "it does not give a diagnosis or treatment advice." },
                new[] { "Photo quality matters", "use even lighting, remove glasses or a mask, and keep hair away from the face." })));
            page.Children.Add(ModelNote());

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
            sp.Children.Add(new TextBlock { Text = "AI skin health monitoring", FontSize = 14, Foreground = Ui.Brush("#E8CCC2"), Margin = new Thickness(2, 0, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "Research prototype  ·  Model PrecisionSkinV1", FontSize = 12, Foreground = Ui.Brush("#DCD4C1"), Margin = new Thickness(2, 8, 0, 0) });

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

        private static UIElement Steps()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("LUMYVUE photographs the face from three angles. For each photograph, one trained AI model combines a whole-face reading with three " +
                                    "lesion detectors and reports acne, hyperpigmentation and eczema separately, with the facial regions involved, so that a dermatologist can judge it.",
                                    13, false, Ui.Ink, new Thickness(0, 0, 0, 12)));

            string[][] steps =
            {
                new[] { "1", "Capture", "front, left and right photographs, taken one after another." },
                new[] { "2", "Analyse", "the AI reads each photograph, marks possible lesion areas and states how certain it is." },
                new[] { "3", "Review", "the clinician compares the photographs, the evidence and the AI result, then records their own assessment." }
            };
            foreach (string[] st in steps)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Brush("#EFD7CF"),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = st[0], FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#8A5A3C"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                row.Children.Add(dot);
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Ink, VerticalAlignment = VerticalAlignment.Center };
                text.Inlines.Add(new System.Windows.Documents.Run(st[1]) { FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new System.Windows.Documents.Run(" — " + st[2]));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            return sp;
        }

        private static UIElement Bullets(params string[][] items)
        {
            var sp = new StackPanel();
            foreach (string[] it in items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
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

        private static UIElement ModelNote()
        {
            var note = Ui.Text("Model: LUMYVUE final model (V6) — a MobileNetV2 whole-face reading fused with three YOLOv8 lesion detectors; three conditions, " +
                               "with normal skin derived when none is found. Technical details and version information are on the Model information screen.", 12, false, Ui.Muted, new Thickness(2, 2, 2, 0));
            return note;
        }
    }
}
