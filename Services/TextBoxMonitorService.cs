using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace GrammrPop.Services
{
    /// <summary>
    /// Monitors focused text controls across all applications using UI Automation
    /// </summary>
    public class TextBoxMonitorService : IDisposable
    {
        private readonly DispatcherTimer _monitorTimer;
        private readonly DispatcherTimer _hideDelayTimer;
        private AutomationElement? _lastFocusedElement;
        private AutomationElement? _currentStableElement;
        private int _stableCount = 0;
        private readonly object _lockObject = new object();
        private bool _isMonitoring;

        public event EventHandler<TextBoxDetectedEventArgs>? TextBoxFocused;
        public event EventHandler? TextBoxLostFocus;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public TextBoxMonitorService()
        {
            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Reduced frequency to 500ms
            };
            _monitorTimer.Tick += MonitorTimer_Tick;

            _hideDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Wait 500ms before hiding
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
                        // Same element - no need to re-fire event
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

                                TextBoxFocused?.Invoke(this, new TextBoxDetectedEventArgs
                                {
                                    Element = focusedElement,
                                    Bounds = rect.Value,
                                    Text = text
                                });
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

                // Check for common text-input control types
                if (controlType == ControlType.Edit ||
                    controlType == ControlType.Document ||
                    controlType == ControlType.Text)
                {
                    return true;
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
                TextBoxLostFocus?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine("Icon hidden (delayed)");
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
            _hideDelayTimer.Stop();
            _monitorTimer.Tick -= MonitorTimer_Tick;
            _hideDelayTimer.Tick -= HideDelayTimer_Tick;
        }
    }

    public class TextBoxDetectedEventArgs : EventArgs
    {
        public AutomationElement Element { get; set; } = null!;
        public Rect Bounds { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
