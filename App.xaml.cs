using System;
using System.Windows;
using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;
using GrammrPop.Services;
using GrammrPop.Views;

namespace GrammrPop
{
    public partial class App : Application
    {
        private SettingsService _settingsService = null!;
        private TextBoxMonitorService? _textBoxMonitor;
        private FloatingIconWindow? _floatingIcon;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Initialize settings service
            _settingsService = new SettingsService();
            _settingsService.Load();

            // Register global hotkey (default Ctrl+Alt+G)
            try
            {
                RegisterHotkey();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to register global hotkey: {ex.Message}\n\nYou can change the hotkey in settings.",
                    "GrammrPop - Hotkey Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Initialize auto-detect feature if enabled
            if (_settingsService.CurrentSettings.EnableAutoDetect)
            {
                StartAutoDetect();
            }

            // Don't show main window - app runs in system tray / background
            // Popup appears on hotkey or icon click
        }

        private void RegisterHotkey()
        {
            var settings = _settingsService.CurrentSettings;

            // Parse hotkey from settings (e.g., "Ctrl+Alt+G")
            var modifiers = ModifierKeys.Control | ModifierKeys.Alt;
            var key = Key.G;

            // TODO: Parse from settings.Hotkey string if you want to make it configurable
            // For now, use default Ctrl+Alt+G

            HotkeyManager.Current.AddOrReplace(
                "OpenGrammrPop",
                key,
                modifiers,
                OnHotkeyPressed);
        }

        private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
        {
            e.Handled = true;

            // Show popup window
            var popup = new PopupWindow(_settingsService);
            popup.Show();
            popup.Activate();
            popup.Focus();
        }

        private void StartAutoDetect()
        {
            try
            {
                // Create floating icon window
                _floatingIcon = new FloatingIconWindow(_settingsService);

                // Create and configure text box monitor
                _textBoxMonitor = new TextBoxMonitorService();
                _textBoxMonitor.TextBoxFocused += OnTextBoxFocused;
                _textBoxMonitor.TextBoxLostFocus += OnTextBoxLostFocus;
                _textBoxMonitor.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to start auto-detect feature: {ex.Message}\n\nYou can disable it in settings.",
                    "GrammrPop - Auto-Detect Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void StopAutoDetect()
        {
            if (_textBoxMonitor != null)
            {
                _textBoxMonitor.TextBoxFocused -= OnTextBoxFocused;
                _textBoxMonitor.TextBoxLostFocus -= OnTextBoxLostFocus;
                _textBoxMonitor.Stop();
                _textBoxMonitor.Dispose();
                _textBoxMonitor = null;
            }

            if (_floatingIcon != null)
            {
                _floatingIcon.Hide();
                _floatingIcon.Close();
                _floatingIcon = null;
            }
        }

        private void OnTextBoxFocused(object? sender, TextBoxDetectedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_floatingIcon != null && !string.IsNullOrWhiteSpace(e.Text))
                {
                    _floatingIcon.PositionNearTextBox(e.Bounds, e.Text, e.Element);
                }
            });
        }

        private void OnTextBoxLostFocus(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _floatingIcon?.Hide();
            });
        }

        public void RefreshAutoDetect()
        {
            // Called when settings change
            if (_settingsService.CurrentSettings.EnableAutoDetect)
            {
                if (_textBoxMonitor == null)
                {
                    StartAutoDetect();
                }
            }
            else
            {
                if (_textBoxMonitor != null)
                {
                    StopAutoDetect();
                }
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Cleanup auto-detect
            StopAutoDetect();

            // Cleanup hotkey
            try
            {
                HotkeyManager.Current.Remove("OpenGrammrPop");
            }
            catch { /* Ignore cleanup errors */ }

            // Save settings
            _settingsService.Save();
        }
    }
}
