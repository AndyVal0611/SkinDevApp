// ============================================================================
// ComparisonGalleryWindow.cs  (NEW)  -  namespace SkinDevApp.Views
//
// Researcher comparison gallery for one scan.
//
//   [Overall] [Front] [Left side] [Right side]          (single capture: one tab)
//
//   Each view tab:  Original | Acne CAM | Hyperpigmentation CAM | Eczema CAM | Normal CAM
//   with that view's own class score and an honest attribution note under each image.
//   Click any image to enlarge it, compare it with the original (opacity slider or
//   side by side) and zoom.
//
//   The Overall tab shows the multi-view fusion AND each view's own result, so one
//   side that differs from the others stays visible. The three photographs are never
//   merged into one heatmap.
//
// Built in code (no XAML) so there is nothing to keep in sync. Everything here is
// labelled as Grad-CAM++ attribution, not lesion segmentation.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkinDevApp.Imaging;
using SkinDevApp.Scanning;

namespace SkinDevApp.Views
{
    internal static class GalleryUi
    {
        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static BitmapSource LoadBitmap(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        /// <summary>
        /// Class-coloured overlay from the saved 8-bit per-class heatmap:
        /// alpha = heat^gamma (the same shaping as the saved overlay files), colour = class colour.
        /// Shown with Image.Opacity so the researcher can fade it in and out.
        /// </summary>
        public static BitmapSource BuildOverlay(string heatmapPath, string colorHex)
        {
            BitmapSource heat = LoadBitmap(heatmapPath);
            if (heat == null) return null;

            var gray = new FormatConvertedBitmap(heat, PixelFormats.Gray8, null, 0);
            int w = gray.PixelWidth, h = gray.PixelHeight;
            var g = new byte[w * h];
            gray.CopyPixels(g, w, 0);

            Color c = (Color)ColorConverter.ConvertFromString(colorHex);
            var px = new byte[w * h * 4];
            for (int i = 0; i < g.Length; i++)
            {
                double a = Math.Pow(g[i] / 255.0, ClassPalette.Gamma);
                px[4 * i + 0] = (byte)Math.Round(c.B * a);
                px[4 * i + 1] = (byte)Math.Round(c.G * a);
                px[4 * i + 2] = (byte)Math.Round(c.R * a);
                px[4 * i + 3] = (byte)Math.Round(255.0 * a);
            }

            BitmapSource bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            bs.Freeze();
            return bs;
        }

        public static TextBlock Text(string text, double size = 12, bool bold = false, string color = "#34362E", Thickness? margin = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Brush(color),
                TextWrapping = TextWrapping.Wrap,
                Margin = margin ?? new Thickness(0)
            };
        }
    }

    public sealed class ComparisonGalleryWindow : Window
    {
        private readonly GalleryModel _model;
        private readonly string _openFolder;

        public ComparisonGalleryWindow(string folder)
        {
            _model = GalleryModel.Load(folder);
            _openFolder = _model.SessionFolder ?? folder;

            Title = "LUMYVUE - Comparison Gallery (Researcher)";
            Width = 1380;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = GalleryUi.Brush("#F8F1E6");

            Content = BuildRoot();
        }

        private UIElement BuildRoot()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // header
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(GalleryUi.Text(_model.Title, 17, true));
            header.Children.Add(new Border
            {
                Background = GalleryUi.Brush("#FFF7E0"),
                BorderBrush = GalleryUi.Brush("#E0B84A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 6, 0, 0),
                Child = GalleryUi.Text(GalleryModel.AttributionStatement, 12, false, "#6B4A00")
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // tabs
            var tabs = new TabControl();
            if (_model.IsSession) tabs.Items.Add(new TabItem { Header = "Overall", Content = BuildOverallTab() });
            foreach (GalleryView v in _model.Views)
                tabs.Items.Add(new TabItem { Header = v.TabTitle + (v.Available ? "" : " (not captured)"), Content = BuildViewTab(v) });
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            // footer
            var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            var openBtn = new Button { Content = "Open folder", Width = 110, Height = 32 };
            openBtn.Click += (s, e) =>
            {
                try { Process.Start("explorer.exe", _openFolder); } catch { }
            };
            var closeBtn = new Button { Content = "Close", Width = 90, Height = 32 };
            closeBtn.Click += (s, e) => Close();
            DockPanel.SetDock(closeBtn, Dock.Right);
            footer.Children.Add(openBtn);
            footer.Children.Add(closeBtn);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        // ------------------------------------------------------------ a view ---

        private UIElement BuildViewTab(GalleryView v)
        {
            var sp = new StackPanel { Margin = new Thickness(10) };

            if (!v.Available)
            {
                sp.Children.Add(GalleryUi.Text(v.SummaryLine, 14, true, "#6B6658"));
                return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            sp.Children.Add(GalleryUi.Text(v.SummaryLine, 14, true, "#34362E", new Thickness(0, 0, 0, 8)));

            var wrap = new WrapPanel();
            foreach (GalleryCard c in v.Cards) wrap.Children.Add(BuildCard(v, c));
            sp.Children.Add(wrap);

            if (!string.IsNullOrEmpty(v.LocalizationText))
            {
                sp.Children.Add(GalleryUi.Text("Lesion localization (separate detector, independent of Grad-CAM++)", 13, true, "#34362E", new Thickness(0, 6, 0, 4)));
                sp.Children.Add(GalleryUi.Text(v.LocalizationText, 12, false, "#3D3732", new Thickness(0, 0, 0, 6)));
                var lw = new WrapPanel();
                if (v.LocalizationCard != null) lw.Children.Add(BuildLocalizationCard(v.LocalizationCard));
                if (v.CombinedCard != null) lw.Children.Add(BuildLocalizationCard(v.CombinedCard));
                sp.Children.Add(lw);
                sp.Children.Add(GalleryUi.Text("Boxes are candidate lesion locations from a research detector: not segmentation, not a diagnosis, not a lesion count; lesions can be missed and boxes can be wrong. Eczema is a pilot class.",
                    11, false, "#6B6658", new Thickness(0, 4, 0, 0)));
            }

            if (!string.IsNullOrEmpty(v.SimilarityText))
                sp.Children.Add(new Border
                {
                    Background = GalleryUi.Brush(v.SimilarityHigh ? "#FFF7E0" : "#F3E4D3"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 8, 0, 0),
                    Child = GalleryUi.Text(v.SimilarityText, 12, false, v.SimilarityHigh ? "#6B4A00" : "#3D3732")
                });

            var details = new StringBuilder();
            if (!string.IsNullOrEmpty(v.PoseText)) details.AppendLine("Pose: " + v.PoseText);
            if (!string.IsNullOrEmpty(v.QualityText)) details.AppendLine("Quality: " + v.QualityText);
            if (!string.IsNullOrEmpty(v.StabilityText)) details.AppendLine("Stability: " + v.StabilityText + "  (steady prediction, not correctness)");
            if (v.Record != null && v.Record.Model != null)
                details.AppendLine("Model run: " + v.Record.Model.RunName + "   service " + v.Record.Model.ServiceVersionReported);
            details.AppendLine("Files: " + v.Folder);
            sp.Children.Add(new TextBox
            {
                Text = details.ToString().TrimEnd(),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildLocalizationCard(GalleryCard c)
        {
            var stack = new StackPanel { Width = 236 };
            var img = new Image
            {
                Source = GalleryUi.LoadBitmap(c.ImagePath),
                Stretch = Stretch.Uniform,
                Height = 236,
                Cursor = Cursors.Hand,
                ToolTip = "Click to enlarge"
            };
            img.MouseLeftButtonUp += (s, e) => SkinDevApp.Views.ImagePopup.Show(c.ImagePath, c.Title);
            stack.Children.Add(new Border { Background = GalleryUi.Brush("#2B2C26"), CornerRadius = new CornerRadius(4), Child = img });
            stack.Children.Add(GalleryUi.Text(c.Title, 13, true, "#34362E", new Thickness(0, 6, 0, 0)));
            stack.Children.Add(GalleryUi.Text(c.ScoreText, 12, false, "#34362E", new Thickness(0, 2, 0, 0)));
            if (!string.IsNullOrEmpty(c.Note))
                stack.Children.Add(GalleryUi.Text(c.Note, 11, false, "#5A524B", new Thickness(0, 2, 0, 0)));
            return new Border
            {
                BorderBrush = GalleryUi.Brush(c.ColorHex),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 10, 10),
                Background = Brushes.White,
                Child = stack
            };
        }

        private UIElement BuildCard(GalleryView v, GalleryCard c)
        {
            var stack = new StackPanel { Width = 236 };

            var img = new Image
            {
                Source = GalleryUi.LoadBitmap(c.ImagePath),
                Stretch = Stretch.Uniform,
                Height = 236,
                Cursor = Cursors.Hand,
                ToolTip = "Click to enlarge / compare with the original"
            };
            int startClass = c.IsOriginal ? -1 : c.ClassIndex;
            img.MouseLeftButtonUp += (s, e) =>
            {
                var w = new ImageCompareWindow(v, startClass) { Owner = this };
                w.Show();
            };

            var imgHost = new Border { Background = GalleryUi.Brush("#2B2C26"), CornerRadius = new CornerRadius(4), Child = img };
            stack.Children.Add(imgHost);

            stack.Children.Add(GalleryUi.Text(c.Title, 13, true, "#34362E", new Thickness(0, 6, 0, 0)));
            stack.Children.Add(GalleryUi.Text(c.ScoreText, 12, c.IsPredicted, "#34362E", new Thickness(0, 2, 0, 0)));
            if (!string.IsNullOrEmpty(c.Note))
                stack.Children.Add(GalleryUi.Text(c.Note, 11, false, c.Diffuse ? "#8A5A00" : "#5A524B", new Thickness(0, 2, 0, 0)));

            return new Border
            {
                BorderBrush = GalleryUi.Brush(c.ColorHex),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 10, 10),
                Background = Brushes.White,
                Child = stack
            };
        }

        // ----------------------------------------------------------- overall ---

        private UIElement BuildOverallTab()
        {
            var sp = new StackPanel { Margin = new Thickness(10) };
            FusedResult f = _model.Fusion;
            SessionIndex s = _model.Session;

            sp.Children.Add(GalleryUi.Text("Overall multi-view result", 16, true));
            sp.Children.Add(GalleryUi.Text(
                "Each angle keeps its own scores and class maps (see the Front / Left / Right tabs). " +
                "The three photographs are never merged into one heatmap.", 12, false, "#5A524B", new Thickness(0, 2, 0, 10)));

            if (f == null || f.ViewsUsed == 0)
            {
                sp.Children.Add(GalleryUi.Text(
                    "The overall result is not available yet" + (s != null ? " (scan status: " + s.Status + ")" : "") + ".", 13, true, "#8A5A00"));
            }
            else
            {
                sp.Children.Add(GalleryUi.Text(
                    "Overall top class: " + f.TopClass + " (" + f.TopClassPercent.ToString("0.0") + "% pooled score, " +
                    f.ViewsUsed + " of " + f.ViewsExpected + " views)", 15, true));

                if (f.Disagreement || f.SideSpecific.Count > 0 || f.Notes.Count > 0)
                {
                    var notes = new StringBuilder();
                    foreach (string n in f.Notes) notes.AppendLine("• " + n);
                    sp.Children.Add(new Border
                    {
                        Background = GalleryUi.Brush(f.Disagreement ? "#FFF1F0" : "#FFF7E0"),
                        BorderBrush = GalleryUi.Brush(f.Disagreement ? "#E57373" : "#E0B84A"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 8, 0, 0),
                        Child = GalleryUi.Text(notes.ToString().TrimEnd(), 12, false, "#5B2B00")
                    });
                }

                var tbl = new StringBuilder();
                tbl.AppendLine("Class".PadRight(20) + "Pooled".PadLeft(9) + "Mean".PadLeft(9) + "Max".PadLeft(9) + "  from");
                foreach (string cls in ClassPalette.Names)
                {
                    tbl.AppendLine(cls.PadRight(20)
                        + (f.PooledPercent.ContainsKey(cls) ? f.PooledPercent[cls].ToString("0.0") : "-").PadLeft(8) + "%"
                        + (f.MeanPercent.ContainsKey(cls) ? f.MeanPercent[cls].ToString("0.0") : "-").PadLeft(8) + "%"
                        + (f.MaxPercent.ContainsKey(cls) ? f.MaxPercent[cls].ToString("0.0") : "-").PadLeft(8) + "%"
                        + "  " + (f.MaxFromView.ContainsKey(cls) ? f.MaxFromView[cls] : ""));
                }
                sp.Children.Add(new TextBox
                {
                    Text = tbl.ToString().TrimEnd(),
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                sp.Children.Add(GalleryUi.Text(
                    "Pooled = quality-weighted average over views. Mean = unweighted average. Max = highest single-view score " +
                    "(a one-sided finding shows here even when the pooled score is low).", 11, false, "#6B6658", new Thickness(0, 2, 0, 0)));
            }

            // per-view table (always shown)
            sp.Children.Add(GalleryUi.Text("Per view", 14, true, "#34362E", new Thickness(0, 14, 0, 4)));
            var pv = new StringBuilder();
            pv.AppendLine("View".PadRight(8) + "Top class".PadRight(20) + "Score".PadLeft(8) + "  Weight".PadRight(10) + "Pose");
            if (s != null)
                foreach (ViewOutcome o in s.Views)
                    pv.AppendLine(o.View.PadRight(8)
                        + (o.PredictedClass ?? "-").PadRight(20)
                        + (o.ConfidencePercent.ToString("0.0") + "%").PadLeft(8)
                        + ("  " + o.QualityWeight.ToString("0.00")).PadRight(10)
                        + (o.PoseGateVerified ? "verified" : "not verified"));
            foreach (GalleryView gv in _model.Views.Where(x => !x.Available))
                pv.AppendLine(gv.TabTitle.PadRight(8) + "not captured");
            sp.Children.Add(new TextBox
            {
                Text = pv.ToString().TrimEnd(),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            });

            if (f != null)
                sp.Children.Add(GalleryUi.Text("Method: " + f.Method, 11, false, "#6B6658", new Thickness(0, 10, 0, 0)));

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }

    // ================================================================ enlarge ===

    /// <summary>
    /// Large view of one capture angle: the original with a class overlay (opacity slider)
    /// or the two side by side, with zoom. Same frame, same geometry, so the researcher can
    /// check exactly which facial features a map corresponds to.
    /// </summary>
    public sealed class ImageCompareWindow : Window
    {
        private readonly GalleryView _view;
        private readonly BitmapSource _original;
        private readonly ComboBox _classBox = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 14, 0) };
        private readonly ComboBox _modeBox = new ComboBox { Width = 210, Margin = new Thickness(0, 0, 14, 0) };
        private readonly Slider _opacity = new Slider { Minimum = 0, Maximum = 1, Value = 0.6, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        private readonly Slider _zoom = new Slider { Minimum = 0.5, Maximum = 3, Value = 1, Width = 120, VerticalAlignment = VerticalAlignment.Center };
        private readonly ScrollViewer _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = GalleryUi.Brush("#2B2C26")
        };
        private readonly TextBlock _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        private bool _ready;

        private readonly List<GalleryCard> _classCards;

        public ImageCompareWindow(GalleryView view, int startClassIndex)
        {
            _view = view;
            _classCards = view.Cards.Where(c => !c.IsOriginal).ToList();
            GalleryCard orig = view.Cards.FirstOrDefault(c => c.IsOriginal);
            _original = orig != null ? GalleryUi.LoadBitmap(orig.ImagePath) : null;

            Title = "LUMYVUE - " + view.TabTitle + ": original vs Grad-CAM++ attribution";
            Width = 1100;
            Height = 900;
            Background = GalleryUi.Brush("#F8F1E6");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            foreach (GalleryCard c in _classCards) _classBox.Items.Add(c.Title);
            int start = 0;
            if (startClassIndex >= 0)
            {
                int i = _classCards.FindIndex(c => c.ClassIndex == startClassIndex);
                if (i >= 0) start = i;
            }
            _classBox.SelectedIndex = _classCards.Count > 0 ? start : -1;

            _modeBox.Items.Add("Overlay with opacity slider");
            _modeBox.Items.Add("Side by side");
            _modeBox.SelectedIndex = startClassIndex >= 0 ? 0 : 1;      // opened from the Original card: show side by side

            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(GalleryUi.Text("Class ", 12, true, "#34362E", new Thickness(0, 4, 4, 0)));
            bar.Children.Add(_classBox);
            bar.Children.Add(GalleryUi.Text("Mode ", 12, true, "#34362E", new Thickness(0, 4, 4, 0)));
            bar.Children.Add(_modeBox);
            bar.Children.Add(GalleryUi.Text("Heatmap opacity ", 12, true, "#34362E", new Thickness(0, 4, 4, 0)));
            bar.Children.Add(_opacity);
            bar.Children.Add(GalleryUi.Text("   Zoom ", 12, true, "#34362E", new Thickness(0, 4, 4, 0)));
            bar.Children.Add(_zoom);

            var banner = GalleryUi.Text(GalleryModel.AttributionStatement, 11, false, "#6B4A00");

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new StackPanel();
            top.Children.Add(bar);
            top.Children.Add(banner);
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            Grid.SetRow(_scroll, 1);
            _scroll.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_scroll);

            Grid.SetRow(_info, 2);
            root.Children.Add(_info);

            Content = root;

            _classBox.SelectionChanged += (s, e) => Refresh();
            _modeBox.SelectionChanged += (s, e) => Refresh();
            _opacity.ValueChanged += (s, e) => ApplyOpacity();
            _zoom.ValueChanged += (s, e) => Refresh();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };

            _ready = true;
            Refresh();
        }

        private Image _overlayImage;

        private void ApplyOpacity()
        {
            if (_overlayImage != null) _overlayImage.Opacity = _opacity.Value;
        }

        private GalleryCard SelectedCard()
        {
            int i = _classBox.SelectedIndex;
            return i >= 0 && i < _classCards.Count ? _classCards[i] : null;
        }

        private Grid MakeStage(double w, double h, BitmapSource src)
        {
            var g = new Grid { Width = w, Height = h, Margin = new Thickness(6) };
            g.Children.Add(new Image { Source = src, Stretch = Stretch.Fill });
            return g;
        }

        private void Refresh()
        {
            if (!_ready) return;

            GalleryCard card = SelectedCard();
            _overlayImage = null;

            if (_original == null)
            {
                _scroll.Content = GalleryUi.Text("The original image file could not be loaded.", 13, true, "#FFFFFF", new Thickness(10));
                return;
            }

            double pw = _original.PixelWidth, ph = _original.PixelHeight;
            double fit = Math.Min(Math.Max(300.0, ActualWidth > 0 ? ActualWidth - 60 : 1000.0) / pw,
                                  Math.Max(300.0, ActualHeight > 0 ? ActualHeight - 230 : 640.0) / ph);
            if (fit > 2.0) fit = 2.0;
            double scale = fit * _zoom.Value;
            double w = pw * scale, h = ph * scale;

            BitmapSource overlay = card != null ? GalleryUi.BuildOverlay(card.HeatmapPath, card.ColorHex) : null;
            bool sideBySide = _modeBox.SelectedIndex == 1;

            if (sideBySide)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var left = new StackPanel();
                left.Children.Add(GalleryUi.Text("Original", 12, true, "#FFFFFF", new Thickness(6, 4, 0, 0)));
                left.Children.Add(MakeStage(w, h, _original));
                row.Children.Add(left);

                var right = new StackPanel();
                right.Children.Add(GalleryUi.Text(card != null ? card.Title : "-", 12, true, "#FFFFFF", new Thickness(6, 4, 0, 0)));
                Grid st = MakeStage(w, h, _original);
                if (overlay != null)
                {
                    _overlayImage = new Image { Source = overlay, Stretch = Stretch.Fill, Opacity = _opacity.Value };
                    st.Children.Add(_overlayImage);
                }
                right.Children.Add(st);
                row.Children.Add(right);
                _scroll.Content = row;
            }
            else
            {
                Grid st = MakeStage(w, h, _original);
                if (overlay != null)
                {
                    _overlayImage = new Image { Source = overlay, Stretch = Stretch.Fill, Opacity = _opacity.Value };
                    st.Children.Add(_overlayImage);
                }
                _scroll.Content = st;
            }

            if (card == null)
                _info.Text = "";
            else if (overlay == null)
                _info.Text = card.Title + ": no heatmap was saved for this class.";
            else
                _info.Text = card.Title + "   " + card.ScoreText + "   " + card.Note +
                             "   (Colour = this class; the map is normalised per class, so compare its shape, not its brightness, with other classes.)";
        }
    }
}
