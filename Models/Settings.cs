namespace GrammrPop.Models
{
    public class Settings
    {
        public string Hotkey { get; set; } = "Ctrl+Alt+G";
        public string ApiEndpoint { get; set; } = "https://api.languagetool.org/v2/check";
        public string Language { get; set; } = "en-US";
        public bool AutoPaste { get; set; } = false;
        public int HistorySize { get; set; } = 20;
        public bool UseLocalServer { get; set; } = false;
        public string LocalServerUrl { get; set; } = "http://localhost:8081/v2/check";
        public string? ApiKey { get; set; } = null; // Optional, encrypted when saved
    }
}
