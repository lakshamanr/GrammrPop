using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GrammrPop.Models;
using GrammrPop.Services;

namespace GrammrPop.Views
{
    public partial class PopupWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly LanguageToolClient _grammarClient;
        private readonly OllamaClient _ollamaClient;
        private readonly ClipboardService _clipboardService;
        private ObservableCollection<SuggestionItem> _suggestions;
        private string _originalText = string.Empty;
        private Match[] _currentMatches = Array.Empty<Match>();
        private bool _isOllamaAvailable = false;

        public PopupWindow(SettingsService settingsService)
        {
            InitializeComponent();

            _settingsService = settingsService;
            _grammarClient = new LanguageToolClient();
            _ollamaClient = new OllamaClient();
            _clipboardService = new ClipboardService();
            _suggestions = new ObservableCollection<SuggestionItem>();

            SuggestionsListView.ItemsSource = _suggestions;

            // Capture the previously focused window for auto-paste
            _clipboardService.CapturePreviousWindow();

            // Focus the input textbox
            Loaded += async (s, e) =>
            {
                InputTextBox.Focus();
                await InitializeGrammarModeAsync();
            };

            // Auto-paste clipboard content if available
            if (Clipboard.ContainsText())
            {
                try
                {
                    InputTextBox.Text = Clipboard.GetText();
                    InputTextBox.SelectAll();
                }
                catch { /* Ignore clipboard errors */ }
            }
        }

        private async Task InitializeGrammarModeAsync()
        {
            // Check Ollama availability
            var settings = _settingsService.CurrentSettings;
            _isOllamaAvailable = await _ollamaClient.IsAvailableAsync(settings.OllamaEndpoint);

            // Update UI based on availability
            if (!_isOllamaAvailable)
            {
                OllamaWarningText.Visibility = Visibility.Visible;
                OllamaButton.IsEnabled = false;

                // If current mode requires Ollama, fall back to LanguageTool
                if (settings.CorrectionMode == GrammarCorrectionMode.Ollama ||
                    settings.CorrectionMode == GrammarCorrectionMode.Both)
                {
                    settings.CorrectionMode = GrammarCorrectionMode.LanguageTool;
                    _settingsService.Save();
                }
            }
            else
            {
                OllamaWarningText.Visibility = Visibility.Collapsed;
                OllamaButton.IsEnabled = true;
            }

            // Set initial toggle state
            UpdateToggleButtons(settings.CorrectionMode);
        }

        private void UpdateToggleButtons(GrammarCorrectionMode mode)
        {
            LanguageToolButton.IsChecked = mode == GrammarCorrectionMode.LanguageTool;
            BothButton.IsChecked = mode == GrammarCorrectionMode.Both;
            OllamaButton.IsChecked = mode == GrammarCorrectionMode.Ollama;

            // Disable "Both" button if Ollama is not available
            if (!_isOllamaAvailable)
            {
                BothButton.IsEnabled = false;
            }
        }

        private void LanguageToolButton_Click(object sender, RoutedEventArgs e)
        {
            SetGrammarMode(GrammarCorrectionMode.LanguageTool);
        }

        private void BothButton_Click(object sender, RoutedEventArgs e)
        {
            SetGrammarMode(GrammarCorrectionMode.Both);
        }

        private void OllamaButton_Click(object sender, RoutedEventArgs e)
        {
            SetGrammarMode(GrammarCorrectionMode.Ollama);
        }

        private void SetGrammarMode(GrammarCorrectionMode mode)
        {
            var settings = _settingsService.CurrentSettings;
            settings.CorrectionMode = mode;
            _settingsService.Save();
            UpdateToggleButtons(mode);
        }

        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckGrammarAsync();
        }

        private async Task CheckGrammarAsync()
        {
            var text = InputTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(
                    "Please enter some text to check.",
                    "GrammrPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                // Show loading overlay
                LoadingOverlay.Visibility = Visibility.Visible;
                IsEnabled = false;

                _originalText = text;
                var settings = _settingsService.CurrentSettings;

                var allMatches = new List<Match>();

                // Run LanguageTool if selected
                if (settings.CorrectionMode == GrammarCorrectionMode.LanguageTool ||
                    settings.CorrectionMode == GrammarCorrectionMode.Both)
                {
                    try
                    {
                        var endpoint = settings.UseLocalServer
                            ? settings.LocalServerUrl
                            : settings.ApiEndpoint;

                        var result = await _grammarClient.CheckAsync(
                            text,
                            settings.Language,
                            endpoint,
                            settings.ApiKey);

                        allMatches.AddRange(result.Matches);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"LanguageTool error:\n\n{ex.Message}",
                            "GrammrPop - LanguageTool Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                // Run Ollama if selected
                if (settings.CorrectionMode == GrammarCorrectionMode.Ollama ||
                    settings.CorrectionMode == GrammarCorrectionMode.Both)
                {
                    try
                    {
                        if (!_isOllamaAvailable)
                        {
                            throw new Exception("Ollama is not running. Please start Ollama and try again.");
                        }

                        var correctedText = await _ollamaClient.CorrectGrammarAsync(text, settings.OllamaEndpoint);
                        var ollamaMatches = _ollamaClient.ConvertToMatches(text, correctedText);

                        allMatches.AddRange(ollamaMatches);
                    }
                    catch (Exception ex)
                    {
                        // If only Ollama mode, show error; if Both mode, continue with LanguageTool results
                        if (settings.CorrectionMode == GrammarCorrectionMode.Ollama)
                        {
                            MessageBox.Show(
                                $"Ollama error:\n\n{ex.Message}",
                                "GrammrPop - Ollama Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                        else
                        {
                            MessageBox.Show(
                                $"Ollama error (continuing with LanguageTool results):\n\n{ex.Message}",
                                "GrammrPop - Ollama Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }

                _currentMatches = allMatches.ToArray();

                // Convert to UI-friendly suggestions
                _suggestions.Clear();

                if (_currentMatches.Length == 0)
                {
                    NoSuggestionsText.Text = "✓ No grammar issues found! Your text looks good.";
                    NoSuggestionsText.Visibility = Visibility.Visible;
                    SuggestionsScrollViewer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    NoSuggestionsText.Visibility = Visibility.Collapsed;
                    SuggestionsScrollViewer.Visibility = Visibility.Visible;

                    foreach (var match in _currentMatches)
                    {
                        var errorText = match.Offset + match.Length <= text.Length 
                            ? text.Substring(match.Offset, match.Length)
                            : text; // Ollama returns full text replacement
                        var suggestedReplacement = match.Replacements?.FirstOrDefault()?.Value ?? "";

                        _suggestions.Add(new SuggestionItem
                        {
                            Match = match,
                            Accepted = true, // Default to checked
                            ErrorText = errorText,
                            SuggestedReplacement = suggestedReplacement
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error checking grammar:\n\n{ex.Message}",
                    "GrammrPop - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Hide loading overlay
                LoadingOverlay.Visibility = Visibility.Collapsed;
                IsEnabled = true;
            }
        }

        private void ApplySelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedSuggestions = _suggestions
                .Where(s => s.Accepted)
                .Select(s => s.Match)
                .ToArray();

            if (selectedSuggestions.Length == 0)
            {
                MessageBox.Show(
                    "Please select at least one suggestion to apply.",
                    "GrammrPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ApplySuggestions(selectedSuggestions);
        }

        private void ApplyAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMatches.Length == 0)
            {
                MessageBox.Show(
                    "No suggestions to apply. Please check grammar first.",
                    "GrammrPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ApplySuggestions(_currentMatches);
        }

        private void ApplySuggestions(Match[] matches)
        {
            try
            {
                var correctedText = _grammarClient.ApplySuggestions(_originalText, _currentMatches, matches);

                // Update the input box with corrected text
                InputTextBox.Text = correctedText;

                // Copy to clipboard and optionally paste back
                var settings = _settingsService.CurrentSettings;
                _clipboardService.CopyAndPasteBack(correctedText, settings.AutoPaste);

                // Clear suggestions
                _suggestions.Clear();
                _currentMatches = Array.Empty<Match>();
                NoSuggestionsText.Text = "✓ Corrections applied and copied to clipboard!";
                NoSuggestionsText.Visibility = Visibility.Visible;
                SuggestionsListView.Visibility = Visibility.Collapsed;

                // Auto-close if auto-paste is enabled
                if (settings.AutoPaste)
                {
                    // Give user a moment to see the result
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() => Close());
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error applying suggestions:\n\n{ex.Message}",
                    "GrammrPop - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var text = InputTextBox.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(
                    "No text to copy.",
                    "GrammrPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                _clipboardService.CopyToClipboard(text);

                MessageBox.Show(
                    "Text copied to clipboard!",
                    "GrammrPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error copying to clipboard:\n\n{ex.Message}",
                    "GrammrPop - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settingsService);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Load grammar results directly (called from FloatingIconWindow)
        /// </summary>
        public void LoadGrammarResults(Match[] matches, string originalText)
        {
            _originalText = originalText;
            _currentMatches = matches;

            // Convert to UI-friendly suggestions
            _suggestions.Clear();

            if (matches.Length == 0)
            {
                NoSuggestionsText.Text = "✓ No grammar issues found!";
                NoSuggestionsText.Visibility = Visibility.Visible;
                SuggestionsListView.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoSuggestionsText.Visibility = Visibility.Collapsed;
                SuggestionsListView.Visibility = Visibility.Visible;

                foreach (var match in matches)
                {
                    var errorText = originalText.Substring(match.Offset, match.Length);
                    var suggestedReplacement = match.Replacements?.FirstOrDefault()?.Value ?? "";

                    _suggestions.Add(new SuggestionItem
                    {
                        Match = match,
                        Accepted = true,
                        ErrorText = errorText,
                        SuggestedReplacement = suggestedReplacement
                    });
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Enter to check grammar
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = CheckGrammarAsync();
            }
            // Escape to close
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
