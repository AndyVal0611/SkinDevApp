// ============================================================================
// Ui.cs  -  namespace SkinDevApp.Views
//
// Small shared helpers for the study screens: navigation, the dark header bar,
// code-built text / cards / score bars, and the system-readiness check used by
// the Dashboard, Preparation and Model Information screens.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AForge.Video.DirectShow;
using SkinDevApp.Data;
using SkinDevApp.Explainability;

namespace SkinDevApp.Views
{
    public static class Nav
    {
        private static Frame MainFrame => ((MainWindow)Application.Current.MainWindow).MainFrame;

        public static void Go(Page page) => MainFrame.Navigate(page);

        public static void Home() => Go(new HomePage());

        public static void Back()
        {
            if (MainFrame.CanGoBack) MainFrame.GoBack();
            else Home();
        }

        public static void SignOut()
        {
            AppSession.SignOut();
            Go(new LoginForm());
        }

        /// <summary>Researcher-only screens call this first; operators are told and sent home.</summary>
        public static bool RequireResearcher(string what)
        {
            if (AppSession.IsResearcher) return true;
            LumyDialog.Show(what + " is available to signed-in researchers / administrators only.",
                "LUMYVUE", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    public static class Ui
    {
        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Ink = Brush("#3A3B2C");
        public static readonly SolidColorBrush Bar = Brush("#6E6F52");
        public static readonly SolidColorBrush Muted = Brush("#6F6F58");
        public static readonly SolidColorBrush Good = Brush("#588157");
        public static readonly SolidColorBrush Warn = Brush("#B7791F");
        public static readonly SolidColorBrush Bad = Brush("#B91C1C");
        public static readonly SolidColorBrush Info = Brush("#818263");
        public static readonly SolidColorBrush Line = Brush("#E3DDCB");

        public static readonly string[] ClassHex = { "#E11D1D", "#1D4ED8", "#E8A200", "#16A34A" };   // Acne, Hyper, Eczema, Normal

        public static string ClassColor(string cls)
        {
            int i = Array.IndexOf(StudyText.Classes, cls);
            return i >= 0 ? ClassHex[i] : "#6B7280";
        }

        // ------------------------------------------------------------- header --

        /// <summary>
        /// The LUMYVUE bar at the top of every screen: wordmark and skin-tone ribbon, the page title, the signed-in user and the
        /// navigation buttons. On narrow screens the buttons shrink to icons and the subtitle is hidden.
        /// </summary>
        public static Border Header(string title, bool showHome = true, bool showBack = true)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(BrandMark(true));

            var divider = new Border { Width = 1, Height = 34, Background = Brush("#40FFFFFF"), Margin = new Thickness(20, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(divider, 1);
            grid.Children.Add(divider);

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = title, Foreground = Brush("#F7F5E9"), FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var subtitle = new TextBlock { Text = "AI skin health monitoring", Foreground = Brush("#DDDCC2"), FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0) };
            titles.Children.Add(subtitle);
            Grid.SetColumn(titles, 2);
            grid.Children.Add(titles);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement chip = UserChip();
            if (chip != null) right.Children.Add(chip);
            var buttons = new List<Button>();
            if (showBack) buttons.Add(HeaderButton("Back", "Icon.Back", (s, e) => Nav.Back()));
            if (showHome) buttons.Add(HeaderButton("Dashboard", "Icon.Home", (s, e) => Nav.Home()));
            buttons.Add(HeaderButton("Sign out", "Icon.Logout", (s, e) => Nav.SignOut()));
            foreach (Button b in buttons) right.Children.Add(b);
            Grid.SetColumn(right, 3);
            grid.Children.Add(right);

            var bar = new Border
            {
                Background = (Brush)Application.Current.FindResource("HeaderGradient"),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(22, 12, 14, 12),
                Margin = new Thickness(8, 0, 8, 16),
                Child = grid
            };
            bar.SizeChanged += (s, e) =>
            {
                bool compact = bar.ActualWidth < 1050;
                subtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                if (chip != null) SetChipCompact(chip, bar.ActualWidth < 1200);
                foreach (Button b in buttons) SetHeaderButtonCompact(b, compact);
            };
            return bar;
        }

        /// <summary>The LUMYVUE band at the top of a secondary window (review, gallery, report): wordmark, ribbon, title and subtitle.</summary>
        public static Border WindowBand(string title, string subtitle, out TextBlock titleText, out TextBlock subtitleText)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(BrandMark(true, 24));
            var divider = new Border { Width = 1, Height = 34, Background = Brush("#40FFFFFF"), Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(divider, 1);
            grid.Children.Add(divider);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleText = new TextBlock { Text = title, Foreground = Brush("#F7F5E9"), FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            subtitleText = new TextBlock { Text = subtitle ?? "", Foreground = Brush("#DDDCC2"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            titles.Children.Add(titleText);
            titles.Children.Add(subtitleText);
            Grid.SetColumn(titles, 2);
            grid.Children.Add(titles);
            return new Border
            {
                Background = (Brush)Application.Current.FindResource("HeaderGradient"),
                CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 14, 22, 14), Margin = new Thickness(0, 0, 0, 14), Child = grid
            };
        }

        public static Border WindowBand(string title, string subtitle)
        {
            TextBlock a, b;
            return WindowBand(title, subtitle, out a, out b);
        }

        /// <summary>The LUMYVUE wordmark with the six-tone skin ribbon under it.</summary>
        public static FrameworkElement BrandMark(bool onDark, double size = 26)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = "LUMYVUE", FontSize = size, FontStyle = FontStyles.Italic,
                FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
                Foreground = onDark ? Brushes.White : Brush("#2F3024")
            });
            sp.Children.Add(SkinRibbon(size * 0.62, 3, new Thickness(2, 3, 0, 0)));
            return sp;
        }

        /// <summary>Six Fitzpatrick-inspired skin tones: the brand's signature detail.</summary>
        public static StackPanel SkinRibbon(double swatch = 16, double height = 3, Thickness? margin = null)
        {
            var ribbon = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin ?? new Thickness(0) };
            foreach (var hex in new[] { "#F3DFD0", "#E9C6A8", "#D9A97F", "#BC8559", "#8C5A3C", "#5B3A29" })
                ribbon.Children.Add(new Border { Width = swatch, Height = height, CornerRadius = new CornerRadius(height / 2), Margin = new Thickness(0, 0, 3, 0), Background = Brush(hex) });
            return ribbon;
        }

        /// <summary>The signed-in user: initials in a blush circle and the user name.</summary>
        private static FrameworkElement UserChip()
        {
            string user = AppSession.UserId;
            if (string.IsNullOrEmpty(user)) return null;
            string initials = new string(user.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Brush("#DDBAAE"),
                Child = new TextBlock { Text = initials, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush("#2F3024"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var name = new TextBlock { Text = user, Foreground = Brush("#F7F5E9"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 4, 0) };
            row.Children.Add(name);
            return new Border
            {
                Background = Brush("#1FFFFFFF"), CornerRadius = new CornerRadius(22), Padding = new Thickness(4, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = row,
                ToolTip = "Signed in as " + user, Tag = name
            };
        }

        private static void SetChipCompact(FrameworkElement chip, bool compact)
        {
            var name = chip.Tag as TextBlock;
            if (name != null) name.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ((Border)chip).Padding = compact ? new Thickness(4) : new Thickness(4, 4, 10, 4);
        }

        /// <summary>
        /// Makes a multi-column Grid responsive: below the given width every child moves under the previous one in a single column
        /// (portrait monitors, narrow windows); above it the original columns come back. lastRowStar lets the last child fill the remaining height.
        /// </summary>
        public static void StackWhenNarrow(Grid g, double breakpoint, bool lastRowStar = false, Action<bool> onChange = null)
        {
            var cols = g.ColumnDefinitions.Select(c => new ColumnDefinition { Width = c.Width, MinWidth = c.MinWidth, MaxWidth = c.MaxWidth }).ToList();
            var rows = g.RowDefinitions.Select(r => new RowDefinition { Height = r.Height, MinHeight = r.MinHeight, MaxHeight = r.MaxHeight }).ToList();
            var kids = g.Children.Cast<UIElement>().ToList();
            var home = kids.Select(k => new[] { Grid.GetRow(k), Grid.GetColumn(k), Grid.GetColumnSpan(k), Grid.GetRowSpan(k) }).ToList();
            bool? narrow = null;

            g.SizeChanged += (s, e) =>
            {
                bool n = g.ActualWidth > 0 && g.ActualWidth < breakpoint;
                if (narrow == n) return;
                narrow = n;
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                if (n)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    for (int i = 0; i < kids.Count; i++)
                    {
                        bool last = i == kids.Count - 1;
                        g.RowDefinitions.Add(new RowDefinition { Height = last && lastRowStar ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                        Grid.SetRow(kids[i], i); Grid.SetColumn(kids[i], 0); Grid.SetColumnSpan(kids[i], 1); Grid.SetRowSpan(kids[i], 1);
                    }
                }
                else
                {
                    foreach (ColumnDefinition c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width, MinWidth = c.MinWidth, MaxWidth = c.MaxWidth });
                    foreach (RowDefinition r in rows) g.RowDefinitions.Add(new RowDefinition { Height = r.Height, MinHeight = r.MinHeight, MaxHeight = r.MaxHeight });
                    for (int i = 0; i < kids.Count; i++)
                    {
                        Grid.SetRow(kids[i], home[i][0]); Grid.SetColumn(kids[i], home[i][1]);
                        Grid.SetColumnSpan(kids[i], home[i][2]); Grid.SetRowSpan(kids[i], home[i][3]);
                    }
                }
                if (onChange != null) onChange(n);
            };
        }

        /// <summary>A Grid with n equal columns holding the given items (collapses to one column on narrow screens).</summary>
        public static Grid Cols(double stackBelow, params UIElement[] items)
        {
            var g = new Grid();
            for (int i = 0; i < items.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(items[i], i);
                g.Children.Add(items[i]);
            }
            StackWhenNarrow(g, stackBelow);
            return g;
        }

        // Skin-tone ribbon: the app's one signature detail, six Fitzpatrick-inspired swatches.
        private static Button HeaderButton(string text, string icon, RoutedEventHandler click)
        {
            var b = new Button { Content = IconLabel(icon, text, 17), Style = (Style)Application.Current.FindResource("HeaderButton"), ToolTip = text };
            b.Click += click;
            return b;
        }

        /// <summary>Header buttons show only their icon on narrow screens.</summary>
        private static void SetHeaderButtonCompact(Button b, bool compact)
        {
            var sp = b.Content as StackPanel;
            if (sp == null || sp.Children.Count < 2) return;
            sp.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)sp.Children[0]).Margin = compact ? new Thickness(0) : new Thickness(0, 0, 8, 0);
            b.Padding = compact ? new Thickness(12, 0, 12, 0) : new Thickness(16, 0, 18, 0);
        }

        // -------------------------------------------------------------- icons --

        /// <summary>A LUMYVUE line icon (see Themes/Lumyvue.Icons.xaml). It takes the surrounding text colour unless a brush is given.</summary>
        public static ContentControl Icon(string key, double size = 20, Brush brush = null)
        {
            var c = new ContentControl
            {
                Style = (Style)Application.Current.FindResource("IconBox"),
                Tag = Application.Current.TryFindResource(key) as Geometry,
                Width = size, Height = size
            };
            if (brush != null) c.Foreground = brush;
            return c;
        }

        /// <summary>An icon followed by a label, for button content.</summary>
        public static StackPanel IconLabel(string iconKey, string text, double iconSize = 18)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Icon(iconKey, iconSize));
            ((FrameworkElement)sp.Children[0]).Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        /// <summary>A round tinted badge holding an icon (used on tiles, empty states and dialogs).</summary>
        public static Border IconBadge(string key, double size, string background, string foreground)
        {
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brush(background),
                Child = new Grid { Children = { Icon(key, size * 0.46, Brush(foreground)) } }
            };
        }

        /// <summary>A friendly empty state: an icon, a title and one line of help.</summary>
        public static StackPanel EmptyState(string iconKey, string title, string text)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 28, 24, 28), MaxWidth = 520 };
            var badge = IconBadge(iconKey, 64, "#EFEADC", "#818263");
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(badge);
            sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush("#2F3024"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 4) });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            return sp;
        }

        // ---------------------------------------------------- loading states --

        /// <summary>A small spinning arc for things that are loading.</summary>
        public static FrameworkElement Spinner(double size = 22, string hex = "#818263")
        {
            var track = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Stroke = Brush("#22000000"), StrokeThickness = size / 9 };
            double r = size / 2 - size / 18;
            var arc = new System.Windows.Shapes.Path
            {
                Stroke = Brush(hex), StrokeThickness = size / 9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0},{1} A{2},{2} 0 0 1 {3},{4}",
                        size / 2, size / 2 - r, r, size / 2 + r, size / 2)),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var rot = new RotateTransform();
            arc.RenderTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            return new Grid { Width = size, Height = size, Children = { track, arc } };
        }

        /// <summary>A soft shimmering placeholder bar shown while something loads (a "skeleton").</summary>
        public static Border Skeleton(double width, double height, double radius = 8)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#EFEADC"), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F8F5EC"), 0.5));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#EFEADC"), 1));
            var shift = new TranslateTransform();
            brush.RelativeTransform = shift;
            shift.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(-1, 1, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            var b = new Border { Height = height, CornerRadius = new CornerRadius(radius), Background = brush, Margin = new Thickness(0, 0, 0, 8) };
            if (width > 0) b.Width = width;
            return b;
        }

        // ------------------------------------------------------------ builders --

        public static TextBlock Text(string text, double size = 13, bool bold = false, Brush color = null, Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text ?? "",
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = color ?? Ink,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin ?? new Thickness(0, 0, 0, 4)
            };
        }

        public static Border Card(UIElement child, Thickness? margin = null)
        {
            var b = new Border { Style = (Style)Application.Current.FindResource("Card"), Child = child };
            if (margin.HasValue) b.Margin = margin.Value;
            return b;
        }

        /// <summary>Two-column "label: value" table.</summary>
        public static Grid KeyValues(IEnumerable<KeyValuePair<string, string>> rows, double labelWidth = 170)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int i = 0;
            foreach (KeyValuePair<string, string> kv in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = Text(kv.Key, 12, false, Muted, new Thickness(0, 2, 8, 2));
                var v = Text(string.IsNullOrWhiteSpace(kv.Value) ? "—" : kv.Value, 12.5, true, Ink, new Thickness(0, 2, 0, 2));
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                g.Children.Add(k); g.Children.Add(v);
                i++;
            }
            return g;
        }

        public static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

        /// <summary>Two columns where the LABEL is bold and the information is regular text (easier to scan down a list of facts).</summary>
        public static Grid InfoRows(IEnumerable<KeyValuePair<string, string>> rows, double labelWidth = 150, double size = 13)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 18, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int i = 0;
            foreach (KeyValuePair<string, string> kv in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = Text(kv.Key, size, true, Ink, new Thickness(0, 3, 12, 3));
                var v = Text(string.IsNullOrWhiteSpace(kv.Value) ? "—" : kv.Value, size, false, Ink, new Thickness(0, 3, 0, 3));
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                g.Children.Add(k); g.Children.Add(v);
                i++;
            }
            return g;
        }

        /// <summary>A soft coloured message box. level: 0 = all fine (green), 1 = look closer (amber), 2 = needs attention (red).</summary>
        public static Border Callout(string headline, string detail, int level)
        {
            string bg = level == 0 ? "#EEF3E8" : level == 1 ? "#FFF6E6" : "#FBEDEA";
            string line = level == 0 ? "#CBDCBF" : level == 1 ? "#F0D49A" : "#EDB9B1";
            string ink = level == 0 ? "#3E6B3D" : level == 1 ? "#8A5A00" : "#9B2F25";
            string icon = level == 0 ? "✓" : "!";

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brush(ink), VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0),
                Child = new TextBlock { Text = icon, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var text = new StackPanel();
            text.Children.Add(Text(headline, 13.5, true, Brush(ink), new Thickness(0, 0, 0, 2)));
            if (!string.IsNullOrEmpty(detail)) text.Children.Add(Text(detail, 12, false, Ink, new Thickness(0)));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            return new Border
            {
                Background = Brush(bg), BorderBrush = Brush(line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 8, 0, 4), Child = row
            };
        }

        public static Border Pill(string text, Brush fg, string bgHex = "#EFD7CF")
        {
            return new Border
            {
                Background = Brush(bgHex),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 3, 11, 4),
                Margin = new Thickness(0, 0, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = 11.5, FontWeight = FontWeights.SemiBold }
            };
        }

        /// <summary>Four Model Class Score bars in class colours (values in percent).</summary>
        public static StackPanel ScoreBars(double[] percent, double barHeight = 8, bool compact = false)
        {
            var sp = new StackPanel();
            for (int i = 0; i < StudyText.Classes.Length; i++)
            {
                double v = percent != null && i < percent.Length ? percent[i] : 0;
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var val = Text(v.ToString("0.0", CultureInfo.InvariantCulture) + "%", compact ? 11 : 12, true, Ink, new Thickness(0));
                DockPanel.SetDock(val, Dock.Right);
                head.Children.Add(val);
                head.Children.Add(Text(StudyText.Classes[i], compact ? 11 : 12.5, false, Ink, new Thickness(0)));
                sp.Children.Add(head);
                var bar = new ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = 0, Height = barHeight,
                    Foreground = Brush(ClassHex[i]), Background = Brush("#F4F0E5"), BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, compact ? 5 : 9)
                };
                double target = v; int index = i;
                bar.Loaded += (s, e) => bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, target, TimeSpan.FromMilliseconds(650))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(index * 70),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    });
                sp.Children.Add(bar);
            }
            return sp;
        }

        public static Button Btn(string text, RoutedEventHandler click, string style = "SecondaryButton", double? width = null)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(style) };
            if (width.HasValue) b.Width = width.Value;
            b.Click += click;
            return b;
        }

        public static BitmapSource LoadImage(string path, int decodeWidth = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static Border Thumb(string path, double height, string borderHex = "#E3DDCB", string caption = null)
        {
            var src = LoadImage(path, 480);
            UIElement content = src != null
                ? (UIElement)new Image { Source = src, Stretch = Stretch.Uniform }
                : Text(caption ?? "image not available", 11, false, Muted, new Thickness(8));
            return new Border
            {
                Height = height,
                Background = Brush("#2D2E22"),
                BorderBrush = Brush(borderHex),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Child = content
            };
        }

        public static string Pct(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Click-to-enlarge viewer for one saved image.</summary>
    public static class ImagePopup
    {
        public static void Show(string path, string title)
        {
            BitmapSource src = Ui.LoadImage(path);
            if (src == null)
            {
                LumyDialog.Show("This image is not available on disk:\n" + (path ?? "(no file)"), "LUMYVUE");
                return;
            }

            var w = new Window
            {
                Title = title + "  —  " + Path.GetFileName(path),
                Width = 820,
                Height = 860,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                Background = Ui.Brush("#2D2E22"),
                Content = new Image { Source = src, Stretch = Stretch.Uniform, Margin = new Thickness(10) }
            };
            w.Show();
        }
    }

    // ---------------------------------------------------------------- health --

    public sealed class SystemHealth
    {
        public bool CameraOk { get; set; }
        public string CameraText { get; set; } = "";
        public bool ModelOk { get; set; }
        public string ModelText { get; set; } = "";
        public bool GradCamOk { get; set; }
        public string GradCamText { get; set; } = "";
        public GradCamHealth GradCam { get; set; }
        public bool DatabaseOk { get; set; }
        public string DatabaseText { get; set; } = "";
        public bool PoseModelOk { get; set; }

        public static async Task<SystemHealth> CheckAsync()
        {
            var h = new SystemHealth();

            await Task.Run(() =>
            {
                try
                {
                    var cams = CameraDiscovery.List();
                    if (cams.Count == 0)
                    {
                        h.CameraText = "No camera detected";
                    }
                    else
                    {
                        h.CameraOk = true;                      // listed; the Preparation page proves that frames arrive
                        h.CameraText = cams[0].Name + (cams[0].IsBrio ? "" : cams[0].IsVirtual ? " (virtual / infrared only)" : " (Logitech BRIO not found; using this camera)");
                    }
                }
                catch (Exception ex) { h.CameraText = "Check failed: " + ex.Message; }

                try { if (!AiEngine.IsAvailable) AiEngine.EnsureLoaded(); } catch { }
                h.ModelOk = AiEngine.IsAvailable;
                h.ModelText = h.ModelOk ? "Ready" : "Not ready: " + (AiEngine.LoadErrorMessage ?? "the model is not loaded");

                h.PoseModelOk = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "face_detection_yunet_2023mar.onnx"));
            }).ConfigureAwait(true);

            h.GradCam = await GradCamService.GetHealthAsync().ConfigureAwait(true);
            h.GradCamOk = h.GradCam != null && h.GradCam.Ok;
            h.GradCamText = h.GradCamOk
                ? "Running"
                : "Not running. Scans still work, but no heat maps are created";

            h.DatabaseOk = StudyDatabase.IsReady;
            h.DatabaseText = h.DatabaseOk ? "Ready" : "Problem: " + (StudyDatabase.LastError ?? "not set up yet");
            return h;
        }

        /// <summary>Horizontal strip of four status chips.</summary>
        public static WrapPanel Strip(SystemHealth h)
        {
            var w = new WrapPanel();
            w.Children.Add(Chip("Camera", h.CameraOk, h.CameraText));
            w.Children.Add(Chip("Skin model", h.ModelOk, h.ModelText));
            w.Children.Add(Chip("Heat-map service", h.GradCamOk, h.GradCamText));
            w.Children.Add(Chip("Database", h.DatabaseOk, h.DatabaseText));
            return w;
        }

        public static Border Chip(string name, bool ok, string detail)
        {
            string icon = name.StartsWith("Camera") ? "Icon.Camera" : name.StartsWith("Skin") ? "Icon.Chip"
                        : name.StartsWith("Heat") ? "Icon.Flame" : name.StartsWith("Data") ? "Icon.Database" : "Icon.Info";
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            Border badge = Ui.IconBadge(icon, 30, ok ? "#EAF1E4" : "#F8E6E2", ok ? "#4F7A4E" : "#A93D33");
            badge.Margin = new Thickness(0, 0, 10, 0);
            sp.Children.Add(badge);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Foreground = Ui.Brush("#2F3024") });
            words.Children.Add(new TextBlock { Text = ok ? "Ready" : "Needs attention", FontSize = 11.5, Foreground = ok ? Ui.Brush("#4F7A4E") : Ui.Brush("#A93D33") });
            sp.Children.Add(words);
            return new Border
            {
                Background = Ui.Brush("#FFFEFB"),
                BorderBrush = Ui.Brush("#E8E2D3"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(8, 7, 16, 7),
                Margin = new Thickness(0, 0, 10, 10),
                ToolTip = detail,
                Child = sp
            };
        }

        /// <summary>Four shimmering chip placeholders while the system check runs.</summary>
        public static WrapPanel StripLoading()
        {
            var w = new WrapPanel();
            for (int i = 0; i < 4; i++)
            {
                var s = Ui.Skeleton(176, 46, 16);
                s.Margin = new Thickness(0, 0, 10, 10);
                w.Children.Add(s);
            }
            return w;
        }

    }
}
