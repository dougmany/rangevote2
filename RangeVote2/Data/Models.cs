namespace RangeVote2.Data
{
    public class Ballot
    {
        public Guid Id { get; set; }
        public Candidate[]? Candidates { get; set; }
    }

    public class Candidate
    {
        public String? Name { get; set; }
        public Int32 Score { get; set; }
        public String? ElectionID { get; set; }
        public String? Description { get; set; }
        public String? ScoreString { get { return (Score / 10.0).ToString("N1"); } }
        public String? Image_link { get; set; }
    }

    public class DBCandidate
    {
        public Guid Guid { get; set; }
        public String? Name { get; set; }
        public Int32 Score { get; set; }
        public String? ElectionID { get; set; }
        public String? Description { get; set; }
        public String? Image_link { get; set; }
    }

    // ========== NEW BALLOT SYSTEM MODELS ==========

    // Enums
    public enum BallotStatus
    {
        Draft,      // Being created/edited
        Open,       // Accepting votes
        Closed,     // Voting ended
        Archived    // Historical
    }

    public enum ShareLinkPermission
    {
        View,       // See results only
        Vote,       // Vote + see results
        Admin       // Full access (view, vote, edit candidates, manage sharing)
    }

    public enum UserPermission
    {
        Viewer,     // View results only
        Voter,      // Vote + view results
        Editor,     // Vote, view, add/edit candidates
        Admin       // Full control except delete (owner only)
    }

    // Core Entities
    public class BallotMetadata
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid OwnerId { get; set; } // User who created it
        public Guid? OrganizationId { get; set; } // Null for personal ballots
        public BallotStatus Status { get; set; } = BallotStatus.Draft;
        public DateTime CreatedAt { get; set; }
        public DateTime? OpenDate { get; set; }
        public DateTime? CloseDate { get; set; }
        public bool IsOpen { get; set; } = true;
        public bool IsPublic { get; set; } // Discoverable in marketplace
        public int CandidateCount { get; set; } // Denormalized for display
        public int VoteCount { get; set; } // Denormalized for display
    }

    // Candidate definitions for a ballot (not votes)
    public class BallotCandidate
    {
        public Guid Id { get; set; }
        public Guid BallotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageLink { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Share links for anonymous/link-based access
    public class BallotShareLink
    {
        public Guid Id { get; set; }
        public Guid BallotId { get; set; }
        public string ShareToken { get; set; } = string.Empty; // URL-safe random string
        public ShareLinkPermission Permission { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; } = true;
        public int UseCount { get; set; } // Track usage
    }

    // User-based invitations
    public class BallotPermission
    {
        public Guid Id { get; set; }
        public Guid BallotId { get; set; }
        public Guid? UserId { get; set; } // Null for email-based invitations not yet accepted
        public string? InvitedEmail { get; set; } // For pending invitations
        public UserPermission Permission { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime? AcceptedAt { get; set; }
    }

    // Vote records (replaces current candidate table usage)
    public class Vote
    {
        public Guid Id { get; set; }
        public Guid BallotId { get; set; }
        public Guid CandidateId { get; set; } // References BallotCandidate
        public Guid UserId { get; set; }
        public int Score { get; set; } // 0-99 (displayed as 0.0-9.9)
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // View Models
    public class BallotListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public BallotStatus Status { get; set; }
        public bool IsOwner { get; set; }
        public bool IsOrganizationBallot { get; set; }
        public string? OrganizationName { get; set; }
        public UserPermission? MyPermission { get; set; }
        public int CandidateCount { get; set; }
        public int VoteCount { get; set; }
        public DateTime? CloseDate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanVote { get; set; }
        public bool HasVoted { get; set; }
    }

    // View model for creating a ballot
    public class CreateBallotModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? OrganizationId { get; set; } // User selects from their orgs
        public DateTime? CloseDate { get; set; }
        public bool IsPublic { get; set; } // Discoverable in marketplace
        public List<CreateCandidateModel> Candidates { get; set; } = new();
    }

    public class CreateCandidateModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageLink { get; set; }
    }

    // Permission checking result
    public class BallotAccess
    {
        public bool CanAccess { get; set; }
        public bool CanView { get; set; }
        public bool CanVote { get; set; }
        public bool CanEdit { get; set; }
        public bool IsOwner { get; set; }
        public bool ViaShareLink { get; set; }
    }

    // ========== GROUP/ORGANIZATION VIEW MODELS ==========

    public class OrganizationListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public bool IsOwner { get; set; }
        public bool IsMember { get; set; }
        public string? MyRole { get; set; }
        public int MemberCount { get; set; }
        public int BallotCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateOrganizationModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
    }

    public class OrganizationMemberListItem
    {
        public Guid MemberId { get; set; }
        public Guid UserId { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string Role { get; set; } = "Member";
        public DateTime JoinedAt { get; set; }
    }

    // ========== BALLOT MARKETPLACE VIEW MODELS ==========

    public class PublicBallotListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? OrganizationName { get; set; }
        public Guid? OrganizationId { get; set; }
        public int CandidateCount { get; set; }
        public int VoteCount { get; set; }
        public DateTime? CloseDate { get; set; }
        public bool IsClosingSoon { get; set; } // Within 7 days
        public DateTime CreatedAt { get; set; }
    }

    // ========== IMPORT RESULT MODELS ==========

    public class ImportResult
    {
        public int BallotsImported { get; set; }
        public int CandidatesImported { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool Success => Errors.Count == 0;
    }
}
