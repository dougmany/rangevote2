namespace RangeVote2.Data
{
    public interface IThemeService
    {
        /// <summary>
        /// Gets the current theme name for the user (defaults to 'cow' for anonymous users)
        /// </summary>
        Task<string> GetCurrentThemeAsync();

        /// <summary>
        /// Sets the theme for the current user
        /// </summary>
        Task SetThemeAsync(string theme);

        /// <summary>
        /// Gets themed text for a given key
        /// </summary>
        string GetText(string key);

        /// <summary>
        /// Gets themed emoji for a given key
        /// </summary>
        string GetEmoji(string key);

        /// <summary>
        /// Gets the CSS class for the current theme
        /// </summary>
        string GetThemeClass();

        /// <summary>
        /// Gets all available themes
        /// </summary>
        List<ThemeInfo> GetAvailableThemes();
    }

    public class ThemeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
        public string PrimaryColor { get; set; } = string.Empty;
    }
}
