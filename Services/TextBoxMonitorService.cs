using System;
using System.Runtime.InteropServices;
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

        public TextBoxMonitorService(LanguageToolClient grammarClient, SettingsService settingsService)
        {
            _grammarClient = grammarClient;
            _settingsService = settingsService;

            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _monitorTimer.Tick += MonitorTimer_Tick;

            _textCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Check 2 seconds after typing stops
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
                try
                {
                    var controlType = focusedElement.Current.ControlType.ProgrammaticName;
                    var className = focusedElement.Current.ClassName;
                    var name = focusedElement.Current.Name;

                    if (!isTextControl)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Rejected: Type={controlType}, Class={className}, Name={name}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✓ Accepted: Type={controlType}, Class={className}, Name={name}");
                    }
                }
                catch { /* Ignore debug logging errors */ }

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

                        // Only fire event after element has been stable for 2 polls (1 second)
                        if (_stableCount >= 2)
                        {
                            _currentStableElement = focusedElement;
                            _stableCount = 0;

                            // Get control information
                            var rect = GetElementBounds(focusedElement);
                            var text = GetElementText(focusedElement);

                            if (rect.HasValue && rect.Value.Width > 20 && rect.Value.Height > 10)
                            {
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

                // Check for common text-input control types
                if (controlType == ControlType.Edit ||
                    controlType == ControlType.Document ||
                    controlType == ControlType.Text)
                {
                    return true;
                }

                // Check for known editor control class names (Notepad++, VS Code, etc.)
                if (!string.IsNullOrEmpty(className))
                {
                    var lowerClassName = className.ToLower();

                    // Notepad++ uses Scintilla
                    // Visual Studio Code uses Chrome_RenderWidgetHostHWND
                    // Sublime Text uses Scintilla
                    // Many code editors use Scintilla
                    if (lowerClassName.Contains("scintilla") ||
                        lowerClassName.Contains("editor") ||
                        lowerClassName.Contains("chrome_renderwidgethosthwnd") ||
                        lowerClassName.Contains("textbox") ||
                        lowerClassName.Contains("richedit"))
                    {
                        System.Diagnostics.Debug.WriteLine($"  -> Detected by className: {className}");
                        return true;
                    }
                }

                // Pane controls might be custom editors
                if (controlType == ControlType.Pane)
                {
                    // Check if it has text patterns (likely an editor)
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out _) ||
                        element.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                    {
                        System.Diagnostics.Debug.WriteLine($"  -> Pane with text pattern: {className}");
                        return true;
                    }
                }

                // Also check if element supports text patterns (catches more controls)
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _) ||
                    element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                {
                    // Make sure it's not a button or non-editable element
                    var isEnabled = element.Current.IsEnabled;
                    var isPassword = element.Current.IsPassword;

                    // Skip password fields for security
                    if (isPassword)
                        return false;

                    // Skip buttons and other non-text controls
                    if (controlType == ControlType.Button ||
                        controlType == ControlType.MenuItem ||
                        controlType == ControlType.ToolBar)
                    {
                        return false;
                    }

                    return isEnabled;
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
                // Try ValuePattern first (for textboxes)
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
                {
                    var pattern = (ValuePattern)valuePattern;
                    return pattern.Current.Value ?? string.Empty;
                }

                // Try TextPattern (for rich text controls)
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
                {
                    var pattern = (TextPattern)textPattern;
                    var range = pattern.DocumentRange;
                    return range.GetText(-1) ?? string.Empty;
                }

                return string.Empty;
            }
            catch
            {
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
                    System.Diagnostics.Debug.WriteLine($"Text changed: length={newText.Length}");

                    // Restart the check timer (debouncing)
                    _textCheckTimer.Stop();
                    _textCheckTimer.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking text changes: {ex.Message}");
            }
        }

        private async void TextCheckTimer_Tick(object? sender, EventArgs e)
        {
            _textCheckTimer.Stop();

            if (_isChecking || _currentStableElement == null)
                return;

            try
            {
                // Only check if text has changed since last check
                if (_currentText == _lastCheckedText || string.IsNullOrWhiteSpace(_currentText))
                {
                    // No errors to show
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

                System.Diagnostics.Debug.WriteLine($"🔍 Auto-checking grammar for text: {_currentText.Substring(0, Math.Min(50, _currentText.Length))}...");

                var settings = _settingsService.CurrentSettings;
                var endpoint = settings.UseLocalServer ? settings.LocalServerUrl : settings.ApiEndpoint;

                var result = await _grammarClient.CheckAsync(_currentText, settings.Language, endpoint, settings.ApiKey);

                // Get bounds again in case window moved
                var rect = GetElementBounds(_currentStableElement);
                if (!rect.HasValue)
                    return;

                if (result.Matches != null && result.Matches.Length > 0)
                {
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
                System.Diagnostics.Debug.WriteLine($"Error checking grammar: {ex.Message}");
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
