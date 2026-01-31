using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Security.Claims;

namespace RangeVote2.Data
{
    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly ProtectedSessionStorage _sessionStorage;
        private ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        private ClaimsPrincipal? _currentUser;

        public CustomAuthenticationStateProvider(ProtectedSessionStorage sessionStorage)
        {
            _sessionStorage = sessionStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                // Return cached user if available
                if (_currentUser != null)
                {
                    return new AuthenticationState(_currentUser);
                }

                // Try to get user from session storage
                // This will throw an InvalidOperationException during prerendering
                var userSessionStorageResult = await _sessionStorage.GetAsync<string>("userId");
                var userId = userSessionStorageResult.Success ? userSessionStorageResult.Value : null;

                if (string.IsNullOrEmpty(userId))
                {
                    return new AuthenticationState(_anonymous);
                }

                // Create claims from stored userId
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId)
                };

                var identity = new ClaimsIdentity(claims, "CustomAuth");
                _currentUser = new ClaimsPrincipal(identity);

                return new AuthenticationState(_currentUser);
            }
            catch (InvalidOperationException)
            {
                // ProtectedSessionStorage can't be used during prerendering
                // Return anonymous user and let the circuit reconnect handle authentication
                return new AuthenticationState(_anonymous);
            }
            catch
            {
                return new AuthenticationState(_anonymous);
            }
        }

        public async Task MarkUserAsAuthenticated(ApplicationUser user)
        {
            try
            {
                await _sessionStorage.SetAsync("userId", user.Id.ToString());

                var claimsPrincipal = CreateClaimsPrincipal(user);
                _currentUser = claimsPrincipal;
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
            }
            catch
            {
                // If storage fails, still update the current user for this session
                var claimsPrincipal = CreateClaimsPrincipal(user);
                _currentUser = claimsPrincipal;
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
            }
        }

        public async Task MarkUserAsLoggedOut()
        {
            try
            {
                await _sessionStorage.DeleteAsync("userId");
            }
            catch
            {
                // Ignore storage errors on logout
            }

            _currentUser = null;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
        }

        private ClaimsPrincipal CreateClaimsPrincipal(ApplicationUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.DisplayName ?? user.Email)
            };

            if (user.OrganizationId.HasValue)
            {
                claims.Add(new Claim("OrganizationId", user.OrganizationId.Value.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "CustomAuth");
            return new ClaimsPrincipal(identity);
        }
    }
}
