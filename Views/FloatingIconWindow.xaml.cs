using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using GrammrPop.Models;
using GrammrPop.Services;

namespace GrammrPop.Views
{
    public partial class FloatingIconWindow : Window
    {
        private AutomationElement? _targetElement;
        private string _targetText = string.Empty;
        private Match[] _errorMatches = Array.Empty<Match>();
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

        public void ShowWithErrors(Rect textBoxBounds, int errorCount, Match[] matches, string originalText, AutomationElement element)
        {
            _targetElement = element;
            _targetText = originalText;
            _errorMatches = matches;

            // Update error count badge
            ErrorCountText.Text = errorCount.ToString();

            // Position at BOTTOM-RIGHT corner of textbox (Grammarly-style)
            double iconX = textBoxBounds.Right - Width - 8;  // 8px from right edge
            double iconY = textBoxBounds.Bottom - Height - 8; // 8px from bottom edge

            // Ensure icon stays on screen
            var screenBounds = SystemParameters.WorkArea;
            iconX = Math.Max(0, Math.Min(iconX, screenBounds.Width - Width));
            iconY = Math.Max(0, Math.Min(iconY, screenBounds.Height - Height));

            Left = iconX;
            Top = iconY;

            System.Diagnostics.Debug.WriteLine($"Icon positioned at bottom-right: ({iconX}, {iconY})");

            if (!IsVisible)
            {
                Show();
            }

            // Ensure the window is topmost
            Topmost = true;
        }

        private void IconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide the floating icon
                Hide();

                // Open popup window with pre-loaded errors
                var popup = new PopupWindow(_settingsService);
                popup.Show();
                popup.Activate();
                popup.Focus();

                // Pre-fill with the textbox content
                popup.InputTextBox.Text = _targetText;

                // Auto-load the grammar results
                popup.LoadGrammarResults(_errorMatches, _targetText);
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
    }
}
