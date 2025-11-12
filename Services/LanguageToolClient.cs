using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GrammrPop.Models;

namespace GrammrPop.Services
{
    public class LanguageToolClient
    {
        private readonly HttpClient _httpClient;

        public LanguageToolClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// Checks text for grammar, spelling, and style issues
        /// </summary>
        /// <param name="text">Text to check</param>
        /// <param name="language">Language code (e.g., "en-US", "en-GB", "de-DE")</param>
        /// <param name="endpoint">API endpoint URL</param>
        /// <param name="apiKey">Optional API key for premium features</param>
        /// <returns>LanguageToolResult with matches</returns>
        public async Task<LanguageToolResult> CheckAsync(
            string text,
            string language = "en-US",
            string endpoint = "https://api.languagetool.org/v2/check",
            string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new LanguageToolResult { Matches = Array.Empty<Match>() };
            }

            try
            {
                // Build form data
                var formData = new StringBuilder();
                formData.Append($"text={Uri.EscapeDataString(text)}");
                formData.Append($"&language={Uri.EscapeDataString(language)}");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    formData.Append($"&apiKey={Uri.EscapeDataString(apiKey)}");
                }

                var content = new StringContent(
                    formData.ToString(),
                    Encoding.UTF8,
                    "application/x-www-form-urlencoded");

                var response = await _httpClient.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<LanguageToolResult>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return result ?? new LanguageToolResult { Matches = Array.Empty<Match>() };
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error while checking grammar: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception("Request timed out. Please check your connection or try again.", ex);
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse response from grammar service: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Applies all suggestions to the original text in the correct order
        /// </summary>
        /// <param name="originalText">Original text</param>
        /// <param name="matches">Matches to apply</param>
        /// <param name="selectedMatches">If provided, only apply these specific matches</param>
        /// <returns>Corrected text</returns>
        public string ApplySuggestions(
            string originalText,
            Match[] matches,
            Match[]? selectedMatches = null)
        {
            if (matches == null || matches.Length == 0)
                return originalText;

            // Use selected matches if provided, otherwise use all
            var matchesToApply = selectedMatches ?? matches;

            if (matchesToApply.Length == 0)
                return originalText;

            // Sort by offset descending to avoid shifting positions
            var orderedMatches = matchesToApply
                .Where(m => m.Replacements != null && m.Replacements.Length > 0)
                .OrderByDescending(m => m.Offset)
                .ToList();

            var sb = new StringBuilder(originalText);

            foreach (var match in orderedMatches)
            {
                var replacement = match.Replacements.First().Value;

                // Safety check: ensure offset and length are valid
                if (match.Offset >= 0 &&
                    match.Offset + match.Length <= sb.Length)
                {
                    sb.Remove(match.Offset, match.Length);
                    sb.Insert(match.Offset, replacement);
                }
            }

            return sb.ToString();
        }
    }
}
