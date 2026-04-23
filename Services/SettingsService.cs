using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrammrPop.Models;

namespace GrammrPop.Services
{
    public class SettingsService
    {
        private static readonly string SettingsFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GrammrPop");

        private static readonly string SettingsFilePath =
            Path.Combine(SettingsFolder, "settings.json");

        public Settings CurrentSettings { get; private set; } = new Settings();

        public void Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    // First run - create default settings
                    CurrentSettings = new Settings();
                    Save();
                    return;
                }

               var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<Settings>(json);

                if (settings != null)
                {
                    CurrentSettings = settings;

                    // Decrypt API key if present
                    if (!string.IsNullOrWhiteSpace(CurrentSettings.ApiKey))
                    {
                        try
                        {
                            CurrentSettings.ApiKey = DecryptString(CurrentSettings.ApiKey);
                        }
                        catch
                        {
                            // If decryption fails, clear the key
                            CurrentSettings.ApiKey = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If loading fails, use defaults
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                CurrentSettings = new Settings();
            }
        }

        public void Save()
        {
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(SettingsFolder);

                // Clone settings for saving
                var settingsToSave = new Settings
                {
                    Hotkey = CurrentSettings.Hotkey,
                    ApiEndpoint = CurrentSettings.ApiEndpoint,
                    Language = CurrentSettings.Language,
                    AutoPaste = CurrentSettings.AutoPaste,
                    HistorySize = CurrentSettings.HistorySize,
                    UseLocalServer = CurrentSettings.UseLocalServer,
                    LocalServerUrl = CurrentSettings.LocalServerUrl,
                    ApiKey = CurrentSettings.ApiKey,
                    EnableAutoDetect = CurrentSettings.EnableAutoDetect,
                    CorrectionMode = CurrentSettings.CorrectionMode,
                    OllamaEndpoint = CurrentSettings.OllamaEndpoint
                };

                // Encrypt API key if present
                if (!string.IsNullOrWhiteSpace(settingsToSave.ApiKey))
                {
                    settingsToSave.ApiKey = EncryptString(settingsToSave.ApiKey);
                }

                var json = JsonSerializer.Serialize(
                    settingsToSave,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Encrypts a string using Windows DPAPI (user-level protection)
        /// </summary>
        private string EncryptString(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return plainText;

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                // If encryption fails, return plain text (better than losing the key)
                return plainText;
            }
        }

        /// <summary>
        /// Decrypts a string using Windows DPAPI
        /// </summary>
        private string DecryptString(string encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
                return encryptedText;

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedText);
                var plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // If decryption fails, assume it's plain text
                return encryptedText;
            }
        }
    }
}
