namespace GrammrPop.Models
{
    /// <summary>
    /// UI-friendly wrapper around a LanguageTool Match for binding to ListView
    /// </summary>
    public class SuggestionItem
    {
        public Match Match { get; set; } = null!;
        public bool Accepted { get; set; }
        public string Message => Match.Message;
        public string ErrorText { get; set; } = string.Empty;
        public string SuggestedReplacement { get; set; } = string.Empty;
        public int Offset => Match.Offset;
        public int Length => Match.Length;

        public string DisplayText =>
            $"\"{ErrorText}\" → \"{SuggestedReplacement}\": {Message}";
    }
}
