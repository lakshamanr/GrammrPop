using System;
using System.Drawing;
using System.Threading;
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
        private const string MutexName = "GrammrPop_SingleInstance_Mutex_B8F3E2A1";
        private static Mutex? _singleInstanceMutex;
        
        private SettingsService _settingsService = null!;
        private TextBoxMonitorService? _textBoxMonitor;
        private FloatingIconWindow? _floatingIcon;
        private TaskbarIcon? _trayIcon;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Check for single instance
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                MessageBox.Show(
                    "GrammrPop is already running.\n\n" +
                    "Check your system tray for the GrammrPop icon.",
                    "GrammrPop - Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                // Exit this instance
                Shutdown();
                return;
            }

            // Initialize settings service
            _settingsService = new SettingsService();
            _settingsService.Load();

            // Create system tray icon
            CreateTrayIcon();

            // Show startup notification
            var hotkeyDisplay = _settingsService.CurrentSettings.Hotkey;
            _trayIcon?.ShowBalloonTip(
                "GrammrPop Started",
                $"Auto-Detect: {(_settingsService.CurrentSettings.EnableAutoDetect ? "ON" : "OFF")}\nHotkey: {hotkeyDisplay}",
                BalloonIcon.Info);

            // Register global hotkey
            RegisterHotkey();

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
            try
            {
                var settings = _settingsService.CurrentSettings;
                
                // Parse hotkey from settings (e.g., "Ctrl+Alt+G")
                if (!TryParseHotkey(settings.Hotkey, out var modifiers, out var key))
                {
                    throw new Exception($"Invalid hotkey format: {settings.Hotkey}");
                }

                HotkeyManager.Current.AddOrReplace(
                    "OpenGrammrPop",
                    key,
                    modifiers,
                    OnHotkeyPressed);
                    
                System.Diagnostics.Debug.WriteLine($"? Hotkey registered: {settings.Hotkey}");
            }
            catch (HotkeyAlreadyRegisteredException)
            {
                var settings = _settingsService.CurrentSettings;
                MessageBox.Show(
                    $"The hotkey '{settings.Hotkey}' is already in use by another application.\n\n" +
                    "Please choose a different hotkey in Settings.\n\n" +
                    "You can still use GrammrPop from the system tray icon or auto-detect mode.",
                    "GrammrPop - Hotkey Conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to register global hotkey: {ex.Message}\n\n" +
                    "You can change the hotkey in settings or use the system tray icon.",
                    "GrammrPop - Hotkey Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private bool TryParseHotkey(string hotkey, out ModifierKeys modifiers, out Key key)
        {
            modifiers = ModifierKeys.None;
            key = Key.None;

            try
            {
                var parts = hotkey.Split('+');
                if (parts.Length < 2)
                    return false;

                // Parse modifiers
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var modifier = parts[i].Trim();
                    switch (modifier.ToLower())
                    {
                        case "ctrl":
                        case "control":
                            modifiers |= ModifierKeys.Control;
                            break;
                        case "alt":
                            modifiers |= ModifierKeys.Alt;
                            break;
                        case "shift":
                            modifiers |= ModifierKeys.Shift;
                            break;
                        case "win":
                        case "windows":
                            modifiers |= ModifierKeys.Windows;
                            break;
                        default:
                            return false;
                    }
                }

                // Parse key
                var keyString = parts[parts.Length - 1].Trim();
                if (!Enum.TryParse<Key>(keyString, true, out key))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ReRegisterHotkey()
        {
            // Remove existing hotkey
            try
            {
                HotkeyManager.Current.Remove("OpenGrammrPop");
            }
            catch { /* Ignore if hotkey wasn't registered */ }

            // Register new hotkey
            RegisterHotkey();
            
            // Update tray icon tooltip
            UpdateTrayIconTooltip();
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
                Console.WriteLine("===========================================");
                Console.WriteLine("?? STARTING AUTO-DETECT MODE");
                Console.WriteLine("===========================================");

                // Create floating icon window
                _floatingIcon = new FloatingIconWindow(_settingsService);
                Console.WriteLine("? Floating icon window created");

                // Create grammar client
                var grammarClient = new LanguageToolClient();
                Console.WriteLine("? Grammar client created");

                // Create and configure text box monitor with auto-checking
                _textBoxMonitor = new TextBoxMonitorService(grammarClient, _settingsService);
                _textBoxMonitor.GrammarErrorsFound += OnGrammarErrorsFound;
                _textBoxMonitor.TextBoxLostFocus += OnTextBoxLostFocus;
                _textBoxMonitor.Start();

                Console.WriteLine("? Text box monitor started - polling every 300ms");
                Console.WriteLine("? Auto-detect is now ACTIVE");
                Console.WriteLine("? FAST MODE: Grammar checked 0.8s after typing stops");
                Console.WriteLine("? Focus any textbox and type to test...\n");

                System.Diagnostics.Debug.WriteLine("? Auto-detect started with real-time grammar checking");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? FAILED TO START AUTO-DETECT: {ex.Message}");
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
                        Console.WriteLine($"?? SHOWING ICON: {e.ErrorCount} errors found at position ({e.Bounds.X:F0}, {e.Bounds.Y:F0})");
                        Console.WriteLine($"   Text sample: \"{e.OriginalText.Substring(0, Math.Min(50, e.OriginalText.Length))}...\"");
                        System.Diagnostics.Debug.WriteLine($"?? Showing icon with {e.ErrorCount} errors at ({e.Bounds.X}, {e.Bounds.Y})");
                        _floatingIcon.ShowWithErrors(e.Bounds, e.ErrorCount, e.Matches, e.OriginalText, e.Element);
                    }
                    else
                    {
                        // Hide icon when no errors
                        Console.WriteLine("? No errors found - hiding icon");
                        System.Diagnostics.Debug.WriteLine("? No errors - hiding icon");
                        _floatingIcon.Hide();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"? ERROR showing icon: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"? Error in OnGrammarErrorsFound: {ex.Message}");
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
            };
            
            UpdateTrayIconTooltip();

            // Create context menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Open GrammrPop menu item
            var openItem = new System.Windows.Controls.MenuItem();
            openItem.Click += (s, e) =>
            {
                var popup = new PopupWindow(_settingsService);
                popup.Show();
                popup.Activate();
            };
            contextMenu.Items.Add(openItem);

            // Update menu text dynamically
            contextMenu.Opened += (s, e) =>
            {
                openItem.Header = $"Open GrammrPop ({_settingsService.CurrentSettings.Hotkey})";
            };

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

        private void UpdateTrayIconTooltip()
        {
            if (_trayIcon != null)
            {
                var hotkey = _settingsService.CurrentSettings.Hotkey;
                _trayIcon.ToolTipText = $"GrammrPop - Grammar Assistant\nHotkey: {hotkey}";
            }
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
            
            // Release single instance mutex
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
    }
}
