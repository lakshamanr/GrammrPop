using System;
using System.Linq;
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
                    controlType == ControlType.Spinner ||
                    controlType == ControlType.Pane)          // DISABLE Panes (causes issues)
                {
                    return false;
                }

                // ACCEPT standard Edit controls (Notepad, standard textboxes)
                if (controlType == ControlType.Edit)
                {
                    Console.WriteLine($"    ✓ Standard Edit control (Notepad/TextBox)");
                    return true;
                }

                // ACCEPT Document controls ONLY if they are editable input boxes
                // This enables Slack, browser textareas, Discord, Teams, etc.
                if (controlType == ControlType.Document)
                {
                    return IsEditableDocumentControl(element, className);
                }

                // REJECT everything else
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Deep inspection of Document controls with comprehensive Slack support
        /// Handles: Slack, Discord, Teams, browser textareas, and other rich text inputs
        /// </summary>
        private bool IsEditableDocumentControl(AutomationElement element, string className)
        {
            try
            {
                // Get process name for Slack-specific handling
                var processName = "unknown";
                var processId = 0u;
                try
                {
                    var hwnd = new IntPtr(element.Current.NativeWindowHandle);
                    GetWindowThreadProcessId(hwnd, out processId);
                    var process = System.Diagnostics.Process.GetProcessById((int)processId);
                    processName = process.ProcessName.ToLower();
                }
                catch { /* Ignore process name errors */ }

                var isSlack = processName.Contains("slack");
                var isChrome = processName.Contains("chrome");
                var isEdge = processName.Contains("msedge") || processName.Contains("edge");
                var isFirefox = processName.Contains("firefox");
                var isDiscord = processName.Contains("discord");
                var isTeams = processName.Contains("teams");
                var isBrowser = isChrome || isEdge || isFirefox;

                Console.WriteLine($"    🔍 Analyzing Document control:");
                Console.WriteLine($"       Process: {processName} (PID: {processId})");
                Console.WriteLine($"       ClassName: {className}");
                Console.WriteLine($"       IsSlack: {isSlack}, IsBrowser: {isBrowser}, IsDiscord: {isDiscord}, IsTeams: {isTeams}");

                // Get element properties for deep inspection
                var rect = GetElementBounds(element);
                var automationId = "";
                var name = "";
                var helpText = "";

                try
                {
                    automationId = element.Current.AutomationId ?? "";
                    name = element.Current.Name ?? "";
                    helpText = element.Current.HelpText ?? "";

                    // Note: ARIA role detection removed to avoid compilation issues
                    // LegacyIAccessiblePattern may not be available in all .NET versions
                }
                catch { /* Ignore property access errors */ }

                Console.WriteLine($"       AutomationId: '{automationId}'");
                Console.WriteLine($"       Name: '{name}'");
                Console.WriteLine($"       HelpText: '{helpText}'");
                if (rect.HasValue)
                {
                    Console.WriteLine($"       Size: {rect.Value.Width:F0}x{rect.Value.Height:F0}px");
                    Console.WriteLine($"       Position: ({rect.Value.X:F0}, {rect.Value.Y:F0})");
                }

                // ===== SLACK-SPECIFIC DETECTION =====
                if (isSlack)
                {
                    Console.WriteLine($"    🎯 SLACK DETECTED - Performing deep Slack analysis...");

                    // Slack uses various automation IDs and names for different input types
                    var slackIndicators = new[]
                    {
                        "message-input",        // Main message input
                        "composer",             // Composer area
                        "msg_input",            // Message input field
                        "search",               // Search boxes
                        "message_input",        // Alternative naming
                        "reply",                // Thread replies
                        "composer-input"        // Rich text composer
                    };

                    var matchesSlackPattern = slackIndicators.Any(indicator =>
                        automationId.ToLower().Contains(indicator) ||
                        name.ToLower().Contains(indicator) ||
                        helpText.ToLower().Contains(indicator));

                    if (matchesSlackPattern)
                    {
                        Console.WriteLine($"    ✓✓✓ SLACK INPUT IDENTIFIED (pattern match)!");
                        Console.WriteLine($"        Matched pattern in AutomationId/Name/HelpText");
                    }

                    // Check if element can be edited using ValuePattern
                    bool hasEditableValuePattern = false;
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                    {
                        var pattern = (ValuePattern)valuePattern;
                        if (!pattern.Current.IsReadOnly)
                        {
                            hasEditableValuePattern = true;
                            Console.WriteLine($"        ✓ Has editable ValuePattern (can read/write)");
                        }
                        else
                        {
                            Console.WriteLine($"        ✗ ValuePattern is read-only");
                        }
                    }

                    // Check if element supports TextPattern (alternative for rich text)
                    bool hasTextPattern = false;
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
                    {
                        hasTextPattern = true;
                        var pattern = (TextPattern)textPattern;

                        // Check if we can get text range (indicates input capability)
                        try
                        {
                            var range = pattern.DocumentRange;
                            if (range != null)
                            {
                                Console.WriteLine($"        ✓ Has TextPattern with DocumentRange");
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"        ⚠ TextPattern exists but DocumentRange failed");
                        }
                    }

                    // Slack message inputs: Check size constraints
                    if (rect.HasValue)
                    {
                        var width = rect.Value.Width;
                        var height = rect.Value.Height;

                        // Slack message input boxes have specific size ranges:
                        // - Main message input: typically 400-1500px wide, 40-300px tall
                        // - Thread reply: typically 300-1000px wide, 40-200px tall
                        // - Search box: typically 200-600px wide, 30-50px tall
                        // - Exclude full-screen displays (> 2000px wide or > 600px tall)

                        var isReasonableSize = width >= 100 && width < 2000 && height >= 20 && height < 600;
                        var isProbablyMessageInput = width >= 300 && width < 1600 && height >= 30 && height < 350;
                        var isProbablyThreadReply = width >= 250 && width < 1200 && height >= 30 && height < 250;
                        var isProbablySearch = width >= 150 && width < 800 && height >= 20 && height < 80;

                        Console.WriteLine($"        Size analysis:");
                        Console.WriteLine($"          IsReasonableSize: {isReasonableSize}");
                        Console.WriteLine($"          IsProbablyMessageInput: {isProbablyMessageInput}");
                        Console.WriteLine($"          IsProbablyThreadReply: {isProbablyThreadReply}");
                        Console.WriteLine($"          IsProbablySearch: {isProbablySearch}");

                        // Accept if: reasonable size AND (has editable ValuePattern OR has TextPattern)
                        if (isReasonableSize && (hasEditableValuePattern || hasTextPattern))
                        {
                            var inputType = isProbablyMessageInput ? "MESSAGE INPUT" :
                                          isProbablyThreadReply ? "THREAD REPLY" :
                                          isProbablySearch ? "SEARCH BOX" : "INPUT BOX";

                            Console.WriteLine($"    ✅✅✅ SLACK {inputType} ACCEPTED!");
                            Console.WriteLine($"        Size: {width:F0}x{height:F0}px - VALID");
                            Console.WriteLine($"        Patterns: ValuePattern={hasEditableValuePattern}, TextPattern={hasTextPattern}");
                            return true;
                        }
                        else if (!isReasonableSize)
                        {
                            Console.WriteLine($"    ❌ SLACK ELEMENT REJECTED - Size out of range");
                            Console.WriteLine($"        {width:F0}x{height:F0}px is too large/small for input box");
                            return false;
                        }
                        else
                        {
                            Console.WriteLine($"    ❌ SLACK ELEMENT REJECTED - No editable pattern");
                            Console.WriteLine($"        Neither ValuePattern nor TextPattern is editable");
                            return false;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"    ⚠ SLACK: Could not get bounds, attempting pattern-based acceptance");

                        // If we can't get bounds but patterns suggest it's editable, accept it
                        if (matchesSlackPattern && (hasEditableValuePattern || hasTextPattern))
                        {
                            Console.WriteLine($"    ✅ SLACK INPUT ACCEPTED (pattern-based, no size check)");
                            return true;
                        }
                    }
                }

                // ===== DISCORD / TEAMS DETECTION =====
                if (isDiscord || isTeams)
                {
                    var appName = isDiscord ? "DISCORD" : "TEAMS";
                    Console.WriteLine($"    🎯 {appName} DETECTED - Performing analysis...");

                    // Check for editable patterns
                    bool isEditable = false;
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                    {
                        var pattern = (ValuePattern)valuePattern;
                        if (!pattern.Current.IsReadOnly)
                        {
                            isEditable = true;
                            Console.WriteLine($"        ✓ Has editable ValuePattern");
                        }
                    }

                    if (!isEditable && element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
                    {
                        isEditable = true;
                        Console.WriteLine($"        ✓ Has TextPattern (alternative)");
                    }

                    if (isEditable && rect.HasValue)
                    {
                        var width = rect.Value.Width;
                        var height = rect.Value.Height;

                        // Similar size constraints as Slack
                        if (width >= 100 && width < 2000 && height >= 20 && height < 600)
                        {
                            Console.WriteLine($"    ✅✅✅ {appName} INPUT ACCEPTED!");
                            Console.WriteLine($"        Size: {width:F0}x{height:F0}px - VALID");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"    ❌ {appName} REJECTED - Size {width:F0}x{height:F0}px invalid");
                        }
                    }
                }

                // ===== BROWSER TEXTAREA DETECTION =====
                if (isBrowser)
                {
                    var browserName = isChrome ? "CHROME" : isEdge ? "EDGE" : "FIREFOX";
                    Console.WriteLine($"    🌐 {browserName} BROWSER DETECTED - Checking for textarea...");

                    // Browser textareas typically have:
                    // - ValuePattern (editable)
                    // - Reasonable size
                    // - May have aria-role or specific automation IDs

                    bool hasEditableValuePattern = false;
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                    {
                        var pattern = (ValuePattern)valuePattern;
                        if (!pattern.Current.IsReadOnly)
                        {
                            hasEditableValuePattern = true;
                            Console.WriteLine($"        ✓ Has editable ValuePattern");
                        }
                    }

                    bool hasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out object? _);

                    if (rect.HasValue && (hasEditableValuePattern || hasTextPattern))
                    {
                        var width = rect.Value.Width;
                        var height = rect.Value.Height;

                        // Browser textareas: 100-2000px wide, 30-500px tall
                        if (width >= 100 && width < 2000 && height >= 30 && height < 500)
                        {
                            Console.WriteLine($"    ✅✅✅ {browserName} TEXTAREA ACCEPTED!");
                            Console.WriteLine($"        Size: {width:F0}x{height:F0}px - VALID");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"    ❌ {browserName} REJECTED - Size {width:F0}x{height:F0}px invalid");
                        }
                    }
                }

                // ===== GENERIC DOCUMENT CONTROL (fallback) =====
                Console.WriteLine($"    📄 Generic Document control check (non-Slack/Discord/Teams/Browser)...");

                // Must have ValuePattern to read/write text
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? genericValuePattern))
                {
                    var pattern = (ValuePattern)genericValuePattern;

                    // Must NOT be read-only (filters out display areas)
                    if (!pattern.Current.IsReadOnly)
                    {
                        Console.WriteLine($"        ✓ Has editable ValuePattern");

                        // Additional safety: Check reasonable size for input box
                        if (rect.HasValue)
                        {
                            var width = rect.Value.Width;
                            var height = rect.Value.Height;

                            // Generic input boxes: typically < 2000px wide and < 500px tall
                            if (width < 2000 && height < 500)
                            {
                                Console.WriteLine($"    ✅ Generic editable Document ACCEPTED");
                                Console.WriteLine($"        Process: {processName}");
                                Console.WriteLine($"        Size: {width:F0}x{height:F0}px - VALID");
                                return true;
                            }
                            else
                            {
                                Console.WriteLine($"    ❌ Document REJECTED - Too large ({width:F0}x{height:F0}px)");
                                Console.WriteLine($"        Likely a display area, not an input box");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"    ❌ Document REJECTED - ValuePattern is read-only");
                    }
                }
                else
                {
                    Console.WriteLine($"    ❌ Document REJECTED - No ValuePattern available");
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ❌ Exception in IsEditableDocumentControl: {ex.Message}");
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
