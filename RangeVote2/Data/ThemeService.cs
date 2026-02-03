using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace RangeVote2.Data
{
    public class ThemeService : IThemeService
    {
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly IRangeVoteRepository _repository;
        private readonly IJSRuntime _jsRuntime;
        private string? _cachedTheme;

        public ThemeService(
            AuthenticationStateProvider authStateProvider,
            IRangeVoteRepository repository,
            IJSRuntime jsRuntime)
        {
            _authStateProvider = authStateProvider;
            _repository = repository;
            _jsRuntime = jsRuntime;
        }

        // Theme vocabulary mappings
        private static readonly Dictionary<string, Dictionary<string, string>> ThemeVocabulary = new()
        {
            ["cow"] = new Dictionary<string, string>
            {
                ["animal"] = "🐄",
                ["group"] = "herd",
                ["group_possessive"] = "herd's",
                ["place"] = "barn",
                ["place_plural"] = "pastures",
                ["action_vote"] = "graze",
                ["action_voting"] = "grazing",
                ["join_action"] = "Join the Herd",
                ["enter_action"] = "Enter the Barn",
                ["leave_action"] = "Leave Barn",
                ["my_places"] = "My Pastures",
                ["open_area"] = "The Open Range",
                ["my_groups"] = "My Herds",
                ["tagline"] = "Where the herd gathers to decide",
                ["loading"] = "Loading the pasture...",
                ["closed"] = "This pasture is fenced off",
                ["save_vote"] = "Save My Grazing",
                ["results"] = "The Digest",
                ["create_action"] = "Plant a Pasture",
                ["member"] = "cow",
                ["members"] = "cows",
                ["voting_action"] = "chewing on decisions"
            },
            ["deer"] = new Dictionary<string, string>
            {
                ["animal"] = "🦌",
                ["group"] = "herd",
                ["group_possessive"] = "herd's",
                ["place"] = "forest",
                ["place_plural"] = "glades",
                ["action_vote"] = "forage",
                ["action_voting"] = "foraging",
                ["join_action"] = "Join the Herd",
                ["enter_action"] = "Enter the Forest",
                ["leave_action"] = "Leave Forest",
                ["my_places"] = "My Glades",
                ["open_area"] = "The Wild Woods",
                ["my_groups"] = "My Herds",
                ["tagline"] = "Where the herd gathers to decide",
                ["loading"] = "Loading the glade...",
                ["closed"] = "This glade is sealed",
                ["save_vote"] = "Save My Foraging",
                ["results"] = "The Outcome",
                ["create_action"] = "Plant a Glade",
                ["member"] = "deer",
                ["members"] = "deer",
                ["voting_action"] = "weighing the options"
            },
            ["bison"] = new Dictionary<string, string>
            {
                ["animal"] = "🦬",
                ["group"] = "tribe",
                ["group_possessive"] = "tribe's",
                ["place"] = "range",
                ["place_plural"] = "ranges",
                ["action_vote"] = "roam",
                ["action_voting"] = "roaming",
                ["join_action"] = "Join the Tribe",
                ["enter_action"] = "Enter the Range",
                ["leave_action"] = "Leave Range",
                ["my_places"] = "My Ranges",
                ["open_area"] = "The Open Plains",
                ["my_groups"] = "My Tribes",
                ["tagline"] = "Where the tribe gathers to decide",
                ["loading"] = "Loading the range...",
                ["closed"] = "This range is closed",
                ["save_vote"] = "Save My Roaming",
                ["results"] = "The Result",
                ["create_action"] = "Create a Range",
                ["member"] = "bison",
                ["members"] = "bison",
                ["voting_action"] = "ranging across choices"
            },
            ["plain"] = new Dictionary<string, string>
            {
                ["animal"] = "📝",
                ["group"] = "group",
                ["group_possessive"] = "group's",
                ["place"] = "room",
                ["place_plural"] = "ballots",
                ["action_vote"] = "vote",
                ["action_voting"] = "voting",
                ["join_action"] = "Join Now",
                ["enter_action"] = "Login",
                ["leave_action"] = "Logout",
                ["my_places"] = "My Ballots",
                ["open_area"] = "Public Ballots",
                ["my_groups"] = "My Groups",
                ["tagline"] = "Democratic range voting",
                ["loading"] = "Loading ballot...",
                ["closed"] = "Voting has ended",
                ["save_vote"] = "Submit Vote",
                ["results"] = "Results",
                ["create_action"] = "Create Ballot",
                ["member"] = "member",
                ["members"] = "members",
                ["voting_action"] = "making decisions"
            }
        };

        public async Task<string> GetCurrentThemeAsync()
        {
            if (_cachedTheme != null)
                return _cachedTheme;

            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                // Get user ID from claims
                var userIdClaim = user.FindFirst("userId");
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    _cachedTheme = await _repository.GetUserThemeAsync(userId);
                    return _cachedTheme;
                }
            }

            // For anonymous users, try to get from localStorage
            try
            {
                var theme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "theme");
                _cachedTheme = string.IsNullOrEmpty(theme) ? "cow" : theme;
                return _cachedTheme;
            }
            catch
            {
                // If JS interop fails, default to cow
                _cachedTheme = "cow";
                return _cachedTheme;
            }
        }

        public async Task SetThemeAsync(string theme)
        {
            // Validate theme
            if (!ThemeVocabulary.ContainsKey(theme))
                throw new ArgumentException($"Invalid theme: {theme}");

            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                // Save to database for authenticated users
                var userIdClaim = user.FindFirst("userId");
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    await _repository.UpdateUserThemeAsync(userId, theme);
                }
            }

            // Always save to localStorage for persistence
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "theme", theme);
            }
            catch
            {
                // Ignore JS interop errors
            }

            // Clear cache
            _cachedTheme = theme;
        }

        public string GetText(string key)
        {
            var theme = _cachedTheme ?? "cow";

            if (ThemeVocabulary.TryGetValue(theme, out var vocabulary) &&
                vocabulary.TryGetValue(key, out var text))
            {
                return text;
            }

            // Fallback to cow theme
            if (ThemeVocabulary["cow"].TryGetValue(key, out var fallback))
            {
                return fallback;
            }

            return key; // Return key if not found
        }

        public string GetEmoji(string key)
        {
            return GetText(key);
        }

        public string GetThemeClass()
        {
            var theme = _cachedTheme ?? "cow";
            return $"theme-{theme}";
        }

        public List<ThemeInfo> GetAvailableThemes()
        {
            return new List<ThemeInfo>
            {
                new ThemeInfo
                {
                    Id = "cow",
                    Name = "Cow/Cattle",
                    Description = "Pastoral farm theme with green pastures and barn vibes",
                    Emoji = "🐄",
                    PrimaryColor = "#22c55e"
                },
                new ThemeInfo
                {
                    Id = "deer",
                    Name = "Deer",
                    Description = "Woodland forest theme with earthy browns and greens",
                    Emoji = "🦌",
                    PrimaryColor = "#8b4513"
                },
                new ThemeInfo
                {
                    Id = "bison",
                    Name = "Bison",
                    Description = "American rangeland theme with prairie tans and mesa colors",
                    Emoji = "🦬",
                    PrimaryColor = "#cd853f"
                },
                new ThemeInfo
                {
                    Id = "plain",
                    Name = "Plain/Ballot",
                    Description = "Professional ballot theme with clean blues and grays",
                    Emoji = "📝",
                    PrimaryColor = "#3182ce"
                }
            };
        }
    }
}
