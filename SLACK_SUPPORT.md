# GrammrPop - Comprehensive Slack Support Documentation

## Overview

GrammrPop now includes **deep, specialized support for Slack** with extensive detection logic, detailed logging, and multiple fallback mechanisms. This document explains how the Slack integration works in detail.

---

## Architecture

### Detection Pipeline

```
1. UI Automation detects focused element (300ms polling)
   ↓
2. IsTextControl() checks if element is ControlType.Document
   ↓
3. IsEditableDocumentControl() performs deep analysis
   ↓
4. Process name identified → Slack detected
   ↓
5. Multi-criteria analysis (AutomationId, patterns, size)
   ↓
6. Accept/Reject with detailed logging
   ↓
7. Grammar checking begins (0.8s after typing stops)
```

---

## How Slack Detection Works

### 1. Process Identification

**Method**: Win32 `GetWindowThreadProcessId()` API
**Detection**: Process name contains "slack"

```csharp
var hwnd = new IntPtr(element.Current.NativeWindowHandle);
GetWindowThreadProcessId(hwnd, out uint processId);
var process = System.Diagnostics.Process.GetProcessById((int)processId);
var processName = process.ProcessName.ToLower();
var isSlack = processName.Contains("slack");
```

**Detects**:
- `slack.exe` (Windows desktop app)
- `Slack.exe` (case-insensitive)
- Any Slack-branded process

---

### 2. Element Property Inspection

GrammrPop inspects **all available UI Automation properties** for comprehensive analysis:

| Property | Purpose | Example Value |
|----------|---------|---------------|
| **AutomationId** | Unique identifier for control | `"message-input-field"` |
| **Name** | Accessible name | `"Message #general"` |
| **HelpText** | Tooltip/help text | `"Type a message"` |
| **ClassName** | Window class | `"Chrome_RenderWidgetHostHWND"` |
| **AriaRole** | ARIA role (web accessibility) | `"textbox"` |
| **Size** | Width x Height in pixels | `650x120px` |
| **Position** | X, Y coordinates | `(100, 850)` |

---

### 3. Slack Input Type Recognition

GrammrPop recognizes **multiple types of Slack inputs** by matching against known patterns:

#### Pattern Matching (AutomationId/Name/HelpText)

```csharp
var slackIndicators = new[]
{
    "message-input",        // Main channel message input
    "composer",             // Rich text composer area
    "msg_input",            // Alternative message input field
    "search",               // Search boxes
    "message_input",        // Underscore variant
    "reply",                // Thread reply boxes
    "composer-input"        // Hyphenated composer variant
};

var matchesSlackPattern = slackIndicators.Any(indicator =>
    automationId.ToLower().Contains(indicator) ||
    name.ToLower().Contains(indicator) ||
    helpText.ToLower().Contains(indicator));
```

**Supported Slack Inputs**:
- ✅ **Main channel message box** (e.g., #general, #random)
- ✅ **Thread replies** (reply to messages in threads)
- ✅ **Direct messages** (DM conversations)
- ✅ **Search box** (top-right search)
- ✅ **Composer mode** (rich text editor)
- ✅ **Edit message** (editing existing messages)

---

### 4. Automation Pattern Detection

Slack uses **rich text controls** that expose multiple UI Automation patterns. GrammrPop checks both:

#### A. ValuePattern (Primary)

**Used For**: Reading and writing text content
**Check**: Must NOT be read-only

```csharp
if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
{
    var pattern = (ValuePattern)valuePattern;
    if (!pattern.Current.IsReadOnly)
    {
        // ✅ Can read and write text - this is an input box!
        hasEditableValuePattern = true;
    }
}
```

**Why This Matters**:
- Slack message **input boxes** have editable ValuePattern
- Slack message **display area** has read-only ValuePattern
- This is the **primary filter** to avoid detecting display areas

#### B. TextPattern (Fallback)

**Used For**: Alternative text access for rich text controls
**Check**: DocumentRange must be accessible

```csharp
if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
{
    var pattern = (TextPattern)textPattern;
    try
    {
        var range = pattern.DocumentRange;
        if (range != null)
        {
            // ✅ Has TextPattern - can be used as fallback
            hasTextPattern = true;
        }
    }
    catch { /* DocumentRange may fail for some controls */ }
}
```

**Why Both Patterns**:
- Some Slack inputs only expose ValuePattern
- Some rich text editors only expose TextPattern
- Checking both ensures maximum compatibility

---

### 5. Size-Based Input Type Detection

Slack inputs have **characteristic size ranges** that help identify their purpose:

#### Main Message Input

**Typical Size**: 400-1500px wide, 40-300px tall

```csharp
var isProbablyMessageInput = width >= 300 && width < 1600 && height >= 30 && height < 350;
```

**Characteristics**:
- Spans most of the bottom of Slack window
- Expands vertically as you type multiple lines
- Usually the largest input box in Slack

#### Thread Reply

**Typical Size**: 300-1000px wide, 40-200px tall

```csharp
var isProbablyThreadReply = width >= 250 && width < 1200 && height >= 30 && height < 250;
```

**Characteristics**:
- Appears in right-side thread panel
- Narrower than main message input
- Similar height range but more constrained

#### Search Box

**Typical Size**: 200-600px wide, 30-50px tall

```csharp
var isProbablySearch = width >= 150 && width < 800 && height >= 20 && height < 80;
```

**Characteristics**:
- Top-right corner of Slack
- Single-line input (shorter height)
- Fixed size, doesn't expand

#### Overall Size Constraints

**Rejection Criteria**:
```csharp
// Too small: likely a button or label
if (width < 100 || height < 20) → REJECT

// Too large: likely the message display area
if (width >= 2000 || height >= 600) → REJECT
```

**Why Size Matters**:
- Filters out the **message display area** (full window size)
- Filters out **buttons and labels** (too small)
- Identifies **specific input types** for better logging

---

### 6. Multi-Criteria Acceptance Logic

An element is accepted as a **Slack input** if **ALL** of the following are true:

```
✓ Process name contains "slack"
  AND
✓ Size is reasonable (100-2000px wide, 20-600px tall)
  AND
✓ Has editable ValuePattern OR has TextPattern
```

**Optional** (improves confidence but not required):
- AutomationId/Name/HelpText matches known patterns

---

## Console Logging

### Full Example Output

When you click in a Slack message box, you'll see:

```
🔍 Analyzing Document control:
   Process: slack (PID: 15432)
   ClassName: Chrome_RenderWidgetHostHWND
   IsSlack: true, IsBrowser: false, IsDiscord: false, IsTeams: false
   AutomationId: 'message-input-for-C1234567890'
   Name: 'Message #general'
   HelpText: 'Type your message here'
   AriaRole: 'textbox'
   Size: 650x120px
   Position: (95, 825)

🎯 SLACK DETECTED - Performing deep Slack analysis...

✓✓✓ SLACK INPUT IDENTIFIED (pattern match)!
    Matched pattern in AutomationId/Name/HelpText

    ✓ Has editable ValuePattern (can read/write)
    ✓ Has TextPattern with DocumentRange

    Size analysis:
      IsReasonableSize: true
      IsProbablyMessageInput: true
      IsProbablyThreadReply: false
      IsProbablySearch: false

✅✅✅ SLACK MESSAGE INPUT ACCEPTED!
    Size: 650x120px - VALID
    Patterns: ValuePattern=true, TextPattern=true
```

### Rejection Examples

#### Example 1: Read-Only Display Area

```
🔍 Analyzing Document control:
   Process: slack (PID: 15432)
   Size: 1200x800px

🎯 SLACK DETECTED - Performing deep Slack analysis...
    ✗ ValuePattern is read-only

❌ SLACK ELEMENT REJECTED - Size out of range
    1200x800px is too large/small for input box
```

#### Example 2: Too Large (Message Display)

```
🔍 Analyzing Document control:
   Process: slack (PID: 15432)
   Size: 1920x1080px

🎯 SLACK DETECTED - Performing deep Slack analysis...
    ✓ Has editable ValuePattern (can read/write)

    Size analysis:
      IsReasonableSize: false
      IsProbablyMessageInput: false

❌ SLACK ELEMENT REJECTED - Size out of range
    1920x1080px is too large/small for input box
```

---

## Special Cases

### Case 1: Slack Without Size Information

**Scenario**: Element bounds cannot be retrieved

**Fallback Logic**:
```csharp
if (matchesSlackPattern && (hasEditableValuePattern || hasTextPattern))
{
    // Accept based on patterns alone
    Console.WriteLine($"✅ SLACK INPUT ACCEPTED (pattern-based, no size check)");
    return true;
}
```

**Why**: Some UI elements may not report bounds correctly. If the AutomationId/Name strongly suggests it's a Slack input AND it has editable patterns, we accept it.

---

### Case 2: Slack Thread Replies

**Challenge**: Thread reply boxes are in a separate panel and have different sizes

**Solution**:
- Dedicated size range: 250-1200px wide, 30-250px tall
- Same pattern matching as main input
- Logged as "THREAD REPLY" for easy identification

---

### Case 3: Slack Search Box

**Challenge**: Search box is much smaller and single-line

**Solution**:
- Smaller size range: 150-800px wide, 20-80px tall
- Pattern matching for "search" in properties
- Logged as "SEARCH BOX"

---

### Case 4: Slack Composer (Rich Text Mode)

**Challenge**: Rich text composer may use different patterns

**Solution**:
- TextPattern support (in addition to ValuePattern)
- Pattern matching for "composer"
- DocumentRange check for text access

---

## Text Extraction

Once a Slack input is detected, GrammrPop extracts text using multiple methods:

### Method 1: ValuePattern (Primary)

```csharp
if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
{
    var pattern = (ValuePattern)valuePattern;
    var text = pattern.Current.Value ?? string.Empty;
    if (!string.IsNullOrEmpty(text))
        return text; // ✅ Got text!
}
```

**Pros**: Fast, reliable, works for most inputs
**Cons**: May not work for rich text with formatting

---

### Method 2: TextPattern (Fallback)

```csharp
if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
{
    var pattern = (TextPattern)textPattern;
    var range = pattern.DocumentRange;
    var text = range.GetText(-1) ?? string.Empty; // -1 = get all text
    if (!string.IsNullOrEmpty(text))
        return text; // ✅ Got text!
}
```

**Pros**: Works for rich text controls
**Cons**: Slightly slower, may include formatting markers

---

### Method 3: WM_GETTEXT (Last Resort)

```csharp
var hwnd = new IntPtr(element.Current.NativeWindowHandle);
int length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
if (length > 0)
{
    StringBuilder sb = new StringBuilder(length + 1);
    SendMessage(hwnd, WM_GETTEXT, new IntPtr(sb.Capacity), sb);
    return sb.ToString(); // ✅ Got text!
}
```

**Pros**: Works when automation patterns fail
**Cons**: Win32 API, may not work for all controls

---

## Grammar Checking Flow

### 1. Text Change Detection

```
User types in Slack → MonitorTimer_Tick (every 300ms)
→ CheckForTextChanges()
→ Text different from previous? → Restart debounce timer
```

### 2. Debounced Grammar Check

```
User stops typing → Wait 0.8 seconds → TextCheckTimer_Tick()
→ Extract full text → Send to LanguageTool API
→ Receive results → Show icon with error count
```

### 3. Icon Positioning

```
Get Slack input bounds (X, Y, Width, Height)
→ Position icon at bottom-right corner: (X + Width - 50, Y + Height - 50)
→ Show red icon with orange badge (error count)
```

---

## Performance Considerations

### Polling Frequency

**300ms polling** is fast enough to feel responsive while not overwhelming the CPU:

```
1 second = 3-4 checks
Typing at 60 WPM = ~5 characters per second
Detection lag: < 300ms (barely noticeable)
```

### Debounce Delay

**0.8 second delay** after typing stops prevents excessive API calls:

```
User types: "hello wor"
→ 0.8s timer starts
User continues: "hello world"
→ Timer resets
User stops typing
→ 0.8s later: Grammar check fires
Result: 1 API call instead of 10+
```

### Text Extraction Caching

**Current text cached** to avoid redundant checks:

```csharp
if (_currentText == _lastCheckedText || string.IsNullOrWhiteSpace(_currentText))
{
    Console.WriteLine("⏭️  Skipping check (text unchanged or empty)");
    return;
}
```

---

## Troubleshooting

### Problem: Slack input not detected

**Check Console Output**:
```
❌ Document REJECTED - No ValuePattern available
```

**Solution**: Slack may be using a different control type. Check:
1. Process name is "slack" (case-insensitive)
2. Element has ValuePattern or TextPattern
3. Element is not read-only
4. Element size is reasonable (100-2000px wide, 20-600px tall)

---

### Problem: Message display area being detected

**Check Console Output**:
```
❌ SLACK ELEMENT REJECTED - Size out of range
    1920x1080px is too large/small for input box
```

**Solution**: This is correct behavior! Size filter prevents this.

---

### Problem: Grammar check not triggering

**Check Console Output**:
```
📝 TEXT CHANGED!
   ⚡ Starting 0.8-second countdown before grammar check...

⏭️  Skipping check (text unchanged or empty)
```

**Possible Causes**:
1. Text hasn't changed since last check
2. Text is empty or whitespace-only
3. API call failed (check internet connection)
4. LanguageTool rate limit exceeded (20 req/min on free tier)

---

### Problem: Icon appears in wrong location

**Check Console Output**:
```
📍 SHOWING ICON: 1 errors found at position (823, 456)
```

**Possible Causes**:
1. Slack window moved between detection and icon display
2. Slack input box resized (e.g., expanded for multiline)
3. Multi-monitor setup with different DPI scaling

**Solution**: Icon position is recalculated each time grammar check completes, so it should auto-correct within 0.8 seconds.

---

## Advanced Configuration

### Adjusting Size Thresholds

If Slack inputs are not being detected, you can modify the size ranges in `IsEditableDocumentControl()`:

```csharp
// Current values:
var isReasonableSize = width >= 100 && width < 2000 && height >= 20 && height < 600;

// More permissive (if some inputs are too small/large):
var isReasonableSize = width >= 50 && width < 3000 && height >= 15 && height < 800;
```

### Adding Custom Slack Patterns

If Slack uses different AutomationIds in your environment:

```csharp
var slackIndicators = new[]
{
    "message-input",
    "composer",
    "msg_input",
    "search",
    "message_input",
    "reply",
    "composer-input",
    "your-custom-pattern-here"  // ← Add your pattern
};
```

### Disabling Size Checks (Not Recommended)

For debugging purposes, you can temporarily disable size checks:

```csharp
// Accept ANY size (WARNING: Will accept message display areas!)
var isReasonableSize = true; // Force acceptance
```

---

## API Reference

### IsEditableDocumentControl()

**Signature**:
```csharp
private bool IsEditableDocumentControl(AutomationElement element, string className)
```

**Parameters**:
- `element`: The UI Automation element to inspect
- `className`: The window class name (for additional context)

**Returns**:
- `true`: Element is an editable Document control (Slack input, browser textarea, etc.)
- `false`: Element is not an input (display area, button, etc.)

**Process**:
1. Extract process name via `GetWindowThreadProcessId()`
2. Detect application type (Slack, Discord, Teams, browser)
3. Inspect AutomationElement properties (AutomationId, Name, HelpText, Size)
4. Check automation patterns (ValuePattern, TextPattern, LegacyIAccessiblePattern)
5. Apply application-specific logic (Slack has different criteria than browsers)
6. Log detailed decision rationale
7. Return acceptance decision

---

## Summary

GrammrPop's Slack support is **comprehensive and robust**:

### ✅ What's Supported

- ✅ Main channel message inputs (#general, #random, etc.)
- ✅ Direct message inputs (1-on-1 and group DMs)
- ✅ Thread replies (side panel)
- ✅ Search boxes
- ✅ Rich text composer mode
- ✅ Edit message mode
- ✅ Multiple size ranges (message input, thread reply, search)
- ✅ Dual pattern support (ValuePattern + TextPattern)
- ✅ Pattern-based fallback (when size unavailable)
- ✅ Extensive console logging for debugging

### ❌ What's Filtered Out

- ❌ Message display area (read-only, too large)
- ❌ Sidebar channels (not inputs)
- ❌ Buttons and menus (wrong control type)
- ❌ Headers and labels (too small, no patterns)

### 🔍 Debugging Features

- 🔍 Process identification logged
- 🔍 All AutomationElement properties logged
- 🔍 Pattern availability logged
- 🔍 Size analysis logged
- 🔍 Accept/reject decision explained
- 🔍 Input type identified (MESSAGE INPUT, THREAD REPLY, SEARCH BOX)

---

## Testing Checklist

Use this checklist to verify Slack support:

### Basic Functionality

- [ ] Open Slack desktop app
- [ ] Click in #general message input box
- [ ] Console shows: "🎯 SLACK DETECTED"
- [ ] Console shows: "✅✅✅ SLACK MESSAGE INPUT ACCEPTED!"
- [ ] Type: "I has a error in this sentence"
- [ ] Wait 1 second
- [ ] Red icon appears at bottom-right of input box
- [ ] Icon shows badge with "2" (error count)
- [ ] Click icon → Popup shows 2 corrections

### Thread Replies

- [ ] Click on any message to open thread
- [ ] Click in thread reply box (right side panel)
- [ ] Console shows: "✅✅✅ SLACK THREAD REPLY ACCEPTED!"
- [ ] Type message with errors
- [ ] Icon appears correctly

### Search Box

- [ ] Click in search box (top-right)
- [ ] Console shows: "✅✅✅ SLACK SEARCH BOX ACCEPTED!"
- [ ] Type search query with errors
- [ ] Icon appears

### Direct Messages

- [ ] Open DM conversation
- [ ] Click in message input
- [ ] Type message with errors
- [ ] Icon appears

### No False Positives

- [ ] Click in message display area (NOT input box)
- [ ] Console shows: "❌ SLACK ELEMENT REJECTED"
- [ ] No icon appears (correct behavior)

---

## Future Enhancements

Potential improvements for Slack support:

1. **Slack-specific grammar rules** (e.g., allow @mentions without grammar checks)
2. **Emoji handling** (don't flag emojis as spelling errors)
3. **Code block detection** (skip grammar checking inside ``` code blocks)
4. **Slash command detection** (don't check /commands)
5. **Multi-language support** (detect Slack workspace language)
6. **Custom API endpoint** (allow Slack-specific LanguageTool configuration)

---

## Contact

For issues or questions about Slack support:
1. Check console output for detailed error messages
2. Review this documentation
3. Open GitHub issue with console logs attached

---

**Last Updated**: 2025-01-12
**Version**: 1.0.0 (Comprehensive Slack Support)
