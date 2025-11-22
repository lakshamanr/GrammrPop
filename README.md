# GrammrPop

**GrammrPop** is a lightweight Windows desktop grammar assistant that works from anywhere using a global hotkey. It integrates with LanguageTool to provide real-time grammar, spelling, and style checking.

## Features

- **🎯 Smart Auto-Detection** (NEW!): Grammarly-like floating icon appears automatically on textboxes - just click to check grammar!
- **⚡ Single Instance**: Only one instance runs at a time - prevents conflicts and duplicate hotkeys
- **⌨️ Global Hotkey**: Press `Ctrl+Alt+G` (customizable) from any application to open the grammar checker
- **⚡ Instant Checking**: Paste or type text and get immediate grammar suggestions
- **🎨 Smart Corrections**: View and selectively apply suggestions with context
- **📋 Auto-Paste**: Optionally paste corrected text back to your original application
- **🔒 Privacy Mode**: Use a local LanguageTool server for offline/private checking
- **🌍 Multi-Language**: Support for English (US/GB), German, French, Spanish, and more
- **⚙️ Customizable**: Configure API endpoint, language, hotkey, and behavior

## Requirements

- **OS**: Windows 10/11
- **Framework**: .NET 8 Runtime ([Download here](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Internet**: Required for public API (optional if using local server)

## Quick Start

### Option 1: Build from Source

1. **Clone the repository**:
   ```bash
   git clone https://github.com/yourusername/GrammrPop.git
   cd GrammrPop
   ```

2. **Open in Visual Studio 2022**:
   - Open `GrammrPop.csproj`
   - Restore NuGet packages (automatic)
   - Build the solution (`Ctrl+Shift+B`)

3. **Run the application**:
   - Press `F5` to run in debug mode
   - Or build in Release mode and run the executable from `bin/Release/net8.0-windows/`

### Option 2: Download Release (Coming Soon)

Download the latest release from the [Releases page](https://github.com/yourusername/GrammrPop/releases).

## Usage

GrammrPop offers two ways to check your text:

### Method 1: Smart Auto-Detection (Grammarly-like)

1. **Enable auto-detect**: Open Settings → Enable "Show floating icon on textboxes automatically"
2. **Focus a textbox**: Click on any text input field in any application
3. **Click the floating icon**: A small green "G" icon appears on the right edge of the textbox
4. **Check grammar**: The popup opens with your text pre-filled - click "Check Grammar"

### Method 2: Global Hotkey (Traditional)

1. **Launch GrammrPop**: The app runs in the background (no main window)

2. **Trigger the popup**: Press `Ctrl+Alt+G` from any application

3. **Check your text**:
   - Paste or type text into the popup
   - Click **Check Grammar** (or press `Ctrl+Enter`)
   - Review suggestions

4. **Apply corrections**:
   - Select suggestions to apply (or use **Apply All**)
   - Click **Copy** or enable **Auto-Paste** in settings
   - Corrected text is copied to clipboard

5. **Configure settings**: Click **Settings** to customize behavior

## Settings

Access settings from the popup window:

### Global Hotkey
- Configure your preferred hotkey combination (default: `Ctrl+Alt+G`)
- Supports modifiers: Ctrl, Alt, Shift, Win + any letter or number key
- Examples: `Ctrl+Alt+G`, `Ctrl+Shift+H`, `Win+G`
- Changes apply immediately without restart
- If a hotkey is already in use, you'll see an error message

### Language
- Choose from English (US/GB), German, French, Spanish, Portuguese, Italian, Dutch

### Grammar Service
- **Public API**: Uses `https://api.languagetool.org` (free, rate-limited)
- **Local Server**: Use your own LanguageTool server for privacy (see below)
- **API Key**: Optional premium API key for enhanced features

### Smart Detection (Grammarly-like)
- **Enable auto-detect**: Shows a floating icon on textboxes automatically
- Uses Windows UI Automation to detect text controls
- Icon appears on the right edge of focused textboxes
- Click the icon to open GrammrPop with pre-filled text
- ⚠️ May increase CPU usage slightly when enabled

### Auto-Paste
- Enable to automatically paste corrected text back to your previous window
- Uses simulated `Ctrl+V` to paste

### History
- Configure how many previous checks to remember (default: 20)

## Privacy Mode: Local LanguageTool Server

For offline or private grammar checking:

1. **Download LanguageTool standalone**:
   - Visit [LanguageTool Downloads](https://languagetool.org/download/)
   - Download the standalone desktop version

2. **Run the server**:
   ```bash
   java -cp languagetool-server.jar org.languagetool.server.HTTPServer --port 8081
   ```

3. **Configure GrammrPop**:
   - Open Settings
   - Enable **"Use local LanguageTool server"**
   - Ensure endpoint is `http://localhost:8081/v2/check`

## Architecture

```
GrammrPop/
├── Models/              # Data models (Settings, LanguageTool responses)
├── Services/            # Business logic (API client, clipboard, settings)
├── Views/               # WPF windows (Popup, Settings)
├── App.xaml[.cs]        # Application entry point & hotkey registration
└── GrammrPop.csproj     # Project file
```

### Key Components

- **NHotkey.Wpf**: Global hotkey registration
- **InputSimulator**: Simulates keyboard input for auto-paste
- **HttpClient**: Communicates with LanguageTool API
- **DPAPI**: Encrypts API keys in settings using Windows Data Protection

## Development

### Building

```bash
dotnet restore
dotnet build --configuration Release
```

### Running Tests (Coming Soon)

```bash
dotnet test
```

### Technologies

- **.NET 8** with **WPF** for Windows desktop UI
- **C# 12** with nullable reference types
- **LanguageTool API** for grammar checking
- **Windows DPAPI** for secure credential storage

## Roadmap

- [x] Customizable hotkey in UI
- [ ] History window to view past checks
- [ ] System tray icon with context menu (✓ implemented)
- [ ] Spelling dictionary customization
- [ ] Text highlighting in popup with inline corrections
- [ ] Support for more languages
- [ ] Plugin system for custom grammar rules

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [LanguageTool](https://languagetool.org/) - Open-source grammar checking service
- [NHotkey](https://github.com/thomaslevesque/NHotkey) - Global hotkey library
- [InputSimulator](https://github.com/michaelnoonan/inputsimulator) - Keyboard input simulation

## Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/GrammrPop/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/GrammrPop/discussions)

---

**Made with ❤️ for writers everywhere**
