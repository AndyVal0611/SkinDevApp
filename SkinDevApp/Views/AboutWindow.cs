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
            Title = "About PrecisionSkin";
            Width = 720;
            Height = 780;
            MinWidth = 560;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Ui.Brush("#F8F1E6");
            FontFamily = (FontFamily)Application.Current.FindResource("BodyFont");

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var page = new StackPanel { Margin = new Thickness(26, 24, 26, 14) };
            page.Children.Add(Banner());
            page.Children.Add(Section("What PrecisionSkin does", Steps()));
            page.Children.Add(Section("How to read the results", Bullets(
                new[] { "Category match", "how closely a photograph resembles each skin category. The four percentages add up to 100%. They show visual similarity, not the chance that the patient has the condition." },
                new[] { "Heatmap (Grad-CAM++)", "shows which parts of the photograph the model paid most attention to for one category. It is not an outline of the affected skin." },
                new[] { "Candidate lesion marks", "small boxes where a second model suggests possible lesions. Some lesions may be missed and some boxes may be wrong. They are not a lesion count." },
                new[] { "Agreement between views", "whether the front, left and right photographs point to the same category." })));
            page.Children.Add(Section("Important to know", Bullets(
                new[] { "Research prototype", "PrecisionSkin supports, and never replaces, the judgement of a qualified clinician." },
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
            sp.Children.Add(new TextBlock { Text = "PrecisionSkin  ·  AI skin health monitoring", FontSize = 14, Foreground = Ui.Brush("#E9C8AC"), Margin = new Thickness(2, 0, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "Research prototype  ·  Model PrecisionSkinV1", FontSize = 12, Foreground = Ui.Brush("#CFC9B8"), Margin = new Thickness(2, 8, 0, 0) });

            // the skin-tone ribbon from the login screen
            var ribbon = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 14, 0, 0) };
            foreach (string c in new[] { "#F3DFD0", "#E9C6A8", "#D9A97F", "#BC8559", "#8C5A3C", "#5B3A29" })
                ribbon.Children.Add(new Border { Width = 38, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Brush(c), Margin = new Thickness(0, 0, 4, 0) });
            sp.Children.Add(ribbon);

            return new Border
            {
                Background = Ui.Brush("#5F6652"),
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
                FontSize = 20,
                Foreground = Ui.Ink,
                Margin = new Thickness(0, 0, 0, 10)
            });
            sp.Children.Add(body);
            return Ui.Card(sp, new Thickness(0, 0, 0, 12));
        }

        private static UIElement Steps()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text("PrecisionSkin photographs the face from three angles and uses an artificial-intelligence model to compare each photograph " +
                                    "with four skin categories: acne, hyperpigmentation, eczema and normal skin. It then shows the evidence behind the result so that a clinician can judge it.",
                                    13, false, Ui.Ink, new Thickness(0, 0, 0, 12)));

            string[][] steps =
            {
                new[] { "1", "Capture", "front, left and right photographs, taken one after another." },
                new[] { "2", "Analyse", "the model reads each photograph and marks where it looked." },
                new[] { "3", "Review", "the clinician compares the photographs, the evidence and the AI result, then records their own assessment." }
            };
            foreach (string[] st in steps)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Brush("#F3E4D3"),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = st[0], FontWeight = FontWeights.Bold, Foreground = Ui.Brush("#8A5A3C"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                row.Children.Add(dot);
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Ink, VerticalAlignment = VerticalAlignment.Center };
                text.Inlines.Add(new System.Windows.Documents.Run(st[1]) { FontWeight = FontWeights.Bold });
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
                row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Brush("#D09F7C"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) });
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Ink };
                text.Inlines.Add(new System.Windows.Documents.Run(it[0]) { FontWeight = FontWeights.Bold });
                text.Inlines.Add(new System.Windows.Documents.Run(" — " + it[1]));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            return sp;
        }

        private static UIElement ModelNote()
        {
            var note = Ui.Text("Model: PrecisionSkinV1 — four categories (acne, hyperpigmentation, eczema, normal skin). " +
                               "Technical details and version information are on the Model Information screen.", 11.5, false, Ui.Muted, new Thickness(2, 2, 2, 0));
            return note;
        }
    }
}
