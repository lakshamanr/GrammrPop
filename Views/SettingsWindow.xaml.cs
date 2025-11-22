using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GrammrPop.Services;

namespace GrammrPop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;

        public SettingsWindow(SettingsService settingsService)
        {
            InitializeComponent();

            _settingsService = settingsService;

            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _settingsService.CurrentSettings;

            // Hotkey
            HotkeyTextBox.Text = settings.Hotkey;

            // Language
            var languageItem = LanguageComboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag.ToString() == settings.Language);

            if (languageItem != null)
            {
                LanguageComboBox.SelectedItem = languageItem;
            }
            else
            {
                LanguageComboBox.SelectedIndex = 0; // Default to en-US
            }

            // API Settings
            UseLocalServerCheckBox.IsChecked = settings.UseLocalServer;
            ApiEndpointTextBox.Text = settings.UseLocalServer
                ? settings.LocalServerUrl
                : settings.ApiEndpoint;
            ApiKeyTextBox.Text = settings.ApiKey ?? string.Empty;

            // Auto-Paste
            AutoPasteCheckBox.IsChecked = settings.AutoPaste;

            // Auto-Detect
            EnableAutoDetectCheckBox.IsChecked = settings.EnableAutoDetect;

            // History
            HistorySizeTextBox.Text = settings.HistorySize.ToString();

            UpdateApiEndpointState();
        }

        private void UseLocalServerCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateApiEndpointState();
        }

        private void UpdateApiEndpointState()
        {
            var useLocal = UseLocalServerCheckBox.IsChecked == true;

            if (useLocal)
            {
                ApiEndpointTextBox.Text = _settingsService.CurrentSettings.LocalServerUrl;
                ApiKeyTextBox.IsEnabled = false;
                ApiKeyTextBox.Text = string.Empty;
            }
            else
            {
                ApiEndpointTextBox.Text = _settingsService.CurrentSettings.ApiEndpoint;
                ApiKeyTextBox.IsEnabled = true;
                ApiKeyTextBox.Text = _settingsService.CurrentSettings.ApiKey ?? string.Empty;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = _settingsService.CurrentSettings;

                // Validate and save settings
                var selectedLanguage = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "en-US";
                settings.Language = selectedLanguage;

                // Validate hotkey
                var hotkey = HotkeyTextBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(hotkey))
                {
                    settings.Hotkey = hotkey;
                }

                var useLocal = UseLocalServerCheckBox.IsChecked == true;
                settings.UseLocalServer = useLocal;

                if (useLocal)
                {
                    settings.LocalServerUrl = ApiEndpointTextBox.Text?.Trim() ?? settings.LocalServerUrl;
                    settings.ApiKey = null;
                }
                else
                {
                    settings.ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? settings.ApiEndpoint;
                    settings.ApiKey = string.IsNullOrWhiteSpace(ApiKeyTextBox.Text)
                        ? null
                        : ApiKeyTextBox.Text.Trim();
                }

                settings.AutoPaste = AutoPasteCheckBox.IsChecked == true;

                // Auto-Detect
                settings.EnableAutoDetect = EnableAutoDetectCheckBox.IsChecked == true;

                // Parse history size
                if (int.TryParse(HistorySizeTextBox.Text, out var historySize) && historySize >= 0)
                {
                    settings.HistorySize = historySize;
                }
                else
                {
                    MessageBox.Show(
                        "History size must be a positive number.",
                        "Invalid Input",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Save to disk
                _settingsService.Save();

                // Refresh auto-detect feature if it was toggled
                var app = Application.Current as App;
                app?.RefreshAutoDetect();
                
                // Re-register hotkey if it changed
                app?.ReRegisterHotkey();

                MessageBox.Show(
                    "Settings saved successfully!",
                    "Settings Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving settings:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
