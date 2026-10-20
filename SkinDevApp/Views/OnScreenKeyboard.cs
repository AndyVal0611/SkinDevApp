// ============================================================================
// OnScreenKeyboard.cs  -  namespace SkinDevApp.Views
//
// LUMYVUE's own on-screen keyboard for the touchscreen (the mini PC has no physical keyboard, and the Windows touch keyboard
// does not open by itself in a desktop app).
//
//   * It slides up from the bottom of the screen when a text box or password box is TAPPED. A mouse click or a physical
//     keyboard never opens it, so working on a laptop is unchanged.
//   * The keys send real key presses (Windows SendInput), so typing behaves exactly as with a real keyboard: password boxes,
//     Enter to sign in, Tab to the next field and Backspace all work.
//   * The keys never take the focus, so the cursor stays in the field being typed into.
//   * In the main window the page moves up so the field is not hidden behind the keyboard.
//
// OnScreenKeyboard.Install() is called once at start-up (App.xaml.cs).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SkinDevApp.Data;

namespace SkinDevApp.Views
{
    public static class OnScreenKeyboard
    {
        private static Popup _popup;
        private static KeyboardPanel _panel;
        private static DateTime _lastTouch = DateTime.MinValue;
        private static DispatcherTimer _hideTimer;
        private static bool _installed;

        /// <summary>True while the keyboard is on screen.</summary>
        public static bool IsOpen => _popup != null && _popup.IsOpen;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            // remember when the screen was last touched (a tap on a field opens the keyboard; a mouse click does not)
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((s, e) => OnTouched(e.OriginalSource)), true);
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewStylusDownEvent, new StylusDownEventHandler((s, e) => OnTouched(e.OriginalSource)), true);
            // "Always show" (Settings): a click on a field that already has the cursor opens the keyboard again after it was hidden
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (!AlwaysShow || IsOpen) return;
                var field = FindTextInput(e.OriginalSource as DependencyObject);
                if (field != null && field.IsKeyboardFocusWithin) field.Dispatcher.BeginInvoke(new Action(() => Show(field)), DispatcherPriority.Input);
            }), true);

            foreach (Type t in new[] { typeof(TextBox), typeof(PasswordBox) })
            {
                EventManager.RegisterClassHandler(t, UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocus), true);
                EventManager.RegisterClassHandler(t, UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocus), true);
            }

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                if (!IsTextInput(Keyboard.FocusedElement as DependencyObject)) Hide();
            };
        }

        // ------------------------------------------------------------------ when to show --

        private static void OnTouched(object source)
        {
            _lastTouch = DateTime.UtcNow;
            // a tap on a field that already has the cursor (for example after the keyboard was hidden) opens it again
            var field = FindTextInput(source as DependencyObject);
            if (field != null && field.IsKeyboardFocusWithin && !IsOpen)
                field.Dispatcher.BeginInvoke(new Action(() => Show(field)), DispatcherPriority.Input);
        }

        private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var field = sender as FrameworkElement;
            if (field == null || !IsTextInput(field)) return;
            _hideTimer.Stop();
            bool byTouch = (DateTime.UtcNow - _lastTouch).TotalMilliseconds < 1500;
            if (byTouch || IsOpen || AlwaysShow) Show(field);          // moving between fields keeps it open
        }

        private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsOpen) return;
            if (IsTextInput(e.NewFocus as DependencyObject)) return;
            _hideTimer.Stop();
            _hideTimer.Start();                           // a short delay avoids a flicker when focus passes between fields
        }

        /// <summary>Settings > Camera > "Always show the on-screen keyboard".</summary>
        private static bool AlwaysShow
        {
            get { try { return ScanSettings.Current.AlwaysShowKeyboard; } catch { return false; } }
        }

        private static bool IsTextInput(DependencyObject d)
        {
            if (d is PasswordBox) return ((PasswordBox)d).IsEnabled;
            var tb = d as TextBox;
            return tb != null && tb.IsEnabled && !tb.IsReadOnly;
        }

        private static FrameworkElement FindTextInput(DependencyObject d)
        {
            for (int i = 0; d != null && i < 30; i++)
            {
                if (IsTextInput(d)) return (FrameworkElement)d;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        // ------------------------------------------------------------------ show / hide --

        public static void Show(FrameworkElement field)
        {
            Window owner = Window.GetWindow(field);
            if (owner == null) return;

            if (_popup == null)
            {
                _panel = new KeyboardPanel();
                _popup = new Popup
                {
                    Child = _panel, AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Absolute,
                    PopupAnimation = PopupAnimation.None, Focusable = false
                };
            }

            Rect area = SystemParameters.WorkArea;
            double width = Math.Min(area.Width - 24, 1180);
            _panel.Width = width;
            _panel.Measure(new Size(width, double.PositiveInfinity));
            double height = _panel.DesiredSize.Height;

            if (_popup.IsOpen && _popup.PlacementTarget != owner) _popup.IsOpen = false;   // focus moved to another window
            bool reopening = _popup.IsOpen;
            _popup.PlacementTarget = owner;
            _popup.HorizontalOffset = area.Left + (area.Width - width) / 2;
            _popup.VerticalOffset = area.Bottom - height;
            if (!reopening)
            {
                _popup.IsOpen = true;
                _panel.SlideIn();
                owner.Closed -= OwnerClosed;
                owner.Closed += OwnerClosed;
            }

            // in the main window the page moves up, so the field stays visible above the keyboard
            var main = owner as MainWindow;
            if (main != null)
            {
                double windowBottom = main.WindowState == WindowState.Maximized ? area.Bottom : main.Top + main.ActualHeight;
                main.SetKeyboardInset(Math.Max(0, height - Math.Max(0, area.Bottom - windowBottom)));
            }
            field.Dispatcher.BeginInvoke(new Action(() => field.BringIntoView()), DispatcherPriority.Loaded);
        }

        private static void OwnerClosed(object sender, EventArgs e) => Hide();

        public static void Hide()
        {
            if (_popup == null || !_popup.IsOpen) return;
            var main = _popup.PlacementTarget as MainWindow ?? Application.Current?.MainWindow as MainWindow;
            _popup.IsOpen = false;
            if (main != null) main.SetKeyboardInset(0);
        }

        // ------------------------------------------------------------------ typing --

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
        internal const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_LEFT = 0x25, VK_RIGHT = 0x27;

        /// <summary>Types one character into the field that has the cursor.</summary>
        internal static void TypeChar(char c)
        {
            var down = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } } };
            var up = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Presses a special key (Backspace, Enter, Tab, arrows).</summary>
        internal static void PressKey(ushort vk)
        {
            var down = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } };
            var up = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } };
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    /// <summary>The keys themselves: letters, numbers, common symbols, Shift, Backspace, Space, Enter, Tab (Next) and Hide.</summary>
    internal sealed class KeyboardPanel : Border
    {
        private const double KeyH = 58, Gap = 7;
        private readonly List<Tuple<Button, string, string>> _charKeys = new List<Tuple<Button, string, string>>();   // key, normal, shifted
        private readonly List<Border> _shiftFaces = new List<Border>();
        private bool _shift;

        private static readonly string[][] Rows =
        {
            new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-" },
            new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "@" },
            new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", "." },
            new[] { "z", "x", "c", "v", "b", "n", "m", ",", "_", "/" }
        };
        private static readonly Dictionary<string, string> Shifted = new Dictionary<string, string>
        {
            { "1", "!" }, { "2", "#" }, { "3", "$" }, { "4", "%" }, { "5", "&" }, { "6", "*" }, { "7", "(" }, { "8", ")" }, { "9", "'" }, { "0", "\"" },
            { "-", "+" }, { "@", "?" }, { ".", ":" }, { ",", ";" }, { "_", "=" }, { "/", "\\" }
        };

        public KeyboardPanel()
        {
            Background = (Brush)Application.Current.FindResource("HeaderGradient");
            CornerRadius = new CornerRadius(26, 26, 0, 0);
            Padding = new Thickness(18, 12, 18, 18);
            BorderBrush = Ui.Brush("#33FFFFFF");
            BorderThickness = new Thickness(1, 1, 1, 0);
            SnapsToDevicePixels = true;
            Focusable = false;

            var stack = new StackPanel();
            // grab handle + title + hide
            var top = new DockPanel { Margin = new Thickness(4, 0, 0, 10) };
            var hide = Key(Ui.IconLabel("Icon.ChevronRight", "Hide keyboard", 16), "#26FFFFFF", "#F7F5E9", 0, () => OnScreenKeyboard.Hide());
            ((FrameworkElement)((StackPanel)((Border)hide.Content).Child).Children[0]).LayoutTransform = new RotateTransform(90);
            hide.Height = 44;
            hide.Width = 190;
            DockPanel.SetDock(hide, Dock.Right);
            top.Children.Add(hide);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(new TextBlock { Text = "LUMYVUE", FontSize = 18, FontStyle = FontStyles.Italic, Foreground = Brushes.White, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), VerticalAlignment = VerticalAlignment.Center });
            brand.Children.Add(new TextBlock { Text = "   keyboard", FontSize = 13, Foreground = Ui.Brush("#DDDCC2"), VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(brand);
            stack.Children.Add(top);

            // the four character rows; Backspace ends the first row, Enter the third, Shift starts and ends the fourth
            for (int r = 0; r < Rows.Length; r++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, Gap) };
                var items = new List<Button>();
                if (r == 2) items.Add(Spacer(0.5));
                if (r == 3) items.Add(ShiftKey(1.5));
                foreach (string k in Rows[r])
                {
                    string shifted = Shifted.ContainsKey(k) ? Shifted[k] : k.ToUpperInvariant();
                    string label = k;
                    Button b = Key(label, "#FFFEFB", "#2F3024", 1, null);
                    string normal = k;
                    b.Click += (s, e) => TypeKey(normal, shifted);
                    _charKeys.Add(Tuple.Create(b, normal, shifted));
                    items.Add(b);
                }
                if (r == 0) items.Add(Key(Ui.IconLabel("Icon.Backspace", "Delete", 18), "#DDBAAE", "#2F3024", 1.6, () => OnScreenKeyboard.PressKey(OnScreenKeyboard.VK_BACK)));
                if (r == 1) items.Add(Spacer(0.01));
                if (r == 2) items.Add(Key("Enter", "#C2C395", "#2F3024", 1.9, () => OnScreenKeyboard.PressKey(OnScreenKeyboard.VK_RETURN)));
                if (r == 3) items.Add(ShiftKey(1.5));
                Lay(row, items);
                stack.Children.Add(row);
            }

            // bottom row: arrows, space, next field
            var bottom = new Grid();
            Lay(bottom, new List<Button>
            {
                Key(Ui.Icon("Icon.ChevronLeft", 20), "#40FFFFFF", "#F7F5E9", 1.1, () => OnScreenKeyboard.PressKey(OnScreenKeyboard.VK_LEFT)),
                Key(Ui.Icon("Icon.ChevronRight", 20), "#40FFFFFF", "#F7F5E9", 1.1, () => OnScreenKeyboard.PressKey(OnScreenKeyboard.VK_RIGHT)),
                Key("space", "#FFFEFB", "#6F6F58", 6.2, () => OnScreenKeyboard.TypeChar(' ')),
                Key(Ui.IconLabel("Icon.ChevronRight", "Next field", 18), "#40FFFFFF", "#F7F5E9", 2.2, () => OnScreenKeyboard.PressKey(OnScreenKeyboard.VK_TAB))
            });
            stack.Children.Add(bottom);
            Child = stack;
        }

        private void TypeKey(string normal, string shifted)
        {
            string s = _shift ? shifted : normal;
            foreach (char c in s) OnScreenKeyboard.TypeChar(c);
            if (_shift) SetShift(false);                  // Shift applies to one key, like a phone keyboard
        }

        private void SetShift(bool on)
        {
            _shift = on;
            foreach (Tuple<Button, string, string> k in _charKeys)
                ((TextBlock)((Border)k.Item1.Content).Child).Text = on ? k.Item3 : k.Item2;
            foreach (Border f in _shiftFaces)
            {
                f.Background = Ui.Brush(on ? "#F7F5E9" : "#40FFFFFF");
                f.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Ui.Brush(on ? "#2F3024" : "#F7F5E9"));   // dark label on the light "on" key
            }
        }

        private Button ShiftKey(double units)
        {
            Button b = Key(Ui.IconLabel("Icon.Shift", "Shift", 17), "#40FFFFFF", "#F7F5E9", units, null);
            b.Click += (s, e) => SetShift(!_shift);
            _shiftFaces.Add((Border)b.Content);
            return b;
        }

        private static Button Spacer(double units)
        {
            var b = new Button { Visibility = Visibility.Hidden, Tag = units, Focusable = false, IsTabStop = false };
            return b;
        }

        private static void Lay(Grid row, List<Button> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                double units = items[i].Tag is double ? (double)items[i].Tag : 1;
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(units, GridUnitType.Star) });
                Grid.SetColumn(items[i], i);
                items[i].Margin = new Thickness(i == 0 ? 0 : Gap / 2, 0, i == items.Count - 1 ? 0 : Gap / 2, 0);
                row.Children.Add(items[i]);
            }
        }

        /// <summary>One key: a rounded face that darkens and shrinks slightly while pressed. It never takes the focus.</summary>
        private static Button Key(object content, string face, string ink, double units, Action action)
        {
            UIElement inner = content as UIElement ?? new TextBlock
            {
                Text = content as string, FontSize = ((string)content).Length == 1 ? 22 : 15, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            if (inner is FrameworkElement) { ((FrameworkElement)inner).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)inner).VerticalAlignment = VerticalAlignment.Center; }
            var faceBorder = new Border { Background = Ui.Brush(face), CornerRadius = new CornerRadius(12), Child = inner };
            faceBorder.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Ui.Brush(ink));
            faceBorder.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 15.0);

            var b = new Button
            {
                Content = faceBorder, Height = KeyH, Focusable = false, IsTabStop = false, Tag = units, Cursor = Cursors.Hand,
                Foreground = Ui.Brush(ink), Template = KeyTemplate()
            };
            if (action != null) b.Click += (s, e) => action();
            return b;
        }

        private static ControlTemplate _keyTemplate;
        private static ControlTemplate KeyTemplate()
        {
            if (_keyTemplate != null) return _keyTemplate;
            var root = new FrameworkElementFactory(typeof(Grid), "Root");
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            root.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            root.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(1, 1));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            root.AppendChild(cp);
            var shade = new FrameworkElementFactory(typeof(Border), "Shade");
            shade.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            shade.SetValue(Border.BackgroundProperty, Ui.Brush("#2F3024"));
            shade.SetValue(UIElement.OpacityProperty, 0.0);
            shade.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(shade);

            var t = new ControlTemplate(typeof(Button)) { VisualTree = root };
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.18, "Shade"));
            pressed.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(0.94, 0.94), "Root"));
            t.Triggers.Add(pressed);
            t.Seal();
            _keyTemplate = t;
            return t;
        }

        /// <summary>The keyboard rises from the bottom edge when it opens.</summary>
        public void SlideIn()
        {
            var move = new TranslateTransform(0, 40);
            RenderTransform = move;
            Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        }
    }
}
