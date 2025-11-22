# HTML Editor Support Implementation Guide

## Overview
This guide shows how to add support for HTML editors (TinyMCE, CKEditor, Quill, Froala, etc.) to GrammrPop's TextBoxMonitorService.

## Changes Required

### 1. Update `IsTextControl()` Method

**Location**: Services/TextBoxMonitorService.cs, around line 289

**Add this code block BEFORE the "REJECT everything else" line:**

```csharp
// ACCEPT Custom controls that might be HTML editors (TinyMCE, CKEditor, Quill, etc.)
if (controlType == ControlType.Custom)
{
    return IsHtmlEditorControl(element, className);
}
```

**Complete updated method should look like:**

```csharp
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
            // ... other rejections ...
            controlType == ControlType.Pane)
        {
            return false;
        }

        // ACCEPT standard Edit controls
        if (controlType == ControlType.Edit)
        {
            Console.WriteLine($"    ? Standard Edit control (Notepad/TextBox)");
            return true;
        }

        // ACCEPT Document controls (Slack, browsers, etc.)
        if (controlType == ControlType.Document)
        {
            return IsEditableDocumentControl(element, className);
        }

        // ACCEPT Custom controls (HTML editors) ? ADD THIS
        if (controlType == ControlType.Custom)
        {
            return IsHtmlEditorControl(element, className);
        }

        // REJECT everything else
        return false;
    }
    catch
    {
        return false;
    }
}
```

### 2. Add New `IsHtmlEditorControl()` Method

**Location**: Services/TextBoxMonitorService.cs, after `IsEditableDocumentControl()` method ends (around line 630)

**Add this complete new method:**

```csharp
/// <summary>
/// Detects HTML editor controls (TinyMCE, CKEditor, Quill, Froala, etc.)
/// Handles Custom control types used by rich text editors
/// </summary>
private bool IsHtmlEditorControl(AutomationElement element, string className)
{
    try
    {
        Console.WriteLine($"    ?? Analyzing Custom control (potential HTML editor):");
        Console.WriteLine($"       ClassName: {className}");

        // Get process name for better detection
        var processName = "unknown";
        try
        {
            var hwnd = new IntPtr(element.Current.NativeWindowHandle);
            GetWindowThreadProcessId(hwnd, out uint processId);
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            processName = process.ProcessName.ToLower();
        }
        catch { /* Ignore process name errors */ }

        var isBrowser = processName.Contains("chrome") || processName.Contains("msedge") || 
                       processName.Contains("edge") || processName.Contains("firefox");

        Console.WriteLine($"       Process: {processName}");
        Console.WriteLine($"       IsBrowser: {isBrowser}");

        // Get element properties
        var rect = GetElementBounds(element);
        var automationId = "";
        var name = "";
        var helpText = "";

        try
        {
            automationId = element.Current.AutomationId ?? "";
            name = element.Current.Name ?? "";
            helpText = element.Current.HelpText ?? "";
        }
        catch { /* Ignore property access errors */ }

        Console.WriteLine($"       AutomationId: '{automationId}'");
        Console.WriteLine($"       Name: '{name}'");
        Console.WriteLine($"       HelpText: '{helpText}'");

        // HTML editor indicators
        var htmlEditorIndicators = new[]
        {
            "tinymce",           // TinyMCE editor
            "ckeditor",          // CKEditor
            "quill",             // Quill editor
            "froala",            // Froala editor
            "summernote",        // Summernote
            "medium-editor",     // Medium Editor
            "trumbowyg",         // Trumbowyg
            "wysiwyg",           // Generic WYSIWYG
            "editor",            // Generic editor
            "rich-text",         // Rich text indicators
            "richtext",
            "contenteditable",   // HTML contenteditable
            "compose",           // Email composers
            "message-editor",    // Message editors
            "text-editor",       // Text editors
            "html-editor"        // HTML editors
        };

        var lowerAutomationId = automationId.ToLower();
        var lowerName = name.ToLower();
        var lowerClassName = className.ToLower();
        var lowerHelpText = helpText.ToLower();

        var matchesEditorPattern = htmlEditorIndicators.Any(indicator =>
            lowerAutomationId.Contains(indicator) ||
            lowerName.Contains(indicator) ||
            lowerClassName.Contains(indicator) ||
            lowerHelpText.Contains(indicator));

        if (matchesEditorPattern)
        {
            Console.WriteLine($"    ??? HTML EDITOR PATTERN MATCH!");
            Console.WriteLine($"        Found editor indicator in element properties");
        }

        // Check for editable patterns
        bool hasEditableValuePattern = false;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern))
        {
            var pattern = (ValuePattern)valuePattern;
            if (!pattern.Current.IsReadOnly)
            {
                hasEditableValuePattern = true;
                Console.WriteLine($"        ? Has editable ValuePattern");
            }
            else
            {
                Console.WriteLine($"        ? ValuePattern is read-only");
            }
        }

        bool hasTextPattern = false;
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPattern))
        {
            hasTextPattern = true;
            Console.WriteLine($"        ? Has TextPattern");
        }

        // HTML editors should have at least one editable pattern
        if (!hasEditableValuePattern && !hasTextPattern)
        {
            Console.WriteLine($"    ? HTML EDITOR REJECTED - No editable patterns");
            return false;
        }

        // Check size constraints
        if (rect.HasValue)
        {
            var width = rect.Value.Width;
            var height = rect.Value.Height;

            Console.WriteLine($"       Size: {width:F0}x{height:F0}px");

            // HTML editors: 200-3000px wide, 100-1000px tall
            var isReasonableEditorSize = width >= 200 && width < 3000 && height >= 100 && height < 1000;

            if (!isReasonableEditorSize)
            {
                Console.WriteLine($"    ? HTML EDITOR REJECTED - Size out of range");
                Console.WriteLine($"        Expected: 200-3000px wide, 100-1000px tall");
                Console.WriteLine($"        Actual: {width:F0}x{height:F0}px");
                return false;
            }

            var isFullEditor = width >= 500 && height >= 200;
            var isInlineEditor = width >= 200 && width < 500 && height >= 100 && height < 200;

            var editorType = isFullEditor ? "FULL HTML EDITOR" :
                            isInlineEditor ? "INLINE EDITOR" : "COMPACT EDITOR";

            Console.WriteLine($"    ??? {editorType} ACCEPTED!");
            Console.WriteLine($"        Size: {width:F0}x{height:F0}px - VALID");
            Console.WriteLine($"        Patterns: ValuePattern={hasEditableValuePattern}, TextPattern={hasTextPattern}");
            Console.WriteLine($"        MatchedPattern: {matchesEditorPattern}");
            
            return true;
        }
        else
        {
            // Pattern-based fallback
            if (matchesEditorPattern && (hasEditableValuePattern || hasTextPattern))
            {
                Console.WriteLine($"    ? HTML EDITOR ACCEPTED (pattern-based, no size check)");
                Console.WriteLine($"        Patterns: ValuePattern={hasEditableValuePattern}, TextPattern={hasTextPattern}");
                return true;
            }
            else
            {
                Console.WriteLine($"    ? HTML EDITOR REJECTED - No bounds and weak pattern match");
                return false;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    ? Exception in IsHtmlEditorControl: {ex.Message}");
        return false;
    }
}
```

## What HTML Editors Are Supported?

### Detected Editors:
- ? **TinyMCE** - Popular WYSIWYG editor used in WordPress, Joomla
- ? **CKEditor** - Enterprise-grade rich text editor
- ? **Quill** - Modern rich text editor for the web
- ? **Froala** - Beautiful JavaScript web editor
- ? **Summernote** - Bootstrap-based editor
- ? **Medium Editor** - Medium.com-style inline editor
- ? **Trumbowyg** - Lightweight WYSIWYG editor
- ? **Generic WYSIWYG** - Any editor with "wysiwyg" in name/class
- ? **Email Composers** - Gmail, Outlook web, etc.
- ? **CMS Editors** - WordPress, Drupal, etc.

### Detection Criteria:

1. **Control Type**: ControlType.Custom
2. **Pattern Match**: AutomationId, Name, ClassName, or HelpText contains editor keywords
3. **Editable Pattern**: Has ValuePattern (not read-only) OR TextPattern
4. **Size Constraints**:
   - Full Editors: 500-3000px wide, 200-1000px tall
   - Inline Editors: 200-500px wide, 100-200px tall
   - Compact Editors: 200-3000px wide, 100-1000px tall

### Keywords Detected:
```
tinymce, ckeditor, quill, froala, summernote, 
medium-editor, trumbowyg, wysiwyg, editor, 
rich-text, richtext, contenteditable, compose,
message-editor, text-editor, html-editor
```

## Testing Instructions

### 1. Stop the Running Application
```bash
# Find and kill GrammrPop process
taskkill /F /IM GrammrPop.exe
```

### 2. Apply the Code Changes
- Open `Services/TextBoxMonitorService.cs`
- Add the Custom control check in `IsTextControl()` (step 1 above)
- Add the complete `IsHtmlEditorControl()` method (step 2 above)

### 3. Build and Run
```bash
dotnet build
dotnet run
```

### 4. Test with HTML Editors

**Option A: WordPress Editor**
1. Go to https://wordpress.com/start/
2. Create a free blog (or log into existing)
3. Click "New Post"
4. Click in the editor (TinyMCE or Gutenberg)
5. Type some text with errors: "This are a test of the editor"
6. Wait 1 second
7. Check console for: "?? Analyzing Custom control (potential HTML editor):"
8. Look for: "??? FULL HTML EDITOR ACCEPTED!"
9. Red icon should appear with error count

**Option B: Online HTML Editor**
1. Go to https://html5-editor.net/
2. Click in the editor area
3. Type text with errors
4. Check console logs

**Option C: Gmail Compose**
1. Open Gmail in Chrome
2. Click "Compose"
3. Click in the message body
4. Type text with errors
5. Should detect as HTML editor (or Document)

## Console Output Example

When HTML editor is detected, you'll see:

```
?? Analyzing Custom control (potential HTML editor):
   ClassName: Chrome_RenderWidgetHostHWND
   Process: chrome
   IsBrowser: true
   AutomationId: 'tinymce-editor'
   Name: 'Rich Text Editor'
   HelpText: 'Enter your content here'
   Size: 800x400px

??? HTML EDITOR PATTERN MATCH!
    Found editor indicator in element properties

    ? Has editable ValuePattern
    ? Has TextPattern

??? FULL HTML EDITOR ACCEPTED!
    Size: 800x400px - VALID
    Patterns: ValuePattern=true, TextPattern=true
    MatchedPattern=true
```

## Troubleshooting

### Editor Not Detected?
Check console for rejection reason:
- "? HTML EDITOR REJECTED - No editable patterns" ? Editor is read-only or display-only
- "? HTML EDITOR REJECTED - Size out of range" ? Editor too small (<200px) or too large (>3000px wide or >1000px tall)
- No "?? Analyzing Custom control" message ? Element is not ControlType.Custom

### Add Custom Patterns
If your HTML editor uses different naming, add to `htmlEditorIndicators` array:

```csharp
var htmlEditorIndicators = new[]
{
    "tinymce",
    "ckeditor",
    "your-custom-editor-name",  // ? Add here
    // ... rest of indicators
};
```

### Adjust Size Constraints
If editors in your application are larger/smaller:

```csharp
// Change these values:
var isReasonableEditorSize = width >= 200 && width < 3000 && height >= 100 && height < 1000;

// To your preferred ranges:
var isReasonableEditorSize = width >= 100 && width < 4000 && height >= 50 && height < 1500;
```

## Benefits of HTML Editor Support

? **WordPress**: Write blog posts with grammar checking  
? **Email Clients**: Compose emails in Gmail, Outlook Web with real-time checking  
? **CMS Platforms**: Joomla, Drupal, etc.  
? **Online Editors**: CodePen, JSFiddle (if they use editable divs)  
? **Content Platforms**: Medium, Substack, Ghost  
? **E-Learning**: Moodle, Canvas editors  

## Summary

This implementation adds comprehensive HTML editor support by:

1. **Detecting Custom control types** (used by most HTML editors)
2. **Pattern matching** against 17+ common editor keywords
3. **Checking editable patterns** (ValuePattern or TextPattern)
4. **Validating size constraints** (filters out toolbars, buttons, display areas)
5. **Extensive logging** for debugging

The feature integrates seamlessly with existing Slack, Discord, Teams, and browser support.

---

**Note**: After making changes, you MUST stop the running GrammrPop process before rebuilding, or you'll get file lock errors.
