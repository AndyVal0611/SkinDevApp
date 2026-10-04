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
            MessageBox.Show(what + " is available to signed-in researchers / administrators only.",
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

        public static readonly SolidColorBrush Ink = Brush("#34362E");
        public static readonly SolidColorBrush Bar = Brush("#5F6652");
        public static readonly SolidColorBrush Muted = Brush("#6B6658");
        public static readonly SolidColorBrush Good = Brush("#588157");
        public static readonly SolidColorBrush Warn = Brush("#B7791F");
        public static readonly SolidColorBrush Bad = Brush("#B91C1C");
        public static readonly SolidColorBrush Info = Brush("#7A816B");
        public static readonly SolidColorBrush Line = Brush("#E0D9C8");

        public static readonly string[] ClassHex = { "#E11D1D", "#1D4ED8", "#E8A200", "#16A34A" };   // Acne, Hyper, Eczema, Normal

        public static string ClassColor(string cls)
        {
            int i = Array.IndexOf(StudyText.Classes, cls);
            return i >= 0 ? ClassHex[i] : "#6B7280";
        }

        // ------------------------------------------------------------- header --

        /// <summary>The dark LUMYVUE bar used at the top of every study screen.</summary>
        public static Border Header(string title, bool showHome = true, bool showBack = true)
        {
            var dock = new DockPanel { LastChildFill = true };

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(right, Dock.Right);

            right.Children.Add(new TextBlock
            {
                Text = AppSession.RoleTitle + (string.IsNullOrEmpty(AppSession.UserId) ? "" : " · " + AppSession.UserId),
                Foreground = Brush("#D6CEC6"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            if (showBack) right.Children.Add(HeaderButton("Back", (s, e) => Nav.Back()));
            if (showHome) right.Children.Add(HeaderButton("Dashboard", (s, e) => Nav.Home()));
            right.Children.Add(HeaderButton("Sign out", (s, e) => Nav.SignOut()));
            dock.Children.Add(right);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = "LUMYVUE", Foreground = Brushes.White, FontSize = 24, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontStyle = FontStyles.Italic });
            left.Children.Add(new TextBlock
            {
                Text = "   PrecisionSkin  ·  " + title,
                Foreground = Brush("#D09F7C"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            dock.Children.Add(left);

            return new Border
            {
                Background = Bar,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 14, 24, 12),
                Margin = new Thickness(8, 0, 8, 16),
                Child = BuildHeaderBody(dock)
            };
        }

        // Skin-tone ribbon: the app's one signature detail, six Fitzpatrick-inspired swatches.
        private static UIElement BuildHeaderBody(UIElement dock)
        {
            var ribbon = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var hex in new[] { "#F3DFD0", "#E9C6A8", "#D9A97F", "#BC8559", "#8C5A3C", "#5B3A29" })
                ribbon.Children.Add(new Border { Width = 30, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 4, 0), Background = Brush(hex) });
            var sp = new StackPanel();
            sp.Children.Add(dock);
            sp.Children.Add(ribbon);
            return sp;
        }

        private static Button HeaderButton(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("HeaderButton") };
            b.Click += click;
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

        public static Border Pill(string text, Brush fg, string bgHex = "#F3E4D3")
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
                sp.Children.Add(new ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = v, Height = barHeight,
                    Foreground = Brush(ClassHex[i]), Background = Brush("#F3F1EE"), BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, compact ? 5 : 9)
                });
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

        public static Border Thumb(string path, double height, string borderHex = "#E0D9C8", string caption = null)
        {
            var src = LoadImage(path, 480);
            UIElement content = src != null
                ? (UIElement)new Image { Source = src, Stretch = Stretch.Uniform }
                : Text(caption ?? "image not available", 11, false, Muted, new Thickness(8));
            return new Border
            {
                Height = height,
                Background = Brush("#2B2C26"),
                BorderBrush = Brush(borderHex),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Child = content
            };
        }

        /// <summary>Card shown wherever a skin-tone result will appear once a validated model exists.</summary>
        public static Border FitzpatrickReservedCard(string manualType, string manualSource)
        {
            var sp = new StackPanel();
            sp.Children.Add(Text("Fitzpatrick / Skin-Tone Assessment", 15, true));
            sp.Children.Add(KeyValues(new[]
            {
                KV("AI assessment", StudyText.FitzpatrickAiStatus + " — pending model development"),
                KV("Manual / self-reported", string.IsNullOrWhiteSpace(manualType) ? "Not collected" :
                    manualType + (string.IsNullOrWhiteSpace(manualSource) ? "" : " (" + manualSource + ")")),
                KV("Model version", "N/A")
            }));
            sp.Children.Add(Text(StudyText.FitzpatrickAiDetail, 11, false, Muted, new Thickness(0, 6, 0, 0)));
            var b = Card(sp);
            b.Background = Brush("#F3E4D3");
            return b;
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
                MessageBox.Show("This image is not available on disk:\n" + (path ?? "(no file)"), "LUMYVUE");
                return;
            }

            var w = new Window
            {
                Title = title + "  —  " + Path.GetFileName(path),
                Width = 820,
                Height = 860,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                Background = Ui.Brush("#2B2C26"),
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
                    var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                    if (devices.Count == 0)
                    {
                        h.CameraText = "No camera detected";
                    }
                    else
                    {
                        string brio = null;
                        for (int i = 0; i < devices.Count; i++)
                            if ((devices[i].Name ?? "").ToLowerInvariant().Contains("brio")) brio = devices[i].Name;
                        h.CameraOk = true;
                        h.CameraText = brio ?? devices[0].Name + " (Logitech BRIO not found)";
                    }
                }
                catch (Exception ex) { h.CameraText = "Check failed: " + ex.Message; }

                try { if (!AiEngine.IsAvailable) AiEngine.EnsureLoaded(); } catch { }
                h.ModelOk = AiEngine.IsAvailable;
                h.ModelText = h.ModelOk ? "Loaded (precisionskin.onnx)" : "Unavailable: " + (AiEngine.LoadErrorMessage ?? "not loaded");

                h.PoseModelOk = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "face_detection_yunet_2023mar.onnx"));
            }).ConfigureAwait(true);

            h.GradCam = await GradCamService.GetHealthAsync().ConfigureAwait(true);
            h.GradCamOk = h.GradCam != null && h.GradCam.Ok;
            h.GradCamText = h.GradCamOk
                ? "Available (v" + h.GradCam.ServiceVersion + ", layer " + (h.GradCam.TargetLayer ?? "?") + ")"
                : "Unavailable — classification still works, heatmaps will be missing";

            h.DatabaseOk = StudyDatabase.IsReady;
            h.DatabaseText = h.DatabaseOk ? "Ready" : "Error: " + (StudyDatabase.LastError ?? "not initialised");
            return h;
        }

        /// <summary>Horizontal strip of four status chips.</summary>
        public static WrapPanel Strip(SystemHealth h)
        {
            var w = new WrapPanel();
            w.Children.Add(Chip("Camera", h.CameraOk, h.CameraText));
            w.Children.Add(Chip("AI model", h.ModelOk, h.ModelText));
            w.Children.Add(Chip("Grad-CAM++ service", h.GradCamOk, h.GradCamText));
            w.Children.Add(Chip("Database", h.DatabaseOk, h.DatabaseText));
            return w;
        }

        public static Border Chip(string name, bool ok, string detail)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 10, Height = 10, Fill = ok ? Ui.Good : Ui.Bad,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = name + ": ", FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Ui.Ink });
            sp.Children.Add(new TextBlock { Text = ok ? "Ready" : "Check", FontSize = 12, Foreground = ok ? Ui.Good : Ui.Bad, FontWeight = FontWeights.Bold });
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = detail,
                Child = sp
            };
        }
    }
}
