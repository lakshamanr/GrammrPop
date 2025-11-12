using System;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;
using GrammrPop.Services;
using GrammrPop.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace GrammrPop
{
    public partial class App : Application
    {
        private SettingsService _settingsService = null!;
        private TextBoxMonitorService? _textBoxMonitor;
        private FloatingIconWindow? _floatingIcon;
        private TaskbarIcon? _trayIcon;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Initialize settings service
            _settingsService = new SettingsService();
            _settingsService.Load();

            // Create system tray icon
            CreateTrayIcon();

            // Show startup notification
            _trayIcon?.ShowBalloonTip(
                "GrammrPop Started",
                $"Auto-Detect: {(_settingsService.CurrentSettings.EnableAutoDetect ? "ON" : "OFF")}\nHotkey: Ctrl+Alt+G",
                BalloonIcon.Info);

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

                // Create grammar client
                var grammarClient = new LanguageToolClient();

                // Create and configure text box monitor with auto-checking
                _textBoxMonitor = new TextBoxMonitorService(grammarClient, _settingsService);
                _textBoxMonitor.GrammarErrorsFound += OnGrammarErrorsFound;
                _textBoxMonitor.TextBoxLostFocus += OnTextBoxLostFocus;
                _textBoxMonitor.Start();

                System.Diagnostics.Debug.WriteLine("✓ Auto-detect started with real-time grammar checking");
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
                _textBoxMonitor.GrammarErrorsFound -= OnGrammarErrorsFound;
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

        private void OnGrammarErrorsFound(object? sender, GrammarErrorsFoundEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_floatingIcon == null)
                        return;

                    // Only show icon when errors found (Grammarly-style)
                    if (e.ErrorCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"📍 Showing icon with {e.ErrorCount} errors at ({e.Bounds.X}, {e.Bounds.Y})");
                        _floatingIcon.ShowWithErrors(e.Bounds, e.ErrorCount, e.Matches, e.OriginalText, e.Element);
                    }
                    else
                    {
                        // Hide icon when no errors
                        System.Diagnostics.Debug.WriteLine("✓ No errors - hiding icon");
                        _floatingIcon.Hide();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error in OnGrammarErrorsFound: {ex.Message}");
                    MessageBox.Show(
                        $"Error showing grammar icon: {ex.Message}",
                        "GrammrPop Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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

        private void CreateTrayIcon()
        {
            _trayIcon = new TaskbarIcon
            {
                Icon = SystemIcons.Application,
                ToolTipText = "GrammrPop - Grammar Assistant\nHotkey: Ctrl+Alt+G"
            };

            // Create context menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Open GrammrPop menu item
            var openItem = new System.Windows.Controls.MenuItem { Header = "Open GrammrPop (Ctrl+Alt+G)" };
            openItem.Click += (s, e) =>
            {
                var popup = new PopupWindow(_settingsService);
                popup.Show();
                popup.Activate();
            };
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            // Auto-detect toggle menu item
            var autoDetectItem = new System.Windows.Controls.MenuItem
            {
                Header = "Auto-Detect Mode",
                IsCheckable = true,
                IsChecked = _settingsService.CurrentSettings.EnableAutoDetect
            };
            autoDetectItem.Click += (s, e) =>
            {
                _settingsService.CurrentSettings.EnableAutoDetect = autoDetectItem.IsChecked;
                _settingsService.Save();
                RefreshAutoDetect();
            };
            contextMenu.Items.Add(autoDetectItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            // Settings menu item
            var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
            settingsItem.Click += (s, e) =>
            {
                var settingsWindow = new SettingsWindow(_settingsService);
                settingsWindow.Show();
            };
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            // Exit menu item
            var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
            exitItem.Click += (s, e) =>
            {
                Shutdown();
            };
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;

            // Double-click to open popup
            _trayIcon.TrayMouseDoubleClick += (s, e) =>
            {
                var popup = new PopupWindow(_settingsService);
                popup.Show();
                popup.Activate();
            };
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Cleanup auto-detect
            StopAutoDetect();

            // Cleanup tray icon
            _trayIcon?.Dispose();

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
