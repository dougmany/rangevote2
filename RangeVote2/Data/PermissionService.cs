namespace RangeVote2.Data
{
    public interface IPermissionService
    {
        Task<BallotAccess> CheckAccessAsync(Guid ballotId, Guid? userId, string? shareToken);
    }

    public class PermissionService : IPermissionService
    {
        private readonly IRangeVoteRepository _repo;

        public PermissionService(IRangeVoteRepository repo)
        {
            _repo = repo;
        }

        public async Task<BallotAccess> CheckAccessAsync(Guid ballotId, Guid? userId, string? shareToken)
        {
            // 1. Check share link first (allows anonymous access)
            if (!string.IsNullOrEmpty(shareToken))
            {
                var shareLink = await _repo.GetShareLinkByTokenAsync(shareToken);
                if (shareLink != null && shareLink.IsActive &&
                    (!shareLink.ExpiresAt.HasValue || shareLink.ExpiresAt > DateTime.UtcNow))
                {
                    // Increment use count
                    await _repo.IncrementShareLinkUseCountAsync(shareLink.Id);

                    return new BallotAccess
                    {
                        CanAccess = true,
                        CanView = true,
                        CanVote = shareLink.Permission >= ShareLinkPermission.Vote,
                        CanEdit = shareLink.Permission >= ShareLinkPermission.Admin,
                        IsOwner = false,
                        ViaShareLink = true
                    };
                }
            }

            // 2. Check user-based permissions
            if (userId.HasValue)
            {
                var permission = await _repo.GetUserPermissionAsync(ballotId, userId.Value);

                if (permission.HasValue)
                {
                    return new BallotAccess
                    {
                        CanAccess = true,
                        CanView = true,
                        CanVote = permission >= UserPermission.Voter,
                        CanEdit = permission >= UserPermission.Editor,
                        IsOwner = permission == UserPermission.Admin,
                        ViaShareLink = false
                    };
                }
            }

            return new BallotAccess { CanAccess = false };
        }
    }
}
