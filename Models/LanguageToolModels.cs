using System.Text.Json.Serialization;

namespace GrammrPop.Models
{
    public class LanguageToolResult
    {
        [JsonPropertyName("software")]
        public SoftwareInfo? Software { get; set; }

        [JsonPropertyName("language")]
        public LanguageInfo? Language { get; set; }

        [JsonPropertyName("matches")]
        public Match[] Matches { get; set; } = Array.Empty<Match>();
    }

    public class SoftwareInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    public class LanguageInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }

    public class Match
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("shortMessage")]
        public string ShortMessage { get; set; } = string.Empty;

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("replacements")]
        public Replacement[] Replacements { get; set; } = Array.Empty<Replacement>();

        [JsonPropertyName("context")]
        public Context? Context { get; set; }

        [JsonPropertyName("rule")]
        public Rule? Rule { get; set; }
    }

    public class Replacement
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    public class Context
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }
    }

    public class Rule
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("issueType")]
        public string IssueType { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public Category? Category { get; set; }
    }

    public class Category
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
