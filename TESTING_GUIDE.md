# GrammrPop Testing Guide

## Quick Start Testing

### 1. Build and Run

```bash
# Navigate to project directory
cd GrammrPop

# Restore packages
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

### 2. Verify Startup

**Expected Behavior:**
- ✅ System tray icon appears (bottom-right of taskbar)
- ✅ Notification balloon shows: "GrammrPop Started - Auto-Detect: ON"
- ✅ No main window opens (app runs in background)

**If you DON'T see the tray icon:**
- Check Task Manager → Processes → Look for "GrammrPop"
- Check for error message boxes
- Run from Command Prompt/PowerShell to see console output

### 3. Test Auto-Detection

**Test Case 1: Simple Grammar Error**

1. Open **Notepad** (Windows built-in)
2. Type this text exactly: `This are a test`
3. **Wait 2-3 seconds** without typing
4. **Expected:**
   - Red icon appears at bottom-right corner of Notepad window
   - Orange badge shows "1" (error count)

**Test Case 2: Multiple Errors**

1. Clear Notepad
2. Type: `I has many errors in my text and it dont look good`
3. Wait 2-3 seconds
4. **Expected:**
   - Red icon appears with error count "3" or more

**Test Case 3: No Errors**

1. Clear Notepad
2. Type: `This is a perfect sentence.`
3. Wait 2-3 seconds
4. **Expected:**
   - No icon appears (text is correct)

**Test Case 4: Notepad++**

1. Open Notepad++ (if installed)
2. Type: `There are a problem`
3. Wait 2-3 seconds
4. **Expected:**
   - Red icon appears with error count

### 4. Test Icon Click

1. After icon appears, **click the red icon**
2. **Expected:**
   - Icon disappears
   - Popup window opens
   - Text is pre-loaded
   - Corrections are already shown
   - Can select/deselect corrections
   - Click "Apply" to fix text

### 5. Test Global Hotkey

1. Close any open Notepad windows
2. Press **Ctrl+Alt+G** from anywhere
3. **Expected:**
   - Popup window appears
   - Can type or paste text
   - Click "Check Grammar" to analyze
   - Apply corrections

### 6. Test Tray Icon Menu

1. **Right-click** the tray icon
2. Verify menu items:
   - ✅ Open GrammrPop (Ctrl+Alt+G)
   - ✅ Auto-Detect Mode (should be ✓ checked)
   - ✅ Settings
   - ✅ Exit

3. **Toggle Auto-Detect Off:**
   - Click "Auto-Detect Mode" to uncheck
   - Try typing errors in Notepad
   - Icon should NOT appear

4. **Toggle Auto-Detect On:**
   - Right-click tray → Check "Auto-Detect Mode"
   - Type errors in Notepad
   - Icon should appear again

5. **Test Settings:**
   - Right-click → Settings
   - Settings window opens
   - Change language, API settings, etc.

6. **Test Exit:**
   - Right-click → Exit
   - App closes (tray icon disappears)

---

## Troubleshooting

### Icon Never Appears

**Possible Causes:**

1. **Auto-Detect is OFF**
   - Right-click tray icon → Check "Auto-Detect Mode"

2. **API Connection Failed**
   - Check internet connection
   - Look for error message boxes
   - Try Settings → Use Local Server (if you have LanguageTool running locally)

3. **Text Not Detected**
   - Make sure you're using a standard text control
   - Try Windows Notepad first (most compatible)
   - Check Debug Output window in Visual Studio for detection logs

4. **No Errors in Text**
   - Make sure your text actually has grammar errors
   - Try: "This are wrong" (guaranteed error)

### Error Message Boxes

If you see error messages, they will tell you what's wrong:

- **"Failed to check grammar"** → API/Network issue
  - Check internet connection
  - Verify API endpoint in Settings
  - Consider using local LanguageTool server

- **"Failed to register global hotkey"** → Ctrl+Alt+G already in use
  - Close other apps using this hotkey
  - Or change hotkey in Settings

- **"Error showing grammar icon"** → Display issue
  - Check if textbox is on screen
  - Try restarting the app

### Debug Output

To see detailed logs:

1. Run from **Visual Studio** with Debug output
2. Or run from **Command Prompt**:
   ```bash
   dotnet run --configuration Debug
   ```

Look for these log messages:
- `✓✓✓ ACCEPTED:` - Textbox detected successfully
- `❌ Rejected:` - Control rejected (not a textbox)
- `🔍 Auto-checking grammar` - Grammar check started
- `✓ Found X grammar errors` - Errors found
- `📍 Showing icon` - Icon displayed

---

## Expected Behavior Summary

| Action | Expected Result |
|--------|----------------|
| App starts | Tray icon appears, notification shown |
| Type error in Notepad | Red icon appears after 2 sec |
| Click red icon | Popup opens with corrections |
| Press Ctrl+Alt+G | Popup opens |
| Right-click tray | Context menu shows |
| Toggle Auto-Detect OFF | Icon stops appearing |
| Exit from tray | App closes |

---

## Performance Notes

- **Polling**: Checks focused element every 500ms
- **Debouncing**: Waits 2 seconds after typing stops before checking
- **API Rate Limit**: Free LanguageTool API = 20 requests/min
- **Memory**: ~50-100 MB (WPF + UI Automation)

---

## Compatible Applications

**Tested & Working:**
- ✅ Notepad (Windows built-in)
- ✅ WordPad
- ✅ Notepad++ (Scintilla controls)
- ✅ VS Code (Chrome-based editors)
- ✅ Browser textboxes (Chrome, Edge, Firefox)
- ✅ Microsoft Word (some controls)
- ✅ Windows Mail

**Not Supported:**
- ❌ Command prompts / terminals
- ❌ Password fields (intentionally skipped for security)
- ❌ Some custom UI frameworks

---

## Need Help?

1. Check if app is running (Task Manager)
2. Look for error message boxes
3. Check Debug output for logs
4. Try disabling/re-enabling Auto-Detect
5. Restart the application
6. Test with Windows Notepad first (most compatible)

## Test Results Template

```
Date: __________
Windows Version: __________
.NET Version: __________

✅ / ❌  App starts, tray icon visible
✅ / ❌  Balloon notification shows
✅ / ❌  Icon appears in Notepad with errors
✅ / ❌  Icon shows correct error count
✅ / ❌  Clicking icon opens popup
✅ / ❌  Corrections are pre-loaded
✅ / ❌  Ctrl+Alt+G opens popup
✅ / ❌  Tray menu works
✅ / ❌  Auto-detect toggle works
✅ / ❌  Works in Notepad++
✅ / ❌  Works in browser textboxes

Issues found:
_________________________________
_________________________________
```
