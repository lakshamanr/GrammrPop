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

            // Don't show main window - app runs in system tray / background
            // Popup appears on hotkey
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

        private void Application_Exit(object sender, ExitEventArgs e)
        {
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
