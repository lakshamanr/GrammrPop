# GrammrPop Portable Build Script
# This script creates a self-contained portable distribution of GrammrPop

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\Portable",
    [switch]$CreateZip = $true
)

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "   GrammrPop Portable Build Script   " -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Get version from project file
$projectFile = "GrammrPop.csproj"
[xml]$project = Get-Content $projectFile
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrEmpty($version)) {
    $version = "1.0.0"
}

Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Version: $version" -ForegroundColor Yellow
Write-Host "Output: $OutputPath" -ForegroundColor Yellow
Write-Host ""

# Clean previous builds
Write-Host "[1/6] Cleaning previous builds..." -ForegroundColor Green
if (Test-Path $OutputPath) {
    Remove-Item -Path $OutputPath -Recurse -Force
}
Write-Host "      [OK] Cleaned" -ForegroundColor DarkGreen
Write-Host ""

# Build the project
Write-Host "[2/6] Building project ($Configuration)..." -ForegroundColor Green
$buildResult = dotnet build -c $Configuration --no-incremental 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "      ✗ Build failed!" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host "      [OK] Build successful" -ForegroundColor DarkGreen
Write-Host ""

# Publish self-contained
Write-Host "[3/6] Publishing self-contained application..." -ForegroundColor Green
$publishPath = Join-Path $OutputPath "GrammrPop_v$version"
$publishResult = dotnet publish -c $Configuration `
    --self-contained true `
    --runtime win-x64 `
    --output $publishPath `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "      [FAIL] Publish failed!" -ForegroundColor Red
    Write-Host $publishResult
    exit 1
}
Write-Host "      [OK] Published to: $publishPath" -ForegroundColor DarkGreen
Write-Host ""

# Copy documentation
Write-Host "[4/6] Copying documentation..." -ForegroundColor Green
$docs = @(
    "README.md",
    "BUGFIXES_2024.md",
    "TESTING_BUGFIXES.md",
    "SLACK_SUPPORT.md",
    "HTML_EDITOR_SUPPORT.md",
    "FIXES.md",
    "TESTING_GUIDE.md"
)

foreach ($doc in $docs) {
    if (Test-Path $doc) {
        Copy-Item -Path $doc -Destination $publishPath -Force
        Write-Host "      [OK] Copied $doc" -ForegroundColor DarkGreen
    }
}
Write-Host ""

# Create portable info file
Write-Host "[5/6] Creating portable info..." -ForegroundColor Green
$portableInfo = @"
# GrammrPop v$version - Portable Edition

## Quick Start

1. Run **GrammrPop.exe** to start the application
2. The app will minimize to the system tray (bottom-right corner)
3. Right-click the tray icon for options:
   - **Settings**: Configure hotkey, API settings, and auto-detect
   - **History**: View your grammar check history
   - **Auto-Detect**: Toggle automatic text box detection
   - **Exit**: Close the application

## Using GrammrPop

### Manual Check (Default Hotkey: Ctrl+Shift+G)
1. Select text in any application
2. Press **Ctrl+Shift+G**
3. A popup will appear with grammar suggestions

### Auto-Detect Mode (Recommended)
1. Right-click tray icon -> Enable "Auto-Detect"
2. Click in any text box (Notepad, Word, WordPad, browser, etc.)
3. Start typing - GrammrPop icon appears automatically
4. After 0.8 seconds of no typing, grammar check runs
5. Click the icon to see suggestions

## Supported Applications

- [YES] Notepad
- [YES] Notepad++
- [YES] Microsoft WordPad (NEW!)
- [YES] Microsoft Word
- [YES] Web browsers (Chrome, Edge, Firefox)
- [YES] Slack
- [YES] Microsoft Teams
- [YES] Discord
- [YES] Electron apps (NEW!)
- [YES] Any text box with standard controls

## Configuration

### Settings Window
Right-click tray icon -> **Settings** to configure:

- **Hotkey**: Customize the keyboard shortcut
- **Language**: Select your checking language (default: en-US)
- **API Settings**: 
  - Use local LanguageTool server (recommended for privacy)
  - Or use public API with optional API key
- **Auto-Paste**: Automatically paste corrected text
- **Auto-Detect**: Enable/disable automatic text box detection
- **History Size**: Number of recent checks to keep

### Default Settings
- Hotkey: **Ctrl+Shift+G**
- Language: **English (US)**
- Auto-Detect: **Enabled**
- Auto-Paste: **Disabled**
- History Size: **50 items**

## Requirements

- Windows 10/11 (64-bit)
- .NET 8 Runtime (included in this portable package)
- Internet connection for grammar checking

## Recent Bug Fixes (v$version)

### Fixed Issues:
1. **Settings Dialog Exception** - Settings now save correctly
2. **WordPad Support** - Now works with Microsoft WordPad
3. **Electron Apps** - Enhanced support for Electron-based apps

See **BUGFIXES_2024.md** for technical details.

## Troubleshooting

### App doesn't start
- Run as Administrator
- Check Windows Defender/Antivirus isn't blocking it

### Grammar check not working
- Check your internet connection
- Verify API settings (Settings -> API Settings)
- Try toggling "Use Local Server" off/on

### Auto-Detect not working
- Make sure Auto-Detect is enabled (tray icon menu)
- Check console output (if running from command line)
- Some custom controls may not be supported

### WordPad not detected
- Make sure you're clicking in the main document area
- Wait for text to stabilize (2 stable polls = 300ms)
- Check TESTING_BUGFIXES.md for detailed testing steps

## Documentation

- **README.md** - Main documentation
- **BUGFIXES_2024.md** - Technical details of recent fixes
- **TESTING_BUGFIXES.md** - Testing guide for bug fixes
- **SLACK_SUPPORT.md** - Slack integration guide
- **HTML_EDITOR_SUPPORT.md** - HTML editor support details
- **TESTING_GUIDE.md** - Comprehensive testing guide

## Support

For issues, questions, or contributions:
- GitHub: https://github.com/lakshamanr/GrammrPop
- Report bugs via GitHub Issues

## License

See LICENSE file for details.

---

**Version:** $version  
**Build Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")  
**Build Type:** Portable Self-Contained  
**Platform:** Windows x64  
**Framework:** .NET 8.0
"@

$portableInfo | Out-File -FilePath (Join-Path $publishPath "PORTABLE_README.txt") -Encoding UTF8
Write-Host "      [OK] Created PORTABLE_README.txt" -ForegroundColor DarkGreen
Write-Host ""

# Create ZIP archive
if ($CreateZip) {
    Write-Host "[6/6] Creating ZIP archive..." -ForegroundColor Green
    $zipName = "GrammrPop_v${version}_Portable_Win64.zip"
    $zipPath = Join-Path $OutputPath $zipName

    # Remove existing ZIP
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    # Create ZIP
    Compress-Archive -Path $publishPath -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "      [OK] Created: $zipPath" -ForegroundColor DarkGreen

    # Calculate size
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "      [OK] Size: $zipSize MB" -ForegroundColor DarkGreen
}
Write-Host ""

# Summary
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "   Build Complete!                   " -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Portable Package Location:" -ForegroundColor Green
Write-Host "  $publishPath" -ForegroundColor White
Write-Host ""

if ($CreateZip) {
    Write-Host "ZIP Archive:" -ForegroundColor Green
    Write-Host "  $zipPath" -ForegroundColor White
    Write-Host "  Size: $zipSize MB" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "To test the portable version:" -ForegroundColor Cyan
Write-Host "  cd `"$publishPath`"" -ForegroundColor White
Write-Host "  .\GrammrPop.exe" -ForegroundColor White
Write-Host ""

Write-Host "Ready to distribute!" -ForegroundColor Green
Write-Host ""
