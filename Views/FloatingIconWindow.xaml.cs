using System;
using System.Windows;
using System.Windows.Automation;
using GrammrPop.Services;

namespace GrammrPop.Views
{
    public partial class FloatingIconWindow : Window
    {
        private AutomationElement? _targetElement;
        private string _targetText = string.Empty;
        private readonly SettingsService _settingsService;

        public FloatingIconWindow(SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;

            // Prevent the window from getting focus
            Loaded += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowExStyle(hwnd);
            };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private void SetWindowExStyle(IntPtr hwnd)
        {
            // Make the window non-activatable so it doesn't steal focus
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void PositionNearTextBox(Rect textBoxBounds, string text, AutomationElement element)
        {
            _targetElement = element;
            _targetText = text;

            // Position icon at the right edge of the textbox, vertically centered
            var iconX = textBoxBounds.Right - Width - 5; // 5px padding from right edge
            var iconY = textBoxBounds.Top + (textBoxBounds.Height / 2) - (Height / 2);

            // Ensure icon stays on screen
            var screenBounds = SystemParameters.WorkArea;
            iconX = Math.Max(0, Math.Min(iconX, screenBounds.Width - Width));
            iconY = Math.Max(0, Math.Min(iconY, screenBounds.Height - Height));

            Left = iconX;
            Top = iconY;

            System.Diagnostics.Debug.WriteLine($"Positioning icon at: ({iconX}, {iconY}), Size: {Width}x{Height}");

            if (!IsVisible)
            {
                Show();
                System.Diagnostics.Debug.WriteLine("Icon shown!");
            }

            // Ensure the window is topmost
            Topmost = true;
            Activate();
        }

        private void IconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Re-fetch text in case it changed since last detection
                var currentText = _targetText;

                if (_targetElement != null)
                {
                    currentText = GetFreshTextFromElement(_targetElement) ?? _targetText;
                }

                // Hide the floating icon
                Hide();

                // Open popup window with the text
                var popup = new PopupWindow(_settingsService);
                popup.Show();
                popup.Activate();
                popup.Focus();

                // Pre-fill with the textbox content
                popup.InputTextBox.Text = currentText;
                popup.InputTextBox.SelectAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error opening GrammrPop:\n\n{ex.Message}",
                    "GrammrPop Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string? GetFreshTextFromElement(AutomationElement element)
        {
            try
            {
                // Try ValuePattern first
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                {
                    var pattern = (ValuePattern)valuePattern;
                    return pattern.Current.Value;
                }

                // Try TextPattern
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
                {
                    var pattern = (TextPattern)textPattern;
                    var range = pattern.DocumentRange;
                    return range.GetText(-1);
                }
            }
            catch
            {
                // Ignore errors, return cached text
            }

            return null;
        }
    }
}
