# Ollama Grammar Correction Integration

## Overview
Added Ollama as a second grammar correction layer to GrammrPop while keeping all existing LanguageTool functionality intact.

## What Was Added

### 1. New Files Created

#### `Models/GrammarCorrectionMode.cs`
- Enum defining three grammar correction modes:
  - `LanguageTool` - Uses only LanguageTool (existing functionality)
  - `Ollama` - Uses only Ollama model
  - `Both` - Runs both engines and combines results

#### `Services/OllamaClient.cs`
- New service for Ollama integration
- Methods:
  - `IsAvailableAsync()` - Checks if Ollama server is running
  - `CorrectGrammarAsync()` - Sends text to Ollama for grammar correction
  - `ConvertToMatches()` - Converts Ollama response to Match format for UI compatibility
- Uses endpoint: `POST http://localhost:11434/api/chat`
- Model: `gnokit/improve-grammar`
- Timeout: 60 seconds (grammar models may take longer)

### 2. Modified Files

#### `Models/Settings.cs`
- Added `CorrectionMode` property (default: `LanguageTool`)
- Added `OllamaEndpoint` property (default: `http://localhost:11434`)
- Settings persist across app restarts

#### `Services/SettingsService.cs`
- Updated `Save()` method to persist new settings properties

#### `Views/PopupWindow.xaml`
- Added grammar engine toggle section with 3 buttons
- Added warning text for when Ollama is not available
- Added `ModeToggleButtonStyle` for styled toggle buttons
- Toggle buttons visually indicate selected state

#### `Views/PopupWindow.xaml.cs`
- Added `OllamaClient` instance
- Added `_isOllamaAvailable` flag
- New methods:
  - `InitializeGrammarModeAsync()` - Checks Ollama availability on load
  - `UpdateToggleButtons()` - Updates UI based on selected mode
  - `LanguageToolButton_Click()`, `BothButton_Click()`, `OllamaButton_Click()` - Handle mode switching
  - `SetGrammarMode()` - Persists mode selection
- Updated `CheckGrammarAsync()` to support all three modes:
  - Runs LanguageTool if mode is LanguageTool or Both
  - Runs Ollama if mode is Ollama or Both
  - Combines results when Both mode is selected
  - Gracefully handles errors for each engine independently

## Error Handling

### When Ollama is Not Running:
1. Warning message "⚠ Ollama not running" is displayed
2. "Ollama" and "Both" buttons are disabled
3. If current mode was Ollama/Both, automatically falls back to LanguageTool
4. User can still use LanguageTool normally

### During Grammar Check:
- If LanguageTool fails in "Both" mode: Shows warning, continues with Ollama results
- If Ollama fails in "Both" mode: Shows warning, continues with LanguageTool results
- If sole engine fails: Shows error dialog
- All errors are user-friendly with clear messages

## Usage

### For End Users:
1. Launch GrammrPop
2. Select desired grammar engine using toggle buttons:
   - **LanguageTool** - Traditional grammar checking (no Ollama required)
   - **Ollama** - AI-powered grammar improvement (requires Ollama running locally)
   - **Both** - Get suggestions from both engines
3. Selection is saved and persists across sessions

### Requirements for Ollama Mode:
1. Ollama must be installed and running locally
2. Model `gnokit/improve-grammar` must be pulled:
   ```bash
   ollama pull gnokit/improve-grammar
   ```
3. Default endpoint: `http://localhost:11434`

## Technical Notes

### Independence:
- LanguageTool and Ollama run completely independently
- No chaining - they process the original text in parallel
- Results are combined without conflict

### Compatibility:
- Ollama results are converted to Match format for UI compatibility
- Existing ApplySuggestions logic works with both engines
- No breaking changes to existing API or function signatures

### Performance:
- Both mode may take longer as it runs two checks
- Ollama timeout set to 60 seconds (models may need time to load)
- UI remains responsive with loading overlay

## Future Enhancements (Optional)
- Add configuration for custom Ollama model
- Show which engine generated each suggestion in "Both" mode
- Add confidence scores
- Support for custom Ollama endpoints
