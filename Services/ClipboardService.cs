using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace GrammrPop.Services
{
    public class ClipboardService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private IntPtr _previousWindow = IntPtr.Zero;

        /// <summary>
        /// Captures the currently focused window (call this before showing popup)
        /// </summary>
        public void CapturePreviousWindow()
        {
            _previousWindow = GetForegroundWindow();
        }

        /// <summary>
        /// Copies text to clipboard
        /// </summary>
        public void CopyToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to copy to clipboard: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Copies text to clipboard and optionally pastes it back to previous window
        /// </summary>
        public void CopyAndPasteBack(string text, bool autoPaste)
        {
            CopyToClipboard(text);

            if (!autoPaste || _previousWindow == IntPtr.Zero)
                return;

            try
            {
                // Give user a moment to see the result
                Thread.Sleep(200);

                // Restore focus to previous window
                SetForegroundWindow(_previousWindow);

                // Wait a bit for window to activate
                Thread.Sleep(100);

                // Simulate Ctrl+V
                var simulator = new InputSimulator();
                simulator.Keyboard.ModifiedKeyStroke(
                    VirtualKeyCode.CONTROL,
                    VirtualKeyCode.VK_V);
            }
            catch (Exception ex)
            {
                // Auto-paste failed, but clipboard copy succeeded
                System.Diagnostics.Debug.WriteLine($"Auto-paste failed: {ex.Message}");
            }
        }
    }
}
