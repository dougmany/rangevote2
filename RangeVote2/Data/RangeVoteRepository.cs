using Dapper;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System.Globalization;

namespace RangeVote2.Data
{
    public interface IRangeVoteRepository
    {
        // Legacy ballot methods
        Task<Ballot> GetBallotAsync(Guid id, string electionId);
        Task<List<DBCandidate>> GetBallotsAsync(string electionId);
        Task PutBallotAsync(Ballot ballot, string electionId);
        Task<Ballot> GetResultAsync(string electionId);
        Task<List<VoteCount>> GetVotersAsync();
        List<string> GetBallots();
        Task<bool> UserCanAccessElection(Guid userId, string electionId);
        Task<List<string>> GetBallotsForOrganizationAsync(Guid? organizationId);

        // ========== NEW BALLOT SYSTEM METHODS ==========

        // Ballot CRUD
        Task<BallotMetadata> CreateBallotAsync(CreateBallotModel model, Guid userId);
        Task<BallotMetadata?> GetBallotMetadataAsync(Guid ballotId);
        Task<List<BallotListItem>> GetBallotsForUserAsync(Guid userId);
        Task UpdateBallotAsync(BallotMetadata ballot);
        Task DeleteBallotAsync(Guid ballotId, Guid userId);

        // Candidate management
        Task<List<BallotCandidate>> GetCandidatesAsync(Guid ballotId);
        Task<BallotCandidate> AddCandidateAsync(Guid ballotId, CreateCandidateModel model);
        Task UpdateCandidateAsync(BallotCandidate candidate);
        Task DeleteCandidateAsync(Guid candidateId);

        // Voting
        Task<List<Vote>> GetUserVotesAsync(Guid ballotId, Guid userId);
        Task SaveVotesAsync(Guid ballotId, Guid userId, Dictionary<Guid, int> candidateScores);
        Task<Dictionary<Guid, double>> GetResultsAsync(Guid ballotId);

        // Share Links
        Task<BallotShareLink> CreateShareLinkAsync(Guid ballotId, ShareLinkPermission permission, Guid creatorId, DateTime? expiresAt = null);
        Task<List<BallotShareLink>> GetShareLinksAsync(Guid ballotId);
        Task<BallotShareLink?> GetShareLinkByTokenAsync(string token);
        Task DeactivateShareLinkAsync(Guid shareLinkId);
        Task IncrementShareLinkUseCountAsync(Guid shareLinkId);

        // User Permissions
        Task<BallotPermission> InviteUserAsync(Guid ballotId, string email, UserPermission permission, Guid inviterId);
        Task<List<BallotPermission>> GetBallotPermissionsAsync(Guid ballotId);
        Task UpdatePermissionAsync(Guid permissionId, UserPermission newPermission);
        Task RevokePermissionAsync(Guid permissionId);

        // Permission checking
        Task<UserPermission?> GetUserPermissionAsync(Guid ballotId, Guid userId);
        Task<bool> CanUserAccessBallotAsync(Guid ballotId, Guid userId);
        Task<bool> CanUserVoteAsync(Guid ballotId, Guid userId);
        Task<bool> CanUserEditAsync(Guid ballotId, Guid userId);
        Task<bool> CanUserAdminAsync(Guid ballotId, Guid userId);

        // Lifecycle
        Task CloseBallotAsync(Guid ballotId);
        Task OpenBallotAsync(Guid ballotId);
        Task<List<BallotMetadata>> GetBallotsToAutoCloseAsync(DateTime now);

        // Helper Methods
        Task<List<Guid>> GetUserOrganizationsAsync(Guid userId);

        // ========== ORGANIZATION/GROUP MANAGEMENT METHODS ==========
        Task<Organization?> GetOrganizationAsync(Guid orgId);
        Task<List<OrganizationListItem>> GetOrganizationsForUserAsync(Guid userId);
        Task<List<OrganizationListItem>> GetPublicOrganizationsAsync(Guid? excludeUserId = null);
        Task<Organization> CreateOrganizationAsync(string name, string? description, bool isPublic, Guid ownerId);
        Task UpdateOrganizationAsync(Organization org);
        Task DeleteOrganizationAsync(Guid orgId, Guid userId);
        Task JoinOrganizationAsync(Guid orgId, Guid userId);
        Task LeaveOrganizationAsync(Guid orgId, Guid userId);
        Task<List<OrganizationMemberListItem>> GetOrganizationMembersAsync(Guid orgId);
        Task<bool> IsUserMemberOfOrganizationAsync(Guid orgId, Guid userId);
        Task<string?> GetUserRoleInOrganizationAsync(Guid orgId, Guid userId);

        // ========== BALLOT MARKETPLACE METHODS ==========
        Task<List<PublicBallotListItem>> GetPublicBallotsAsync(string? searchTerm, Guid? organizationId, bool? closingSoon, Guid excludeUserId);
        Task JoinBallotAsync(Guid ballotId, Guid userId);
        Task<List<OrganizationListItem>> GetOrganizationsWithPublicBallotsAsync();

        // ========== IMPORT METHODS ==========
        Task<ImportResult> ImportBallotsFromJsonAsync(Guid userId, string? jsonFilePath = null, List<string>? electionIdsToImport = null);
        Task<Dictionary<string, int>> PreviewBallotsFromJsonAsync(string? jsonFilePath = null);

        // ========== THEME PREFERENCE METHODS ==========
        Task UpdateUserThemeAsync(Guid userId, string theme);
        Task<string> GetUserThemeAsync(Guid userId);
    }

    public class RangeVoteRepository : IRangeVoteRepository
    {
        private readonly ApplicationConfig _config;

        public RangeVoteRepository(ApplicationConfig config)
        {
            _config = config;
        }


        public async Task<Ballot> GetBallotAsync(Guid guid, string electionId)
        {
            Console.WriteLine($"[DEBUG GetBallotAsync] Loading ballot for GUID: {guid}, Election: {electionId}");
            
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<DBCandidate>(
                @"SELECT * FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = guid, electionID = electionId }
            );
            
            Console.WriteLine($"[DEBUG GetBallotAsync] Found {candidates.Count()} candidate records");

            var ballot = new Ballot
            {
                Id = guid,
                Candidates = candidates.Select(c => new Candidate
                {
                    Name = c.Name,
                    Score = c.Score,
                    ElectionID = c.ElectionID,
                    Description = c.Description,
                    Image_link = c.Image_link
                }).ToArray()
            };
            
            if (ballot.Candidates.Length > 0)
            {
                Console.WriteLine($"[DEBUG GetBallotAsync] Returning ballot with {ballot.Candidates.Length} candidates");
                return ballot;
            }

            Console.WriteLine($"[DEBUG GetBallotAsync] No existing ballot found, returning default candidates");
            return new Ballot { Id = guid, Candidates = DefaultCandidates(electionId) };
        }
        public async Task<List<DBCandidate>> GetBallotsAsync(string electionId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var ballots = await connection.QueryAsync<DBCandidate>(
                @"SELECT * FROM candidate WHERE ElectionID = @electionID;",
                new { electionID = electionId }
            );

            return ballots.ToList();
        }

        public async Task PutBallotAsync(Ballot ballot, string electionId)
        {
            Console.WriteLine($"[DEBUG PutBallotAsync] Saving ballot for GUID: {ballot.Id}, Election: {electionId}");
            Console.WriteLine($"[DEBUG PutBallotAsync] Number of candidates: {ballot.Candidates?.Length ?? 0}");
            
            using var connection = new SqliteConnection(_config.DatabaseName);
            
            // Debug: Check what's in DB before delete
            var beforeDelete = await connection.QueryAsync<dynamic>(
                @"SELECT GUID, Name, Score FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = ballot.Id, electionID = electionId }
            );
            Console.WriteLine($"[DEBUG PutBallotAsync] Records before delete: {beforeDelete.Count()}");
            
            await connection.ExecuteAsync(
                @"DELETE FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = ballot.Id, electionID = electionId }
            );
            
            Console.WriteLine($"[DEBUG PutBallotAsync] Deleted existing records");

            if (ballot.Candidates is not null)
            {
                var data = ballot.Candidates.Select(c => new DBCandidate
                {
                    Guid = ballot.Id,
                    Name = c.Name,
                    Score = c.Score,
                    ElectionID = c.ElectionID,
                    Description = c.Description,
                    Image_link = c.Image_link
                });

                String query = "INSERT INTO candidate (Guid, Name, Score, ElectionID, Description, Image_link) Values (@Guid, @Name, @Score, @ElectionID, @Description, @Image_link)";
                await connection.ExecuteAsync(query, data);
                
                Console.WriteLine($"[DEBUG PutBallotAsync] Inserted {ballot.Candidates.Length} candidate records");
                
                // Debug: Verify what was saved
                var afterInsert = await connection.QueryAsync<dynamic>(
                    @"SELECT GUID, Name, Score FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                    new { guid = ballot.Id, electionID = electionId }
                );
                Console.WriteLine($"[DEBUG PutBallotAsync] Records after insert: {afterInsert.Count()}");
                foreach (var record in afterInsert)
                {
                    Console.WriteLine($"[DEBUG PutBallotAsync] Saved - GUID: {record.GUID}, Name: {record.Name}, Score: {record.Score}");
                }
            }
        }

        public async Task<Ballot> GetResultAsync(string electionId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<Candidate>(
                    @"SELECT Name, CAST(ROUND(SUM(Score)/COUNT(DISTINCT Guid))AS SIGNED) AS Score 
                        FROM candidate
                        WHERE ElectionID = @electionID
                        GROUP BY Name 
                        ORDER BY SUM(Score) DESC;",
                    new { electionID = electionId }
                );
            var ballot = new Ballot
            {
                Candidates = candidates.ToArray()
            };
            if (ballot.Candidates.Length > 0)
            {
                return ballot;
            }

            return new Ballot { Candidates = DefaultCandidates(electionId) };
        }

        public async Task<List<VoteCount>> GetVotersAsync()
        {
            var voters = new List<VoteCount>();
            
            if (_config.ElectionIds is not null && _config.ElectionIds.Length > 0)
            {
                try
                {
                    using var connection = new SqliteConnection(_config.DatabaseName);
                    
                    // Debug: See all GUIDs in the database
                    var allCandidates = await connection.QueryAsync<dynamic>(
                        @"SELECT GUID, ElectionID, Name, Score FROM candidate;"
                    );
                    Console.WriteLine($"[DEBUG GetVotersAsync] Total candidate records: {allCandidates.Count()}");
                    foreach (var c in allCandidates)
                    {
                        Console.WriteLine($"[DEBUG] GUID: {c.GUID}, Election: {c.ElectionID}, Name: {c.Name}, Score: {c.Score}");
                    }
                    
                    // Debug: See distinct GUIDs
                    var distinctGuids = await connection.QueryAsync<dynamic>(
                        @"SELECT DISTINCT GUID, ElectionID FROM candidate;"
                    );
                    Console.WriteLine($"[DEBUG GetVotersAsync] Distinct GUID/Election combos: {distinctGuids.Count()}");
                    foreach (var g in distinctGuids)
                    {
                        Console.WriteLine($"[DEBUG] Distinct GUID: {g.GUID}, Election: {g.ElectionID}");
                    }
                    
                    var query = @"SELECT ElectionID, COUNT(1) Voters FROM ( SELECT ElectionID, COUNT(GUID) FROM candidate GROUP BY GUID) GROUP BY ElectionID;";
                    var results = await connection.QueryAsync<VoteCount>(query);
                    Console.WriteLine($"[DEBUG GetVotersAsync] Query results count: {results.Count()}");
                    foreach (var r in results)
                    {
                        Console.WriteLine($"[DEBUG GetVotersAsync] ElectionID: {r.ElectionID}, Voters: {r.Voters}");
                    }
                    
                    voters = results.Where(r => _config.ElectionIds.Contains(r.ElectionID)).ToList();
                    Console.WriteLine($"[DEBUG GetVotersAsync] Filtered voters count: {voters.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG GetVotersAsync] Exception: {ex.Message}");
                    // If database query fails, return empty list rather than crashing
                    return new List<VoteCount>();
                }
            }
            return voters;
        }

        public List<string> GetBallots()
        {
            return _config.ElectionIds?.ToList() ?? new List<string>();
        }

        Candidate[] DefaultCandidates(string electionId)
        {
            string json = File.ReadAllText("Ballots.json");
            var allBallots = JsonConvert.DeserializeObject<Candidate[]>(json);
            if (allBallots is not null)
            {
                return allBallots.Where(b => b.ElectionID == electionId)
                    .OrderBy(b => b.Score)
                    .ThenBy(b => Guid.NewGuid())
                    .ToArray();
            }

            return new Candidate[] { new Candidate() };
        }

        public async Task<bool> UserCanAccessElection(Guid userId, string electionId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Get user's organization
            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE Id = @Id",
                new { Id = userId.ToString() }
            );

            if (user == null)
                return false;

            // For now, all users in an organization can access all elections
            // In the future, you can add election-level permissions
            return true;
        }

        public async Task<List<string>> GetBallotsForOrganizationAsync(Guid? organizationId)
        {
            // For now, return all configured ballots
            // In the future, filter by organization
            return GetBallots();
        }

        // ========== NEW BALLOT SYSTEM IMPLEMENTATIONS ==========

        // Ballot CRUD
        public async Task<BallotMetadata> CreateBallotAsync(CreateBallotModel model, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var ballotId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var ballot = new BallotMetadata
            {
                Id = ballotId,
                Name = model.Name,
                Description = model.Description,
                OwnerId = userId,
                OrganizationId = model.OrganizationId,
                Status = BallotStatus.Open,
                CreatedAt = now,
                CloseDate = model.CloseDate,
                IsOpen = true,
                IsPublic = model.IsPublic,
                CandidateCount = model.Candidates.Count,
                VoteCount = 0
            };

            await connection.ExecuteAsync(
                @"INSERT INTO ballots (Id, Name, Description, OwnerId, OrganizationId, Status, CreatedAt, CloseDate, IsOpen, IsPublic, CandidateCount, VoteCount)
                  VALUES (@Id, @Name, @Description, @OwnerId, @OrganizationId, @Status, @CreatedAt, @CloseDate, @IsOpen, @IsPublic, @CandidateCount, @VoteCount)",
                new
                {
                    Id = ballot.Id.ToString(),
                    ballot.Name,
                    ballot.Description,
                    OwnerId = ballot.OwnerId.ToString(),
                    OrganizationId = ballot.OrganizationId?.ToString(),
                    Status = ballot.Status.ToString(),
                    ballot.CreatedAt,
                    ballot.CloseDate,
                    ballot.IsOpen,
                    IsPublic = ballot.IsPublic ? 1 : 0,
                    ballot.CandidateCount,
                    ballot.VoteCount
                }
            );

            // Create candidates
            foreach (var candidateModel in model.Candidates)
            {
                var candidateId = Guid.NewGuid();

                await connection.ExecuteAsync(
                    @"INSERT INTO ballot_candidates (Id, BallotId, Name, Description, ImageLink, CreatedAt)
                      VALUES (@Id, @BallotId, @Name, @Description, @ImageLink, @CreatedAt)",
                    new
                    {
                        Id = candidateId.ToString(),
                        BallotId = ballotId.ToString(),
                        Name = candidateModel.Name,
                        Description = candidateModel.Description,
                        ImageLink = candidateModel.ImageLink,
                        CreatedAt = now
                    }
                );
            }

            return ballot;
        }

        public async Task<BallotMetadata?> GetBallotMetadataAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM ballots WHERE Id = @Id",
                new { Id = ballotId.ToString() }
            );

            if (result == null) return null;

            return new BallotMetadata
            {
                Id = Guid.Parse(result.Id),
                Name = result.Name,
                Description = result.Description,
                OwnerId = Guid.Parse(result.OwnerId),
                OrganizationId = result.OrganizationId != null ? Guid.Parse(result.OrganizationId) : null,
                Status = Enum.Parse<BallotStatus>(result.Status),
                CreatedAt = DateTime.Parse(result.CreatedAt),
                OpenDate = result.OpenDate != null ? DateTime.Parse(result.OpenDate) : null,
                CloseDate = result.CloseDate != null ? DateTime.Parse(result.CloseDate) : null,
                IsOpen = result.IsOpen == 1,
                IsPublic = result.IsPublic == 1,
                CandidateCount = Convert.ToInt32(result.CandidateCount),
                VoteCount = Convert.ToInt32(result.VoteCount)
            };
        }

        public async Task<List<BallotListItem>> GetBallotsForUserAsync(Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Get user's organizations
            var orgs = await GetUserOrganizationsAsync(userId);
            var orgIdStrings = orgs.Select(o => o.ToString()).ToList();
            var userIdString = userId.ToString();

            // Build query - handle empty org list separately to avoid Dapper IN clause issues
            string sql;
            object parameters;

            if (orgIdStrings.Count > 0)
            {
                sql = @"
                    SELECT DISTINCT
                        b.Id,
                        b.Name,
                        b.Description,
                        b.Status,
                        b.OwnerId,
                        b.OrganizationId,
                        b.CandidateCount,
                        b.VoteCount,
                        b.CloseDate,
                        o.Name as OrganizationName,
                        bp.Permission as MyPermission,
                        CASE WHEN b.OwnerId = @UserId THEN 1 ELSE 0 END as IsOwner,
                        CASE WHEN EXISTS(SELECT 1 FROM votes WHERE BallotId = b.Id AND UserId = @UserId) THEN 1 ELSE 0 END as HasVoted
                    FROM ballots b
                    LEFT JOIN organizations o ON b.OrganizationId = o.Id
                    LEFT JOIN ballot_permissions bp ON b.Id = bp.BallotId AND bp.UserId = @UserId
                    WHERE
                        b.OwnerId = @UserId
                        OR bp.UserId = @UserId
                        OR b.OrganizationId IN @OrgIds
                    ORDER BY b.CreatedAt DESC
                ";
                parameters = new { UserId = userIdString, OrgIds = orgIdStrings };
            }
            else
            {
                sql = @"
                    SELECT DISTINCT
                        b.Id,
                        b.Name,
                        b.Description,
                        b.Status,
                        b.OwnerId,
                        b.OrganizationId,
                        b.CandidateCount,
                        b.VoteCount,
                        b.CloseDate,
                        o.Name as OrganizationName,
                        bp.Permission as MyPermission,
                        CASE WHEN b.OwnerId = @UserId THEN 1 ELSE 0 END as IsOwner,
                        CASE WHEN EXISTS(SELECT 1 FROM votes WHERE BallotId = b.Id AND UserId = @UserId) THEN 1 ELSE 0 END as HasVoted
                    FROM ballots b
                    LEFT JOIN organizations o ON b.OrganizationId = o.Id
                    LEFT JOIN ballot_permissions bp ON b.Id = bp.BallotId AND bp.UserId = @UserId
                    WHERE
                        b.OwnerId = @UserId
                        OR bp.UserId = @UserId
                    ORDER BY b.CreatedAt DESC
                ";
                parameters = new { UserId = userIdString };
            }

            var results = await connection.QueryAsync<dynamic>(sql, parameters);

            var items = new List<BallotListItem>();
            foreach (var r in results)
            {
                var item = new BallotListItem
                {
                    Id = Guid.Parse((string)r.Id),
                    Name = (string)r.Name,
                    Description = (string?)r.Description,
                    Status = Enum.Parse<BallotStatus>((string)r.Status),
                    IsOwner = Convert.ToInt64(r.IsOwner) == 1,
                    IsOrganizationBallot = r.OrganizationId != null,
                    OrganizationName = (string?)r.OrganizationName,
                    CandidateCount = Convert.ToInt32(r.CandidateCount),
                    VoteCount = Convert.ToInt32(r.VoteCount),
                    CloseDate = r.CloseDate != null ? DateTime.Parse((string)r.CloseDate) : null,
                    HasVoted = Convert.ToInt64(r.HasVoted) == 1
                };

                // Parse permission
                if (r.MyPermission != null)
                {
                    item.MyPermission = Enum.Parse<UserPermission>(r.MyPermission);
                }

                // Calculate permissions
                var permission = await GetUserPermissionAsync(item.Id, userId);
                item.CanEdit = permission >= UserPermission.Editor;
                item.CanVote = permission >= UserPermission.Voter && item.Status == BallotStatus.Open;

                items.Add(item);
            }

            return items;
        }

        public async Task UpdateBallotAsync(BallotMetadata ballot)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                @"UPDATE ballots
                  SET Name = @Name, Description = @Description, CloseDate = @CloseDate,
                      Status = @Status, IsOpen = @IsOpen, IsPublic = @IsPublic
                  WHERE Id = @Id",
                new
                {
                    Id = ballot.Id.ToString(),
                    ballot.Name,
                    ballot.Description,
                    ballot.CloseDate,
                    Status = ballot.Status.ToString(),
                    ballot.IsOpen,
                    IsPublic = ballot.IsPublic ? 1 : 0
                }
            );
        }

        public async Task DeleteBallotAsync(Guid ballotId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Verify user is owner
            var ballot = await GetBallotMetadataAsync(ballotId);
            if (ballot == null || ballot.OwnerId != userId)
            {
                throw new UnauthorizedAccessException("Only the ballot owner can delete the ballot");
            }

            await connection.ExecuteAsync(
                "DELETE FROM ballots WHERE Id = @Id",
                new { Id = ballotId.ToString() }
            );
        }

        // Candidate management
        public async Task<List<BallotCandidate>> GetCandidatesAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<BallotCandidate>(
                "SELECT * FROM ballot_candidates WHERE BallotId = @BallotId ORDER BY RANDOM()",
                new { BallotId = ballotId.ToString() }
            );

            return candidates.ToList();
        }

        public async Task<BallotCandidate> AddCandidateAsync(Guid ballotId, CreateCandidateModel model)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidate = new BallotCandidate
            {
                Id = Guid.NewGuid(),
                BallotId = ballotId,
                Name = model.Name,
                Description = model.Description,
                ImageLink = model.ImageLink,
                CreatedAt = DateTime.UtcNow
            };

            await connection.ExecuteAsync(
                @"INSERT INTO ballot_candidates (Id, BallotId, Name, Description, ImageLink, CreatedAt)
                  VALUES (@Id, @BallotId, @Name, @Description, @ImageLink, @CreatedAt)",
                new
                {
                    Id = candidate.Id.ToString(),
                    BallotId = candidate.BallotId.ToString(),
                    candidate.Name,
                    candidate.Description,
                    candidate.ImageLink,
                    candidate.CreatedAt
                }
            );

            // Update candidate count
            await connection.ExecuteAsync(
                "UPDATE ballots SET CandidateCount = CandidateCount + 1 WHERE Id = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            return candidate;
        }

        public async Task UpdateCandidateAsync(BallotCandidate candidate)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                @"UPDATE ballot_candidates
                  SET Name = @Name, Description = @Description, ImageLink = @ImageLink
                  WHERE Id = @Id",
                new
                {
                    Id = candidate.Id.ToString(),
                    candidate.Name,
                    candidate.Description,
                    candidate.ImageLink
                }
            );
        }

        public async Task DeleteCandidateAsync(Guid candidateId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Get ballot ID before deleting
            var candidate = await connection.QueryFirstOrDefaultAsync<BallotCandidate>(
                "SELECT * FROM ballot_candidates WHERE Id = @Id",
                new { Id = candidateId.ToString() }
            );

            if (candidate != null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM ballot_candidates WHERE Id = @Id",
                    new { Id = candidateId.ToString() }
                );

                // Update candidate count
                await connection.ExecuteAsync(
                    "UPDATE ballots SET CandidateCount = CandidateCount - 1 WHERE Id = @BallotId",
                    new { BallotId = candidate.BallotId.ToString() }
                );
            }
        }

        // Voting
        public async Task<List<Vote>> GetUserVotesAsync(Guid ballotId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var votes = await connection.QueryAsync<Vote>(
                "SELECT * FROM votes WHERE BallotId = @BallotId AND UserId = @UserId",
                new { BallotId = ballotId.ToString(), UserId = userId.ToString() }
            );

            return votes.ToList();
        }

        public async Task SaveVotesAsync(Guid ballotId, Guid userId, Dictionary<Guid, int> candidateScores)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);
            var now = DateTime.UtcNow;

            foreach (var kvp in candidateScores)
            {
                var candidateId = kvp.Key;
                var score = kvp.Value;

                // Upsert vote
                var existing = await connection.QueryFirstOrDefaultAsync<Vote>(
                    "SELECT * FROM votes WHERE BallotId = @BallotId AND CandidateId = @CandidateId AND UserId = @UserId",
                    new { BallotId = ballotId.ToString(), CandidateId = candidateId.ToString(), UserId = userId.ToString() }
                );

                if (existing != null)
                {
                    await connection.ExecuteAsync(
                        "UPDATE votes SET Score = @Score, UpdatedAt = @UpdatedAt WHERE Id = @Id",
                        new { Score = score, UpdatedAt = now, Id = existing.Id.ToString() }
                    );
                }
                else
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO votes (Id, BallotId, CandidateId, UserId, Score, CreatedAt, UpdatedAt)
                          VALUES (@Id, @BallotId, @CandidateId, @UserId, @Score, @CreatedAt, @UpdatedAt)",
                        new
                        {
                            Id = Guid.NewGuid().ToString(),
                            BallotId = ballotId.ToString(),
                            CandidateId = candidateId.ToString(),
                            UserId = userId.ToString(),
                            Score = score,
                            CreatedAt = now,
                            UpdatedAt = now
                        }
                    );
                }
            }

            // Update vote count (distinct users who voted)
            var voteCount = await connection.QueryFirstAsync<int>(
                "SELECT COUNT(DISTINCT UserId) FROM votes WHERE BallotId = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            await connection.ExecuteAsync(
                "UPDATE ballots SET VoteCount = @VoteCount WHERE Id = @BallotId",
                new { VoteCount = voteCount, BallotId = ballotId.ToString() }
            );
        }

        public async Task<Dictionary<Guid, double>> GetResultsAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT CandidateId, AVG(CAST(Score AS REAL)) as AvgScore
                  FROM votes
                  WHERE BallotId = @BallotId
                  GROUP BY CandidateId",
                new { BallotId = ballotId.ToString() }
            );

            var dict = new Dictionary<Guid, double>();
            foreach (var r in results)
            {
                dict[Guid.Parse(r.CandidateId)] = (double)r.AvgScore;
            }

            return dict;
        }

        // Share Links
        public async Task<BallotShareLink> CreateShareLinkAsync(Guid ballotId, ShareLinkPermission permission, Guid creatorId, DateTime? expiresAt = null)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Generate secure token (will be done by ShareService, but fallback here)
            var tokenBytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBytes);
            }
            var token = Convert.ToBase64String(tokenBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            var shareLink = new BallotShareLink
            {
                Id = Guid.NewGuid(),
                BallotId = ballotId,
                ShareToken = token,
                Permission = permission,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = creatorId,
                ExpiresAt = expiresAt,
                IsActive = true,
                UseCount = 0
            };

            await connection.ExecuteAsync(
                @"INSERT INTO ballot_share_links (Id, BallotId, ShareToken, Permission, CreatedAt, CreatedBy, ExpiresAt, IsActive, UseCount)
                  VALUES (@Id, @BallotId, @ShareToken, @Permission, @CreatedAt, @CreatedBy, @ExpiresAt, @IsActive, @UseCount)",
                new
                {
                    Id = shareLink.Id.ToString(),
                    BallotId = shareLink.BallotId.ToString(),
                    ShareToken = shareLink.ShareToken,
                    Permission = shareLink.Permission.ToString(),
                    CreatedAt = shareLink.CreatedAt,
                    CreatedBy = shareLink.CreatedBy.ToString(),
                    ExpiresAt = shareLink.ExpiresAt,
                    IsActive = shareLink.IsActive ? 1 : 0,
                    UseCount = shareLink.UseCount
                }
            );

            return shareLink;
        }

        public async Task<List<BallotShareLink>> GetShareLinksAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var links = await connection.QueryAsync<dynamic>(
                "SELECT * FROM ballot_share_links WHERE BallotId = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            var result = new List<BallotShareLink>();
            foreach (var link in links)
            {
                result.Add(new BallotShareLink
                {
                    Id = Guid.Parse(link.Id),
                    BallotId = Guid.Parse(link.BallotId),
                    ShareToken = link.ShareToken,
                    Permission = Enum.Parse<ShareLinkPermission>(link.Permission),
                    CreatedAt = DateTime.Parse(link.CreatedAt),
                    CreatedBy = Guid.Parse(link.CreatedBy),
                    ExpiresAt = link.ExpiresAt != null ? DateTime.Parse(link.ExpiresAt) : null,
                    IsActive = link.IsActive == 1,
                    UseCount = link.UseCount
                });
            }

            return result;
        }

        public async Task<BallotShareLink?> GetShareLinkByTokenAsync(string token)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var link = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM ballot_share_links WHERE ShareToken = @Token",
                new { Token = token }
            );

            if (link == null) return null;

            return new BallotShareLink
            {
                Id = Guid.Parse(link.Id),
                BallotId = Guid.Parse(link.BallotId),
                ShareToken = link.ShareToken,
                Permission = Enum.Parse<ShareLinkPermission>(link.Permission),
                CreatedAt = DateTime.Parse(link.CreatedAt),
                CreatedBy = Guid.Parse(link.CreatedBy),
                ExpiresAt = link.ExpiresAt != null ? DateTime.Parse(link.ExpiresAt) : null,
                IsActive = link.IsActive == 1,
                UseCount = link.UseCount
            };
        }

        public async Task DeactivateShareLinkAsync(Guid shareLinkId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballot_share_links SET IsActive = 0 WHERE Id = @Id",
                new { Id = shareLinkId.ToString() }
            );
        }

        public async Task IncrementShareLinkUseCountAsync(Guid shareLinkId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballot_share_links SET UseCount = UseCount + 1 WHERE Id = @Id",
                new { Id = shareLinkId.ToString() }
            );
        }

        // User Permissions
        public async Task<BallotPermission> InviteUserAsync(Guid ballotId, string email, UserPermission permission, Guid inviterId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Check if user exists
            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE Email = @Email",
                new { Email = email }
            );

            var ballotPermission = new BallotPermission
            {
                Id = Guid.NewGuid(),
                BallotId = ballotId,
                UserId = user?.Id,
                InvitedEmail = email,
                Permission = permission,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = inviterId,
                AcceptedAt = user != null ? DateTime.UtcNow : null
            };

            await connection.ExecuteAsync(
                @"INSERT INTO ballot_permissions (Id, BallotId, UserId, InvitedEmail, Permission, CreatedAt, CreatedBy, AcceptedAt)
                  VALUES (@Id, @BallotId, @UserId, @InvitedEmail, @Permission, @CreatedAt, @CreatedBy, @AcceptedAt)",
                new
                {
                    Id = ballotPermission.Id.ToString(),
                    BallotId = ballotPermission.BallotId.ToString(),
                    UserId = ballotPermission.UserId?.ToString(),
                    InvitedEmail = ballotPermission.InvitedEmail,
                    Permission = ballotPermission.Permission.ToString(),
                    CreatedAt = ballotPermission.CreatedAt,
                    CreatedBy = ballotPermission.CreatedBy.ToString(),
                    AcceptedAt = ballotPermission.AcceptedAt
                }
            );

            return ballotPermission;
        }

        public async Task<List<BallotPermission>> GetBallotPermissionsAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var perms = await connection.QueryAsync<dynamic>(
                "SELECT * FROM ballot_permissions WHERE BallotId = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            var result = new List<BallotPermission>();
            foreach (var p in perms)
            {
                result.Add(new BallotPermission
                {
                    Id = Guid.Parse(p.Id),
                    BallotId = Guid.Parse(p.BallotId),
                    UserId = p.UserId != null ? Guid.Parse(p.UserId) : null,
                    InvitedEmail = p.InvitedEmail,
                    Permission = Enum.Parse<UserPermission>(p.Permission),
                    CreatedAt = DateTime.Parse(p.CreatedAt),
                    CreatedBy = Guid.Parse(p.CreatedBy),
                    AcceptedAt = p.AcceptedAt != null ? DateTime.Parse(p.AcceptedAt) : null
                });
            }

            return result;
        }

        public async Task UpdatePermissionAsync(Guid permissionId, UserPermission newPermission)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballot_permissions SET Permission = @Permission WHERE Id = @Id",
                new { Permission = newPermission.ToString(), Id = permissionId.ToString() }
            );
        }

        public async Task RevokePermissionAsync(Guid permissionId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "DELETE FROM ballot_permissions WHERE Id = @Id",
                new { Id = permissionId.ToString() }
            );
        }

        // Permission checking
        public async Task<UserPermission?> GetUserPermissionAsync(Guid ballotId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // 1. Check if user is owner
            var ballot = await GetBallotMetadataAsync(ballotId);
            if (ballot == null) return null;

            if (ballot.OwnerId == userId)
                return UserPermission.Admin;

            // 2. Check explicit permissions
            var permission = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Permission FROM ballot_permissions WHERE BallotId = @BallotId AND UserId = @UserId",
                new { BallotId = ballotId.ToString(), UserId = userId.ToString() }
            );

            if (permission != null)
            {
                return Enum.Parse<UserPermission>(permission.Permission);
            }

            // 3. Check organization membership (implicit access)
            if (ballot.OrganizationId.HasValue)
            {
                var orgs = await GetUserOrganizationsAsync(userId);
                if (orgs.Contains(ballot.OrganizationId.Value))
                {
                    return UserPermission.Voter; // Default org member permission
                }
            }

            return null; // No access
        }

        public async Task<bool> CanUserAccessBallotAsync(Guid ballotId, Guid userId)
        {
            var permission = await GetUserPermissionAsync(ballotId, userId);
            return permission.HasValue;
        }

        public async Task<bool> CanUserVoteAsync(Guid ballotId, Guid userId)
        {
            var permission = await GetUserPermissionAsync(ballotId, userId);
            return permission >= UserPermission.Voter;
        }

        public async Task<bool> CanUserEditAsync(Guid ballotId, Guid userId)
        {
            var permission = await GetUserPermissionAsync(ballotId, userId);
            return permission >= UserPermission.Editor;
        }

        public async Task<bool> CanUserAdminAsync(Guid ballotId, Guid userId)
        {
            var permission = await GetUserPermissionAsync(ballotId, userId);
            return permission == UserPermission.Admin;
        }

        // Lifecycle
        public async Task CloseBallotAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballots SET Status = @Status, IsOpen = 0 WHERE Id = @Id",
                new { Status = BallotStatus.Closed.ToString(), Id = ballotId.ToString() }
            );
        }

        public async Task OpenBallotAsync(Guid ballotId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballots SET Status = @Status, IsOpen = 1 WHERE Id = @Id",
                new { Status = BallotStatus.Open.ToString(), Id = ballotId.ToString() }
            );
        }

        public async Task<List<BallotMetadata>> GetBallotsToAutoCloseAsync(DateTime now)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var ballots = await connection.QueryAsync<BallotMetadata>(
                @"SELECT * FROM ballots
                  WHERE Status = @Status
                    AND IsOpen = 1
                    AND CloseDate IS NOT NULL
                    AND CloseDate <= @Now",
                new { Status = BallotStatus.Open.ToString(), Now = now }
            );

            return ballots.ToList();
        }

        // Helper Methods
        public async Task<List<Guid>> GetUserOrganizationsAsync(Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var orgIds = await connection.QueryAsync<string>(
                "SELECT OrganizationId FROM organization_members WHERE UserId = @UserId",
                new { UserId = userId.ToString() }
            );

            return orgIds.Select(id => Guid.Parse(id)).ToList();
        }

        // ========== ORGANIZATION/GROUP MANAGEMENT IMPLEMENTATIONS ==========

        public async Task<Organization?> GetOrganizationAsync(Guid orgId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var org = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM organizations WHERE Id = @Id",
                new { Id = orgId.ToString() }
            );

            if (org == null) return null;

            return new Organization
            {
                Id = Guid.Parse(org.Id),
                Name = org.Name,
                Description = org.Description,
                OwnerId = Guid.Parse(org.OwnerId),
                IsPublic = org.IsPublic == 1,
                CreatedAt = DateTime.Parse(org.CreatedAt)
            };
        }

        public async Task<List<OrganizationListItem>> GetOrganizationsForUserAsync(Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);
            var userIdString = userId.ToString();

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT
                    o.Id,
                    o.Name,
                    o.Description,
                    o.IsPublic,
                    o.OwnerId,
                    o.CreatedAt,
                    om.Role,
                    (SELECT COUNT(*) FROM organization_members WHERE OrganizationId = o.Id) as MemberCount,
                    (SELECT COUNT(*) FROM ballots WHERE OrganizationId = o.Id) as BallotCount
                FROM organizations o
                INNER JOIN organization_members om ON o.Id = om.OrganizationId
                WHERE om.UserId = @UserId
                ORDER BY o.Name",
                new { UserId = userIdString }
            );

            var items = new List<OrganizationListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationListItem
                {
                    Id = Guid.Parse(r.Id),
                    Name = r.Name,
                    Description = r.Description,
                    IsPublic = r.IsPublic == 1,
                    IsOwner = r.OwnerId == userIdString,
                    IsMember = true,
                    MyRole = r.Role,
                    MemberCount = Convert.ToInt32(r.MemberCount),
                    BallotCount = Convert.ToInt32(r.BallotCount),
                    CreatedAt = DateTime.Parse(r.CreatedAt)
                });
            }

            return items;
        }

        public async Task<List<OrganizationListItem>> GetPublicOrganizationsAsync(Guid? excludeUserId = null)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            string sql;
            object parameters;

            if (excludeUserId.HasValue)
            {
                // Get public orgs the user is NOT a member of
                sql = @"SELECT
                        o.Id,
                        o.Name,
                        o.Description,
                        o.IsPublic,
                        o.OwnerId,
                        o.CreatedAt,
                        (SELECT COUNT(*) FROM organization_members WHERE OrganizationId = o.Id) as MemberCount,
                        (SELECT COUNT(*) FROM ballots WHERE OrganizationId = o.Id) as BallotCount
                    FROM organizations o
                    WHERE o.IsPublic = 1
                    AND NOT EXISTS (SELECT 1 FROM organization_members om WHERE om.OrganizationId = o.Id AND om.UserId = @UserId)
                    ORDER BY o.Name";
                parameters = new { UserId = excludeUserId.Value.ToString() };
            }
            else
            {
                sql = @"SELECT
                        o.Id,
                        o.Name,
                        o.Description,
                        o.IsPublic,
                        o.OwnerId,
                        o.CreatedAt,
                        (SELECT COUNT(*) FROM organization_members WHERE OrganizationId = o.Id) as MemberCount,
                        (SELECT COUNT(*) FROM ballots WHERE OrganizationId = o.Id) as BallotCount
                    FROM organizations o
                    WHERE o.IsPublic = 1
                    ORDER BY o.Name";
                parameters = new { };
            }

            var results = await connection.QueryAsync<dynamic>(sql, parameters);

            var items = new List<OrganizationListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationListItem
                {
                    Id = Guid.Parse(r.Id),
                    Name = r.Name,
                    Description = r.Description,
                    IsPublic = true,
                    IsOwner = false,
                    IsMember = false,
                    MyRole = null,
                    MemberCount = Convert.ToInt32(r.MemberCount),
                    BallotCount = Convert.ToInt32(r.BallotCount),
                    CreatedAt = DateTime.Parse(r.CreatedAt)
                });
            }

            return items;
        }

        public async Task<Organization> CreateOrganizationAsync(string name, string? description, bool isPublic, Guid ownerId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                OwnerId = ownerId,
                IsPublic = isPublic,
                CreatedAt = DateTime.UtcNow
            };

            await connection.ExecuteAsync(
                @"INSERT INTO organizations (Id, Name, Description, OwnerId, IsPublic, CreatedAt)
                  VALUES (@Id, @Name, @Description, @OwnerId, @IsPublic, @CreatedAt)",
                new
                {
                    Id = org.Id.ToString(),
                    org.Name,
                    org.Description,
                    OwnerId = org.OwnerId.ToString(),
                    IsPublic = org.IsPublic ? 1 : 0,
                    org.CreatedAt
                }
            );

            // Add owner as a member with Owner role
            await connection.ExecuteAsync(
                @"INSERT INTO organization_members (Id, OrganizationId, UserId, Role, JoinedAt)
                  VALUES (@Id, @OrganizationId, @UserId, @Role, @JoinedAt)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    OrganizationId = org.Id.ToString(),
                    UserId = ownerId.ToString(),
                    Role = "Owner",
                    JoinedAt = DateTime.UtcNow
                }
            );

            return org;
        }

        public async Task UpdateOrganizationAsync(Organization org)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                @"UPDATE organizations
                  SET Name = @Name, Description = @Description, IsPublic = @IsPublic
                  WHERE Id = @Id",
                new
                {
                    Id = org.Id.ToString(),
                    org.Name,
                    org.Description,
                    IsPublic = org.IsPublic ? 1 : 0
                }
            );
        }

        public async Task DeleteOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Verify user is owner
            var org = await GetOrganizationAsync(orgId);
            if (org == null || org.OwnerId != userId)
            {
                throw new UnauthorizedAccessException("Only the organization owner can delete the organization");
            }

            // Delete organization members first
            await connection.ExecuteAsync(
                "DELETE FROM organization_members WHERE OrganizationId = @OrgId",
                new { OrgId = orgId.ToString() }
            );

            // Delete the organization
            await connection.ExecuteAsync(
                "DELETE FROM organizations WHERE Id = @Id",
                new { Id = orgId.ToString() }
            );
        }

        public async Task JoinOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Check if org is public
            var org = await GetOrganizationAsync(orgId);
            if (org == null)
            {
                throw new InvalidOperationException("Organization not found");
            }
            if (!org.IsPublic)
            {
                throw new UnauthorizedAccessException("Cannot join a private organization");
            }

            // Check if already a member
            var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id FROM organization_members WHERE OrganizationId = @OrgId AND UserId = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );

            if (existing != null)
            {
                throw new InvalidOperationException("Already a member of this organization");
            }

            await connection.ExecuteAsync(
                @"INSERT INTO organization_members (Id, OrganizationId, UserId, Role, JoinedAt)
                  VALUES (@Id, @OrganizationId, @UserId, @Role, @JoinedAt)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    OrganizationId = orgId.ToString(),
                    UserId = userId.ToString(),
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow
                }
            );
        }

        public async Task LeaveOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Check if user is the owner
            var org = await GetOrganizationAsync(orgId);
            if (org == null)
            {
                throw new InvalidOperationException("Organization not found");
            }
            if (org.OwnerId == userId)
            {
                throw new InvalidOperationException("Owner cannot leave the organization. Transfer ownership or delete the organization instead.");
            }

            await connection.ExecuteAsync(
                "DELETE FROM organization_members WHERE OrganizationId = @OrgId AND UserId = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );
        }

        public async Task<List<OrganizationMemberListItem>> GetOrganizationMembersAsync(Guid orgId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT
                    om.Id as MemberId,
                    om.UserId,
                    om.Role,
                    om.JoinedAt,
                    u.DisplayName,
                    u.Email
                FROM organization_members om
                INNER JOIN users u ON om.UserId = u.Id
                WHERE om.OrganizationId = @OrgId
                ORDER BY
                    CASE om.Role
                        WHEN 'Owner' THEN 1
                        WHEN 'Admin' THEN 2
                        ELSE 3
                    END,
                    u.DisplayName",
                new { OrgId = orgId.ToString() }
            );

            var items = new List<OrganizationMemberListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationMemberListItem
                {
                    MemberId = Guid.Parse(r.MemberId),
                    UserId = Guid.Parse(r.UserId),
                    DisplayName = r.DisplayName,
                    Email = r.Email,
                    Role = r.Role,
                    JoinedAt = DateTime.Parse(r.JoinedAt)
                });
            }

            return items;
        }

        public async Task<bool> IsUserMemberOfOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var count = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM organization_members WHERE OrganizationId = @OrgId AND UserId = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );

            return count > 0;
        }

        public async Task<string?> GetUserRoleInOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var role = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT Role FROM organization_members WHERE OrganizationId = @OrgId AND UserId = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );

            return role;
        }

        // ========== BALLOT MARKETPLACE IMPLEMENTATIONS ==========

        public async Task<List<PublicBallotListItem>> GetPublicBallotsAsync(string? searchTerm, Guid? organizationId, bool? closingSoon, Guid excludeUserId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);
            var userIdString = excludeUserId.ToString();
            var now = DateTime.UtcNow;
            var sevenDaysFromNow = now.AddDays(7);

            // Build dynamic SQL based on filters
            var sql = @"SELECT
                    b.Id,
                    b.Name,
                    b.Description,
                    b.OrganizationId,
                    b.CandidateCount,
                    b.VoteCount,
                    b.CloseDate,
                    b.CreatedAt,
                    o.Name as OrganizationName
                FROM ballots b
                LEFT JOIN organizations o ON b.OrganizationId = o.Id
                WHERE b.IsPublic = 1
                AND b.Status = 'Open'
                AND b.IsOpen = 1
                AND b.OwnerId != @UserId
                AND NOT EXISTS (
                    SELECT 1 FROM ballot_permissions bp
                    WHERE bp.BallotId = b.Id AND bp.UserId = @UserId
                )";

            var parameters = new DynamicParameters();
            parameters.Add("UserId", userIdString);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sql += " AND (b.Name LIKE @SearchTerm OR b.Description LIKE @SearchTerm)";
                parameters.Add("SearchTerm", $"%{searchTerm}%");
            }

            if (organizationId.HasValue)
            {
                sql += " AND b.OrganizationId = @OrganizationId";
                parameters.Add("OrganizationId", organizationId.Value.ToString());
            }

            if (closingSoon == true)
            {
                sql += " AND b.CloseDate IS NOT NULL AND b.CloseDate <= @SevenDaysFromNow AND b.CloseDate > @Now";
                parameters.Add("SevenDaysFromNow", sevenDaysFromNow);
                parameters.Add("Now", now);
            }

            sql += " ORDER BY b.CreatedAt DESC";

            var results = await connection.QueryAsync<dynamic>(sql, parameters);

            var items = new List<PublicBallotListItem>();
            foreach (var r in results)
            {
                object closeDateValue = r.CloseDate;
                var closeDate = closeDateValue == null || closeDateValue is DBNull ? (DateTime?)null : DateTime.Parse(closeDateValue.ToString()!);

                object orgIdValue = r.OrganizationId;
                Guid? organizationIdParsed = orgIdValue == null || orgIdValue is DBNull ? null : Guid.Parse(orgIdValue.ToString()!);

                object orgNameValue = r.OrganizationName;
                string? organizationName = orgNameValue == null || orgNameValue is DBNull ? null : orgNameValue.ToString();

                object descValue = r.Description;
                string? description = descValue == null || descValue is DBNull ? null : descValue.ToString();

                items.Add(new PublicBallotListItem
                {
                    Id = Guid.Parse(r.Id),
                    Name = r.Name,
                    Description = description,
                    OrganizationId = organizationIdParsed,
                    OrganizationName = organizationName,
                    CandidateCount = Convert.ToInt32(r.CandidateCount),
                    VoteCount = Convert.ToInt32(r.VoteCount),
                    CloseDate = closeDate,
                    IsClosingSoon = closeDate.HasValue && closeDate.Value <= sevenDaysFromNow && closeDate.Value > now,
                    CreatedAt = DateTime.Parse(r.CreatedAt)
                });
            }

            return items;
        }

        public async Task JoinBallotAsync(Guid ballotId, Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Verify ballot exists and is public
            var ballot = await GetBallotMetadataAsync(ballotId);
            if (ballot == null)
            {
                throw new InvalidOperationException("Ballot not found");
            }
            if (!ballot.IsPublic)
            {
                throw new UnauthorizedAccessException("Cannot join a non-public ballot");
            }
            if (ballot.Status != BallotStatus.Open)
            {
                throw new InvalidOperationException("Cannot join a closed ballot");
            }

            // Check if already has permission
            var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id FROM ballot_permissions WHERE BallotId = @BallotId AND UserId = @UserId",
                new { BallotId = ballotId.ToString(), UserId = userId.ToString() }
            );

            if (existing != null)
            {
                throw new InvalidOperationException("Already joined this ballot");
            }

            // Add user as Voter
            await connection.ExecuteAsync(
                @"INSERT INTO ballot_permissions (Id, BallotId, UserId, Permission, CreatedAt, CreatedBy, AcceptedAt)
                  VALUES (@Id, @BallotId, @UserId, @Permission, @CreatedAt, @CreatedBy, @AcceptedAt)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    BallotId = ballotId.ToString(),
                    UserId = userId.ToString(),
                    Permission = UserPermission.Voter.ToString(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId.ToString(), // Self-joined
                    AcceptedAt = DateTime.UtcNow
                }
            );
        }

        public async Task<List<OrganizationListItem>> GetOrganizationsWithPublicBallotsAsync()
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT DISTINCT
                    o.Id,
                    o.Name,
                    o.Description,
                    o.IsPublic,
                    o.CreatedAt,
                    (SELECT COUNT(*) FROM organization_members WHERE OrganizationId = o.Id) as MemberCount,
                    (SELECT COUNT(*) FROM ballots WHERE OrganizationId = o.Id AND IsPublic = 1 AND Status = 'Open') as BallotCount
                FROM organizations o
                INNER JOIN ballots b ON o.Id = b.OrganizationId
                WHERE b.IsPublic = 1 AND b.Status = 'Open' AND b.IsOpen = 1
                ORDER BY o.Name"
            );

            var items = new List<OrganizationListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationListItem
                {
                    Id = Guid.Parse(r.Id),
                    Name = r.Name,
                    Description = r.Description,
                    IsPublic = r.IsPublic == 1,
                    IsOwner = false,
                    IsMember = false,
                    MyRole = null,
                    MemberCount = Convert.ToInt32(r.MemberCount),
                    BallotCount = Convert.ToInt32(r.BallotCount),
                    CreatedAt = DateTime.Parse(r.CreatedAt)
                });
            }

            return items;
        }

        // ========== IMPORT METHODS IMPLEMENTATION ==========

        public async Task<Dictionary<string, int>> PreviewBallotsFromJsonAsync(string? jsonFilePath = null)
        {
            var preview = new Dictionary<string, int>();

            try
            {
                // Use provided path or default to Ballots.json
                string filePath = jsonFilePath ?? "Ballots.json";

                if (!File.Exists(filePath))
                {
                    return preview;
                }

                // Read and deserialize the JSON file
                string json = await File.ReadAllTextAsync(filePath);
                var allCandidates = JsonConvert.DeserializeObject<Candidate[]>(json);

                if (allCandidates == null || allCandidates.Length == 0)
                {
                    return preview;
                }

                // Group candidates by ElectionID and count them
                var groupedByElection = allCandidates
                    .Where(c => !string.IsNullOrEmpty(c.ElectionID))
                    .GroupBy(c => c.ElectionID)
                    .ToDictionary(g => g.Key!, g => g.Count());

                return groupedByElection;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PreviewBallotsFromJsonAsync] Error: {ex.Message}");
                return preview;
            }
        }

        public async Task<ImportResult> ImportBallotsFromJsonAsync(Guid userId, string? jsonFilePath = null, List<string>? electionIdsToImport = null)
        {
            var result = new ImportResult();

            try
            {
                // Use provided path or default to Ballots.json
                string filePath = jsonFilePath ?? "Ballots.json";

                if (!File.Exists(filePath))
                {
                    result.Errors.Add($"File not found: {filePath}");
                    return result;
                }

                // Read and deserialize the JSON file
                string json = await File.ReadAllTextAsync(filePath);
                var allCandidates = JsonConvert.DeserializeObject<Candidate[]>(json);

                if (allCandidates == null || allCandidates.Length == 0)
                {
                    result.Errors.Add("No candidates found in JSON file");
                    return result;
                }

                // Group candidates by ElectionID
                var groupedByElection = allCandidates
                    .Where(c => !string.IsNullOrEmpty(c.ElectionID))
                    .GroupBy(c => c.ElectionID)
                    .ToList();

                // Filter by selected elections if provided
                if (electionIdsToImport != null && electionIdsToImport.Any())
                {
                    groupedByElection = groupedByElection
                        .Where(g => electionIdsToImport.Contains(g.Key!))
                        .ToList();
                }

                using var connection = new SqliteConnection(_config.DatabaseName);

                // For each election, create a ballot and its candidates
                foreach (var group in groupedByElection)
                {
                    string electionId = group.Key!;
                    var candidates = group.ToList();

                    // Check if ballot already exists
                    var existingBallot = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT Id FROM ballots WHERE Name = @Name",
                        new { Name = electionId }
                    );

                    Guid ballotId;

                    if (existingBallot != null)
                    {
                        // Use existing ballot ID
                        ballotId = Guid.Parse(existingBallot.Id);
                        Console.WriteLine($"[ImportBallotsFromJsonAsync] Found existing ballot: {electionId} ({ballotId})");
                    }
                    else
                    {
                        // Create new ballot
                        ballotId = Guid.NewGuid();
                        var now = DateTime.UtcNow;

                        var ballot = new BallotMetadata
                        {
                            Id = ballotId,
                            Name = electionId,
                            Description = $"Imported from Ballots.json - {candidates.Count} options",
                            OwnerId = userId,
                            OrganizationId = null,
                            Status = BallotStatus.Open,
                            CreatedAt = now,
                            CloseDate = null,
                            IsOpen = true,
                            IsPublic = true,
                            CandidateCount = candidates.Count,
                            VoteCount = 0
                        };

                        await connection.ExecuteAsync(
                            @"INSERT INTO ballots (Id, Name, Description, OwnerId, OrganizationId, Status, CreatedAt, CloseDate, IsOpen, IsPublic, CandidateCount, VoteCount)
                              VALUES (@Id, @Name, @Description, @OwnerId, @OrganizationId, @Status, @CreatedAt, @CloseDate, @IsOpen, @IsPublic, @CandidateCount, @VoteCount)",
                            new
                            {
                                Id = ballot.Id.ToString(),
                                ballot.Name,
                                ballot.Description,
                                OwnerId = ballot.OwnerId.ToString(),
                                OrganizationId = ballot.OrganizationId?.ToString(),
                                Status = (int)ballot.Status,
                                CreatedAt = ballot.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                                CloseDate = ballot.CloseDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                                IsOpen = ballot.IsOpen ? 1 : 0,
                                IsPublic = ballot.IsPublic ? 1 : 0,
                                ballot.CandidateCount,
                                ballot.VoteCount
                            }
                        );

                        result.BallotsImported++;
                        Console.WriteLine($"[ImportBallotsFromJsonAsync] Created ballot: {electionId} ({ballotId})");
                    }

                    // Add candidates to the ballot
                    foreach (var candidate in candidates)
                    {
                        // Check if candidate already exists
                        var existingCandidate = await connection.QueryFirstOrDefaultAsync<dynamic>(
                            @"SELECT Id FROM ballot_candidates WHERE BallotId = @BallotId AND Name = @Name",
                            new { BallotId = ballotId.ToString(), Name = candidate.Name }
                        );

                        if (existingCandidate == null)
                        {
                            var ballotCandidate = new BallotCandidate
                            {
                                Id = Guid.NewGuid(),
                                BallotId = ballotId,
                                Name = candidate.Name ?? "Unnamed",
                                Description = candidate.Description,
                                ImageLink = candidate.Image_link,
                                CreatedAt = DateTime.UtcNow
                            };

                            await connection.ExecuteAsync(
                                @"INSERT INTO ballot_candidates (Id, BallotId, Name, Description, ImageLink, CreatedAt)
                                  VALUES (@Id, @BallotId, @Name, @Description, @ImageLink, @CreatedAt)",
                                new
                                {
                                    Id = ballotCandidate.Id.ToString(),
                                    BallotId = ballotCandidate.BallotId.ToString(),
                                    ballotCandidate.Name,
                                    ballotCandidate.Description,
                                    ballotCandidate.ImageLink,
                                    CreatedAt = ballotCandidate.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                                }
                            );

                            result.CandidatesImported++;
                        }
                    }

                    // Update candidate count for existing ballots
                    if (existingBallot != null)
                    {
                        var candidateCount = await connection.ExecuteScalarAsync<int>(
                            @"SELECT COUNT(*) FROM ballot_candidates WHERE BallotId = @BallotId",
                            new { BallotId = ballotId.ToString() }
                        );

                        await connection.ExecuteAsync(
                            @"UPDATE ballots SET CandidateCount = @CandidateCount WHERE Id = @Id",
                            new { CandidateCount = candidateCount, Id = ballotId.ToString() }
                        );
                    }
                }

                Console.WriteLine($"[ImportBallotsFromJsonAsync] Import complete: {result.BallotsImported} ballots, {result.CandidatesImported} candidates");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Import failed: {ex.Message}");
                Console.WriteLine($"[ImportBallotsFromJsonAsync] Error: {ex.Message}");
                Console.WriteLine($"[ImportBallotsFromJsonAsync] Stack trace: {ex.StackTrace}");
            }

            return result;
        }

        // ========== THEME PREFERENCE IMPLEMENTATIONS ==========

        public async Task UpdateUserThemeAsync(Guid userId, string theme)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE users SET PreferredTheme = @Theme WHERE Id = @UserId",
                new { Theme = theme, UserId = userId.ToString() }
            );
        }

        public async Task<string> GetUserThemeAsync(Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var theme = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT PreferredTheme FROM users WHERE Id = @UserId",
                new { UserId = userId.ToString() }
            );

            return theme ?? "cow"; // Default to cow theme if not set
        }
    }

    public class VoteCount
    {
        public String ElectionID { get; set; } = string.Empty;
        public int Voters { get; set; }
    }
}