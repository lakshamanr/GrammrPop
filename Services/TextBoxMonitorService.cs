using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using GrammrPop.Models;

namespace GrammrPop.Services
{
    /// <summary>
    /// Monitors focused text controls and auto-checks grammar (Grammarly-style)
    /// </summary>
    public class TextBoxMonitorService : IDisposable
    {
        private readonly DispatcherTimer _monitorTimer;
        private readonly DispatcherTimer _textCheckTimer;
        private readonly DispatcherTimer _hideDelayTimer;
        private readonly LanguageToolClient _grammarClient;
        private readonly SettingsService _settingsService;

        private AutomationElement? _lastFocusedElement;
        private AutomationElement? _currentStableElement;
        private string _lastCheckedText = string.Empty;
        private string _currentText = string.Empty;
        private int _stableCount = 0;
        private readonly object _lockObject = new object();
        private bool _isMonitoring;
        private bool _isChecking = false;

        public event EventHandler<TextBoxDetectedEventArgs>? TextBoxFocused;
        public event EventHandler<GrammarErrorsFoundEventArgs>? GrammarErrorsFound;
        public event EventHandler? TextBoxLostFocus;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        // Scintilla messages
        private const uint SCI_GETLENGTH = 2006;
        private const uint SCI_GETTEXT = 2182;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;

        public TextBoxMonitorService(LanguageToolClient grammarClient, SettingsService settingsService)
        {
            _grammarClient = grammarClient;
            _settingsService = settingsService;

            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300) // Check every 300ms for faster detection
            };
            _monitorTimer.Tick += MonitorTimer_Tick;

            _textCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800) // Check 0.8 seconds after typing stops (faster than 2 sec)
            };
            _textCheckTimer.Tick += TextCheckTimer_Tick;

            _hideDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _hideDelayTimer.Tick += HideDelayTimer_Tick;
        }

        public void Start()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;
            _monitorTimer.Start();
            Console.WriteLine("🔍 TextBoxMonitor: Started monitoring for focused textboxes");
        }

        public void Stop()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;
            _monitorTimer.Stop();
            _lastFocusedElement = null;
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // Get currently focused element
                var focusedElement = AutomationElement.FocusedElement;

                if (focusedElement == null)
                {
                    HandleFocusLost();
                    return;
                }

                // Check if it's a text control
                var isTextControl = IsTextControl(focusedElement);

                // Debug logging for troubleshooting
                if (isTextControl)
                {
                    try
                    {
                        var controlType = focusedElement.Current.ControlType.ProgrammaticName;
                        var className = focusedElement.Current.ClassName;
                        var processName = "";

                        try
                        {
                            var hwnd = new IntPtr(focusedElement.Current.NativeWindowHandle);
                            GetWindowThreadProcessId(hwnd, out uint processId);
                            var process = System.Diagnostics.Process.GetProcessById((int)processId);
                            processName = process.ProcessName;
                        }
                        catch { /* Ignore process name errors */ }

                        Console.WriteLine($"\n✓✓✓ TEXTBOX DETECTED!");
                        Console.WriteLine($"    Type: {controlType}");
                        Console.WriteLine($"    Class: {className}");
                        Console.WriteLine($"    Process: {processName}");
                    }
                    catch { /* Ignore debug logging errors */ }
                }

                if (!isTextControl)
                {
                    // Not a text control - start hide timer
                    if (_currentStableElement != null)
                    {
                        HandleFocusLost();
                    }
                    return;
                }

                lock (_lockObject)
                {
                    // Cancel any pending hide
                    _hideDelayTimer.Stop();

                    // Check if this is the same element as before
                    if (IsSameElement(focusedElement, _currentStableElement))
                    {
                        // Same element - check for text changes
                        CheckForTextChanges(focusedElement);
                        return;
                    }

                    // New element - check if it's stable
                    if (IsSameElement(focusedElement, _lastFocusedElement))
                    {
                        _stableCount++;

                        // Only fire event after element has been stable for 1 poll (300ms) - FASTER!
                        if (_stableCount >= 1)
                        {
                            _currentStableElement = focusedElement;
                            _stableCount = 0;

                            // Get control information
                            var rect = GetElementBounds(focusedElement);
                            var text = GetElementText(focusedElement);

                            if (rect.HasValue && rect.Value.Width > 20 && rect.Value.Height > 10)
                            {
                                Console.WriteLine($"✓ Textbox is STABLE (confirmed after 300ms)");
                                Console.WriteLine($"  Size: {rect?.Width:F0}x{rect?.Height:F0} pixels");
                                Console.WriteLine($"  Current text length: {text.Length} chars");
                                Console.WriteLine($"  ⚡ Fast mode: Grammar checked 0.8s after typing stops");
                                System.Diagnostics.Debug.WriteLine($"✓ Stable textbox detected: {rect?.Width}x{rect?.Height}");

                                _currentText = text;
                                _lastCheckedText = "";  // Reset to trigger initial check

                                TextBoxFocused?.Invoke(this, new TextBoxDetectedEventArgs
                                {
                                    Element = focusedElement,
                                    Bounds = rect.Value,
                                    Text = text
                                });

                                // Start monitoring text changes
                                CheckForTextChanges(focusedElement);
                            }
                        }
                    }
                    else
                    {
                        // Different element - reset stability counter
                        _lastFocusedElement = focusedElement;
                        _stableCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // UI Automation can throw various exceptions for inaccessible elements
                System.Diagnostics.Debug.WriteLine($"TextBoxMonitor error: {ex.Message}");
            }
        }

        private bool IsSameElement(AutomationElement? element1, AutomationElement? element2)
        {
            if (element1 == null || element2 == null)
                return false;

            try
            {
                return Automation.Compare(element1, element2);
            }
            catch
            {
                return false;
            }
        }

        private bool IsTextControl(AutomationElement element)
        {
            try
            {
                var controlType = element.Current.ControlType;
                var className = element.Current.ClassName;
                var isEnabled = element.Current.IsEnabled;
                var isKeyboardFocusable = element.Current.IsKeyboardFocusable;

                // Must be enabled and keyboard focusable
                if (!isEnabled || !isKeyboardFocusable)
                    return false;

                // Skip password fields for security
                if (element.Current.IsPassword)
                    return false;

                // EXPLICITLY REJECT non-input controls
                if (controlType == ControlType.Button ||
                    controlType == ControlType.MenuItem ||
                    controlType == ControlType.ToolBar ||
                    controlType == ControlType.HeaderItem ||
                    controlType == ControlType.TreeItem ||
                    controlType == ControlType.ListItem ||
                    controlType == ControlType.TabItem ||
                    controlType == ControlType.MenuBar ||
                    controlType == ControlType.StatusBar ||
                    controlType == ControlType.TitleBar ||
                    controlType == ControlType.ToolTip ||
                    controlType == ControlType.Image ||
                    controlType == ControlType.Table ||
                    controlType == ControlType.DataGrid ||
                    controlType == ControlType.DataItem ||
                    controlType == ControlType.Header ||
                    controlType == ControlType.Group ||
                    controlType == ControlType.Thumb ||
                    controlType == ControlType.ScrollBar ||
                    controlType == ControlType.Separator ||
                    controlType == ControlType.ProgressBar ||
                    controlType == ControlType.Slider ||
                    controlType == ControlType.Spinner)
                {
                    return false;
                }

                // ACCEPT standard Edit controls (Notepad, standard textboxes)
                if (controlType == ControlType.Edit)
                {
                    Console.WriteLine($"    ✓ Standard Edit control");
                    return true;
                }

                // Check for known editor control class names
                if (!string.IsNullOrEmpty(className))
                {
                    var lowerClassName = className.ToLower();

                    // TEMPORARILY DISABLED: Notepad++ causes hang due to frequent text extraction
                    // TODO: Re-enable with proper caching to avoid reading full file every 500ms
                    /*
                    // Notepad++ uses Scintilla
                    if (lowerClassName.Contains("scintilla"))
                    {
                        Console.WriteLine($"    ✓ Scintilla editor (Notepad++, Sublime)");
                        return true;
                    }
                    */

                    // Visual Studio Code, Browsers (Chrome, Edge) use this
                    if (lowerClassName.Contains("chrome_renderwidgethosthwnd"))
                    {
                        Console.WriteLine($"    ✓ Chrome-based editor (VS Code, Browser)");
                        return true;
                    }

                    // RichEdit controls (WordPad, etc.)
                    if (lowerClassName.Contains("richedit"))
                    {
                        Console.WriteLine($"    ✓ RichEdit control");
                        return true;
                    }

                    // Explicit textbox class names
                    if (lowerClassName == "textbox" || lowerClassName == "edit")
                    {
                        Console.WriteLine($"    ✓ TextBox/Edit class");
                        return true;
                    }
                }

                // ONLY accept Document if it has ValuePattern AND is editable
                // This catches some web textareas but filters out read-only documents
                if (controlType == ControlType.Document)
                {
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                    {
                        var pattern = (ValuePattern)valuePattern;
                        if (!pattern.Current.IsReadOnly)
                        {
                            Console.WriteLine($"    ✓ Editable Document with ValuePattern");
                            return true;
                        }
                    }
                    return false; // Read-only document or no ValuePattern
                }

                // For Pane controls, be very strict
                if (controlType == ControlType.Pane)
                {
                    // Must have ValuePattern AND be editable
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                    {
                        var pattern = (ValuePattern)valuePattern;
                        if (!pattern.Current.IsReadOnly)
                        {
                            Console.WriteLine($"    ✓ Editable Pane with ValuePattern");
                            return true;
                        }
                    }
                    return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private Rect? GetElementBounds(AutomationElement element)
        {
            try
            {
                var rect = element.Current.BoundingRectangle;

                if (rect.IsEmpty || rect.Width == 0 || rect.Height == 0)
                    return null;

                return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
            }
            catch
            {
                return null;
            }
        }

        private string GetElementText(AutomationElement element)
        {
            try
            {
                var className = element.Current.ClassName;

                // Special handling for Scintilla controls (Notepad++, Sublime Text, etc.)
                if (!string.IsNullOrEmpty(className) && className.ToLower().Contains("scintilla"))
                {
                    return GetScintillaText(element);
                }

                // Try ValuePattern first (for standard textboxes)
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                {
                    var pattern = (ValuePattern)valuePattern;
                    var text = pattern.Current.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }

                // Try TextPattern (for rich text controls)
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
                {
                    var pattern = (TextPattern)textPattern;
                    var range = pattern.DocumentRange;
                    var text = range.GetText(-1) ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }

                // Fallback: Try WM_GETTEXT for other controls
                try
                {
                    var hwnd = new IntPtr(element.Current.NativeWindowHandle);
                    if (hwnd != IntPtr.Zero)
                    {
                        int length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
                        if (length > 0)
                        {
                            StringBuilder sb = new StringBuilder(length + 1);
                            SendMessage(hwnd, WM_GETTEXT, new IntPtr(sb.Capacity), sb);
                            return sb.ToString();
                        }
                    }
                }
                catch { /* Ignore WM_GETTEXT failures */ }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetScintillaText(AutomationElement element)
        {
            try
            {
                var hwnd = new IntPtr(element.Current.NativeWindowHandle);
                if (hwnd == IntPtr.Zero)
                    return string.Empty;

                // Get text length using Scintilla message
                int length = (int)SendMessage(hwnd, SCI_GETLENGTH, IntPtr.Zero, IntPtr.Zero);

                if (length <= 0)
                    return string.Empty;

                // Limit text length to prevent huge allocations
                if (length > 1000000) // 1 MB limit
                {
                    Console.WriteLine($"   ⚠️  Scintilla text too large ({length} bytes), truncating to 1MB");
                    length = 1000000;
                }

                // Get text using Scintilla message
                StringBuilder sb = new StringBuilder(length + 1);
                SendMessage(hwnd, SCI_GETTEXT, new IntPtr(length + 1), sb);

                var text = sb.ToString();
                Console.WriteLine($"   ✓ Extracted {text.Length} chars from Scintilla");
                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to get Scintilla text: {ex.Message}");
                return string.Empty;
            }
        }

        private void HideDelayTimer_Tick(object? sender, EventArgs e)
        {
            _hideDelayTimer.Stop();

            lock (_lockObject)
            {
                _lastFocusedElement = null;
                _currentStableElement = null;
                _stableCount = 0;
                _textCheckTimer.Stop();
                TextBoxLostFocus?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine("Icon hidden (delayed)");
            }
        }

        private void CheckForTextChanges(AutomationElement element)
        {
            try
            {
                var newText = GetElementText(element);

                if (newText != _currentText)
                {
                    _currentText = newText;
                    Console.WriteLine($"\n📝 TEXT CHANGED!");
                    Console.WriteLine($"   New length: {newText.Length} chars");
                    Console.WriteLine($"   Preview: \"{newText.Substring(0, Math.Min(50, newText.Length))}{(newText.Length > 50 ? "..." : "")}\"");
                    Console.WriteLine($"   ⚡ Starting 0.8-second countdown before grammar check...");
                    System.Diagnostics.Debug.WriteLine($"Text changed: length={newText.Length}");

                    // Restart the check timer (debouncing)
                    _textCheckTimer.Stop();
                    _textCheckTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error checking text changes: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error checking text changes: {ex.Message}");
            }
        }

        private async void TextCheckTimer_Tick(object? sender, EventArgs e)
        {
            _textCheckTimer.Stop();

            if (_isChecking || _currentStableElement == null)
            {
                Console.WriteLine("⏭️  Skipping check (already checking or no element)");
                return;
            }

            try
            {
                // Only check if text has changed since last check
                if (_currentText == _lastCheckedText || string.IsNullOrWhiteSpace(_currentText))
                {
                    // No errors to show
                    Console.WriteLine("⏭️  Skipping check (text unchanged or empty)");
                    System.Diagnostics.Debug.WriteLine("No text or unchanged - hiding icon");
                    GrammarErrorsFound?.Invoke(this, new GrammarErrorsFoundEventArgs
                    {
                        Element = _currentStableElement,
                        Bounds = GetElementBounds(_currentStableElement) ?? default,
                        ErrorCount = 0,
                        Matches = Array.Empty<Match>()
                    });
                    return;
                }

                _isChecking = true;
                _lastCheckedText = _currentText;

                Console.WriteLine($"\n🔍 GRAMMAR CHECK STARTED");
                Console.WriteLine($"   Text: \"{_currentText.Substring(0, Math.Min(50, _currentText.Length))}{(_currentText.Length > 50 ? "..." : "")}\"");
                System.Diagnostics.Debug.WriteLine($"🔍 Auto-checking grammar for text: {_currentText.Substring(0, Math.Min(50, _currentText.Length))}...");

                var settings = _settingsService.CurrentSettings;
                var endpoint = settings.UseLocalServer ? settings.LocalServerUrl : settings.ApiEndpoint;

                Console.WriteLine($"   API Endpoint: {endpoint}");
                Console.WriteLine($"   Language: {settings.Language}");
                Console.WriteLine($"   Sending request to LanguageTool...");

                var result = await _grammarClient.CheckAsync(_currentText, settings.Language, endpoint, settings.ApiKey);

                Console.WriteLine($"   ✓ Response received!");

                // Get bounds again in case window moved
                var rect = GetElementBounds(_currentStableElement);
                if (!rect.HasValue)
                {
                    Console.WriteLine("   ⚠️  Warning: Could not get textbox bounds");
                    return;
                }

                if (result.Matches != null && result.Matches.Length > 0)
                {
                    Console.WriteLine($"   ✅ FOUND {result.Matches.Length} GRAMMAR ERROR(S)!");
                    for (int i = 0; i < Math.Min(3, result.Matches.Length); i++)
                    {
                        var match = result.Matches[i];
                        Console.WriteLine($"      #{i+1}: {match.Message}");
                    }
                    if (result.Matches.Length > 3)
                    {
                        Console.WriteLine($"      ... and {result.Matches.Length - 3} more");
                    }
                    Console.WriteLine($"   → Firing GrammarErrorsFound event to show icon\n");
                    System.Diagnostics.Debug.WriteLine($"✓ Found {result.Matches.Length} grammar errors");

                    GrammarErrorsFound?.Invoke(this, new GrammarErrorsFoundEventArgs
                    {
                        Element = _currentStableElement,
                        Bounds = rect.Value,
                        ErrorCount = result.Matches.Length,
                        Matches = result.Matches,
                        OriginalText = _currentText
                    });
                }
                else
                {
                    Console.WriteLine($"   ✓ No errors found (text is correct)");
                    System.Diagnostics.Debug.WriteLine("✓ No errors found - hiding icon");

                    GrammarErrorsFound?.Invoke(this, new GrammarErrorsFoundEventArgs
                    {
                        Element = _currentStableElement,
                        Bounds = rect.Value,
                        ErrorCount = 0,
                        Matches = Array.Empty<Match>()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ GRAMMAR CHECK FAILED!");
                Console.WriteLine($"   Error: {ex.Message}");
                Console.WriteLine($"   Type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}");
                }
                System.Diagnostics.Debug.WriteLine($"❌ Error checking grammar: {ex.Message}\n{ex.StackTrace}");

                // Show error to user
                System.Windows.MessageBox.Show(
                    $"Failed to check grammar:\n\n{ex.Message}\n\nCheck your internet connection and LanguageTool settings.",
                    "GrammrPop - Grammar Check Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                _isChecking = false;
            }
        }

        private void HandleFocusLost()
        {
            // Don't hide immediately - use delay timer to prevent flickering
            if (!_hideDelayTimer.IsEnabled)
            {
                System.Diagnostics.Debug.WriteLine("Starting hide delay timer...");
                _hideDelayTimer.Start();
            }
        }

        public void Dispose()
        {
            Stop();
            _textCheckTimer.Stop();
            _hideDelayTimer.Stop();
            _monitorTimer.Tick -= MonitorTimer_Tick;
            _textCheckTimer.Tick -= TextCheckTimer_Tick;
            _hideDelayTimer.Tick -= HideDelayTimer_Tick;
        }
    }

    public class TextBoxDetectedEventArgs : EventArgs
    {
        public AutomationElement Element { get; set; } = null!;
        public Rect Bounds { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class GrammarErrorsFoundEventArgs : EventArgs
    {
        public AutomationElement Element { get; set; } = null!;
        public Rect Bounds { get; set; }
        public int ErrorCount { get; set; }
        public Match[] Matches { get; set; } = Array.Empty<Match>();
        public string OriginalText { get; set; } = string.Empty;
    }
}
