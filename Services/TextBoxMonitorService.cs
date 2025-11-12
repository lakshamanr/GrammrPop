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
        private AutomationElement? _lastFocusedElement;
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
                Interval = TimeSpan.FromMilliseconds(300) // Check every 300ms
            };
            _monitorTimer.Tick += MonitorTimer_Tick;
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

                // Check if it's a different element
                lock (_lockObject)
                {
                    if (IsSameElement(focusedElement, _lastFocusedElement))
                        return;

                    _lastFocusedElement = focusedElement;
                }

                // Check if it's a text control
                var isTextControl = IsTextControl(focusedElement);
                var controlType = focusedElement.Current.ControlType.ProgrammaticName;
                var className = focusedElement.Current.ClassName;

                System.Diagnostics.Debug.WriteLine($"Focused: {controlType}, Class: {className}, IsText: {isTextControl}");

                if (isTextControl)
                {
                    // Get control information
                    var rect = GetElementBounds(focusedElement);
                    var text = GetElementText(focusedElement);

                    System.Diagnostics.Debug.WriteLine($"  -> Bounds: {rect?.Width}x{rect?.Height}, TextLen: {text?.Length ?? 0}");

                    if (rect.HasValue && rect.Value.Width > 20 && rect.Value.Height > 10)
                    {
                        TextBoxFocused?.Invoke(this, new TextBoxDetectedEventArgs
                        {
                            Element = focusedElement,
                            Bounds = rect.Value,
                            Text = text
                        });
                    }
                }
                else
                {
                    HandleFocusLost();
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

        private void HandleFocusLost()
        {
            lock (_lockObject)
            {
                if (_lastFocusedElement != null)
                {
                    _lastFocusedElement = null;
                    TextBoxLostFocus?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _monitorTimer.Tick -= MonitorTimer_Tick;
        }
    }

    public class TextBoxDetectedEventArgs : EventArgs
    {
        public AutomationElement Element { get; set; } = null!;
        public Rect Bounds { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
