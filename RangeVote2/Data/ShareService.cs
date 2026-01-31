using System.Security.Cryptography;
using Microsoft.AspNetCore.Components;

namespace RangeVote2.Data
{
    public interface IShareService
    {
        string GenerateSecureToken();
        Task<bool> ValidateShareLinkAsync(string token);
        string FormatShareUrl(string token, NavigationManager navManager);
    }

    public class ShareService : IShareService
    {
        private readonly IRangeVoteRepository _repo;

        public ShareService(IRangeVoteRepository repo)
        {
            _repo = repo;
        }

        public string GenerateSecureToken()
        {
            var tokenBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBytes);
            }
            var token = Convert.ToBase64String(tokenBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            return token;
        }

        public async Task<bool> ValidateShareLinkAsync(string token)
        {
            var shareLink = await _repo.GetShareLinkByTokenAsync(token);

            if (shareLink == null || !shareLink.IsActive)
                return false;

            if (shareLink.ExpiresAt.HasValue && shareLink.ExpiresAt.Value < DateTime.UtcNow)
                return false;

            return true;
        }

        public string FormatShareUrl(string token, NavigationManager navManager)
        {
            return navManager.ToAbsoluteUri($"/vote/link?t={token}").ToString();
        }
    }
}
