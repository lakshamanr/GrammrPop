using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GrammrPop.Models;
using GrammrPop.Services;

namespace GrammrPop.Views
{
    public partial class PopupWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly LanguageToolClient _grammarClient;
        private readonly ClipboardService _clipboardService;
        private ObservableCollection<SuggestionItem> _suggestions;
        private string _originalText = string.Empty;
        private Match[] _currentMatches = Array.Empty<Match>();

        public PopupWindow(SettingsService settingsService)
        {
            InitializeComponent();

            _settingsService = settingsService;
            _grammarClient = new LanguageToolClient();
            _clipboardService = new ClipboardService();
            _suggestions = new ObservableCollection<SuggestionItem>();

            SuggestionsListView.ItemsSource = _suggestions;

            // Capture the previously focused window for auto-paste
            _clipboardService.CapturePreviousWindow();

            // Focus the input textbox
            Loaded += (s, e) => InputTextBox.Focus();

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
                var endpoint = settings.UseLocalServer
                    ? settings.LocalServerUrl
                    : settings.ApiEndpoint;

                // Call LanguageTool API
                var result = await _grammarClient.CheckAsync(
                    text,
                    settings.Language,
                    endpoint,
                    settings.ApiKey);

                _currentMatches = result.Matches;

                // Convert to UI-friendly suggestions
                _suggestions.Clear();

                if (_currentMatches.Length == 0)
                {
                    NoSuggestionsText.Text = "✓ No grammar issues found! Your text looks good.";
                    NoSuggestionsText.Visibility = Visibility.Visible;
                    SuggestionsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    NoSuggestionsText.Visibility = Visibility.Collapsed;
                    SuggestionsListView.Visibility = Visibility.Visible;

                    foreach (var match in _currentMatches)
                    {
                        var errorText = text.Substring(match.Offset, match.Length);
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
