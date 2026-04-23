using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GrammrPop.Models;

namespace GrammrPop.Services
{
    public class OllamaClient
    {
        private readonly HttpClient _httpClient;
        private const string ModelName = "gnokit/improve-grammar";

        public OllamaClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60) // Ollama may take longer for grammar correction
            };
        }

        /// <summary>
        /// Checks if Ollama server is running and accessible
        /// </summary>
        /// <param name="endpoint">Ollama API endpoint (e.g., http://localhost:11434)</param>
        /// <returns>True if Ollama is available</returns>
        public async Task<bool> IsAvailableAsync(string endpoint)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{endpoint}/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Corrects text using Ollama's grammar model
        /// </summary>
        /// <param name="text">Text to correct</param>
        /// <param name="endpoint">Ollama API endpoint</param>
        /// <returns>Corrected text</returns>
        public async Task<string> CorrectGrammarAsync(string text, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            try
            {
                var requestBody = new
                {
                    model = ModelName,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = text
                        }
                    },
                    stream = false
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{endpoint}/api/chat", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<OllamaResponse>(
                    responseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (responseData?.Message?.Content != null)
                {
                    var correctedText = responseData.Message.Content.Trim();

                    // Remove common prefixes that Ollama models often add
                    correctedText = RemoveCommonPrefixes(correctedText);

                    return correctedText;
                }

                return text; // Return original if no response
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error while connecting to Ollama: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception("Ollama request timed out. The model may be loading or the server is busy.", ex);
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse Ollama response: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Removes common prefixes that grammar correction models often add to their responses
        /// </summary>
        private string RemoveCommonPrefixes(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // List of common prefixes to remove (case-insensitive)
            var prefixes = new[]
            {
                "Improved text:",
                "Corrected text:",
                "Improved:",
                "Corrected:",
                "Here is the improved text:",
                "Here is the corrected text:",
                "Here's the improved text:",
                "Here's the corrected text:",
                "Correction:",
                "Improved version:",
                "Corrected version:"
            };

            var trimmedText = text.TrimStart();

            foreach (var prefix in prefixes)
            {
                if (trimmedText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // Remove the prefix and any leading whitespace/quotes
                    var result = trimmedText.Substring(prefix.Length).TrimStart();

                    // Remove leading quotes if present
                    if (result.StartsWith("\"") || result.StartsWith("'"))
                    {
                        result = result.Substring(1);
                    }

                    // Remove trailing quotes if present
                    if (result.EndsWith("\"") || result.EndsWith("'"))
                    {
                        result = result.Substring(0, result.Length - 1);
                    }

                    return result.Trim();
                }
            }

            return text;
        }

        /// <summary>
        /// Converts Ollama's corrected text into a Match format similar to LanguageTool
        /// for UI compatibility
        /// </summary>
        public Match[] ConvertToMatches(string originalText, string correctedText)
        {
            // If texts are identical, no changes
            if (originalText == correctedText)
            {
                return Array.Empty<Match>();
            }

            // Create a single match representing the entire correction
            // This is a simplified approach - Ollama returns corrected text, not individual matches
            return new[]
            {
                new Match
                {
                    Message = "Ollama grammar improvement suggestion",
                    ShortMessage = "Grammar improvement",
                    Offset = 0,
                    Length = originalText.Length,
                    Replacements = new[]
                    {
                        new Replacement { Value = correctedText }
                    }
                }
            };
        }
    }

    // Response models for Ollama API
    internal class OllamaResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    internal class OllamaMessage
    {
        public string? Content { get; set; }
    }
}
