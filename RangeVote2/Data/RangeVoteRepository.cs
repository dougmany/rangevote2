using Dapper;
using Npgsql;
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

            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<DBCandidate>(
                @"SELECT * FROM candidate WHERE guid = @guid AND electionid = @electionID;",
                new { guid = guid.ToString(), electionID = electionId }
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var ballots = await connection.QueryAsync<DBCandidate>(
                @"SELECT * FROM candidate WHERE electionid = @electionID;",
                new { electionID = electionId }
            );

            return ballots.ToList();
        }

        public async Task PutBallotAsync(Ballot ballot, string electionId)
        {
            Console.WriteLine($"[DEBUG PutBallotAsync] Saving ballot for GUID: {ballot.Id}, Election: {electionId}");
            Console.WriteLine($"[DEBUG PutBallotAsync] Number of candidates: {ballot.Candidates?.Length ?? 0}");

            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var beforeDelete = await connection.QueryAsync<dynamic>(
                @"SELECT guid, name, score FROM candidate WHERE guid = @guid AND electionid = @electionID;",
                new { guid = ballot.Id.ToString(), electionID = electionId }
            );
            Console.WriteLine($"[DEBUG PutBallotAsync] Records before delete: {beforeDelete.Count()}");

            await connection.ExecuteAsync(
                @"DELETE FROM candidate WHERE guid = @guid AND electionid = @electionID;",
                new { guid = ballot.Id.ToString(), electionID = electionId }
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

                string query = "INSERT INTO candidate (guid, name, score, electionid, description, image_link) VALUES (@Guid, @Name, @Score, @ElectionID, @Description, @Image_link)";
                await connection.ExecuteAsync(query, data);

                Console.WriteLine($"[DEBUG PutBallotAsync] Inserted {ballot.Candidates.Length} candidate records");
            }
        }

        public async Task<Ballot> GetResultAsync(string electionId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<Candidate>(
                    @"SELECT name, CAST(ROUND(SUM(score)/COUNT(DISTINCT guid)) AS INTEGER) AS Score
                        FROM candidate
                        WHERE electionid = @electionID
                        GROUP BY name
                        ORDER BY SUM(score) DESC;",
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
                    using var connection = new NpgsqlConnection(_config.DatabaseName);

                    var allCandidates = await connection.QueryAsync<dynamic>(
                        @"SELECT guid, electionid, name, score FROM candidate;"
                    );
                    Console.WriteLine($"[DEBUG GetVotersAsync] Total candidate records: {allCandidates.Count()}");

                    var query = @"SELECT electionid AS ElectionID, COUNT(1) AS Voters FROM ( SELECT electionid, COUNT(guid) FROM candidate GROUP BY guid, electionid) t GROUP BY electionid;";
                    var results = await connection.QueryAsync<VoteCount>(query);
                    Console.WriteLine($"[DEBUG GetVotersAsync] Query results count: {results.Count()}");

                    voters = results.Where(r => _config.ElectionIds.Contains(r.ElectionID)).ToList();
                    Console.WriteLine($"[DEBUG GetVotersAsync] Filtered voters count: {voters.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG GetVotersAsync] Exception: {ex.Message}");
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE id = @Id",
                new { Id = userId.ToString() }
            );

            if (user == null)
                return false;

            return true;
        }

        public async Task<List<string>> GetBallotsForOrganizationAsync(Guid? organizationId)
        {
            return GetBallots();
        }

        // ========== NEW BALLOT SYSTEM IMPLEMENTATIONS ==========

        // Ballot CRUD
        public async Task<BallotMetadata> CreateBallotAsync(CreateBallotModel model, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

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
                @"INSERT INTO ballots (id, name, description, ownerid, organizationid, status, createdat, closedate, isopen, ispublic, candidatecount, votecount)
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
                    ballot.IsPublic,
                    ballot.CandidateCount,
                    ballot.VoteCount
                }
            );

            // Create candidates
            foreach (var candidateModel in model.Candidates)
            {
                var candidateId = Guid.NewGuid();

                await connection.ExecuteAsync(
                    @"INSERT INTO ballot_candidates (id, ballotid, name, description, imagelink, createdat)
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM ballots WHERE id = @Id",
                new { Id = ballotId.ToString() }
            );

            if (result == null) return null;

            return MapBallotMetadata(result);
        }

        private BallotMetadata MapBallotMetadata(dynamic r)
        {
            return new BallotMetadata
            {
                Id = Guid.Parse((string)r.id),
                Name = (string)r.name,
                Description = (string?)r.description,
                OwnerId = Guid.Parse((string)r.ownerid),
                OrganizationId = r.organizationid != null ? Guid.Parse((string)r.organizationid) : null,
                Status = Enum.Parse<BallotStatus>((string)r.status),
                CreatedAt = (DateTime)r.createdat,
                OpenDate = (DateTime?)r.opendate,
                CloseDate = (DateTime?)r.closedate,
                IsOpen = (bool)r.isopen,
                IsPublic = (bool)r.ispublic,
                CandidateCount = (int)r.candidatecount,
                VoteCount = (int)r.votecount
            };
        }

        public async Task<List<BallotListItem>> GetBallotsForUserAsync(Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var orgs = await GetUserOrganizationsAsync(userId);
            var orgIdStrings = orgs.Select(o => o.ToString()).ToList();
            var userIdString = userId.ToString();

            string sql;
            object parameters;

            if (orgIdStrings.Count > 0)
            {
                sql = @"
                    SELECT DISTINCT
                        b.id,
                        b.name,
                        b.description,
                        b.status,
                        b.ownerid,
                        b.organizationid,
                        b.candidatecount,
                        b.votecount,
                        b.closedate,
                        b.createdat,
                        o.name as organizationname,
                        bp.permission as mypermission,
                        CASE WHEN b.ownerid = @UserId THEN TRUE ELSE FALSE END as isowner,
                        CASE WHEN EXISTS(SELECT 1 FROM votes WHERE ballotid = b.id AND userid = @UserId) THEN TRUE ELSE FALSE END as hasvoted
                    FROM ballots b
                    LEFT JOIN organizations o ON b.organizationid = o.id
                    LEFT JOIN ballot_permissions bp ON b.id = bp.ballotid AND bp.userid = @UserId
                    WHERE
                        b.ownerid = @UserId
                        OR bp.userid = @UserId
                        OR b.organizationid = ANY(@OrgIds)
                    ORDER BY b.createdat DESC
                ";
                parameters = new { UserId = userIdString, OrgIds = orgIdStrings.ToArray() };
            }
            else
            {
                sql = @"
                    SELECT DISTINCT
                        b.id,
                        b.name,
                        b.description,
                        b.status,
                        b.ownerid,
                        b.organizationid,
                        b.candidatecount,
                        b.votecount,
                        b.closedate,
                        b.createdat,
                        o.name as organizationname,
                        bp.permission as mypermission,
                        CASE WHEN b.ownerid = @UserId THEN TRUE ELSE FALSE END as isowner,
                        CASE WHEN EXISTS(SELECT 1 FROM votes WHERE ballotid = b.id AND userid = @UserId) THEN TRUE ELSE FALSE END as hasvoted
                    FROM ballots b
                    LEFT JOIN organizations o ON b.organizationid = o.id
                    LEFT JOIN ballot_permissions bp ON b.id = bp.ballotid AND bp.userid = @UserId
                    WHERE
                        b.ownerid = @UserId
                        OR bp.userid = @UserId
                    ORDER BY b.createdat DESC
                ";
                parameters = new { UserId = userIdString };
            }

            var results = await connection.QueryAsync<dynamic>(sql, parameters);

            var items = new List<BallotListItem>();
            foreach (var r in results)
            {
                var item = new BallotListItem
                {
                    Id = Guid.Parse((string)r.id),
                    Name = (string)r.name,
                    Description = (string?)r.description,
                    Status = Enum.Parse<BallotStatus>((string)r.status),
                    IsOwner = (bool)r.isowner,
                    IsOrganizationBallot = r.organizationid != null,
                    OrganizationName = (string?)r.organizationname,
                    CandidateCount = (int)r.candidatecount,
                    VoteCount = (int)r.votecount,
                    CloseDate = (DateTime?)r.closedate,
                    HasVoted = (bool)r.hasvoted
                };

                if (r.mypermission != null)
                {
                    item.MyPermission = Enum.Parse<UserPermission>((string)r.mypermission);
                }

                var permission = await GetUserPermissionAsync(item.Id, userId);
                item.CanEdit = permission >= UserPermission.Editor;
                item.CanVote = permission >= UserPermission.Voter && item.Status == BallotStatus.Open;

                items.Add(item);
            }

            return items;
        }

        public async Task UpdateBallotAsync(BallotMetadata ballot)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                @"UPDATE ballots
                  SET name = @Name, description = @Description, closedate = @CloseDate,
                      status = @Status, isopen = @IsOpen, ispublic = @IsPublic
                  WHERE id = @Id",
                new
                {
                    Id = ballot.Id.ToString(),
                    ballot.Name,
                    ballot.Description,
                    ballot.CloseDate,
                    Status = ballot.Status.ToString(),
                    ballot.IsOpen,
                    ballot.IsPublic
                }
            );
        }

        public async Task DeleteBallotAsync(Guid ballotId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var ballot = await GetBallotMetadataAsync(ballotId);
            if (ballot == null || ballot.OwnerId != userId)
            {
                throw new UnauthorizedAccessException("Only the ballot owner can delete the ballot");
            }

            await connection.ExecuteAsync(
                "DELETE FROM ballots WHERE id = @Id",
                new { Id = ballotId.ToString() }
            );
        }

        // Candidate management
        public async Task<List<BallotCandidate>> GetCandidatesAsync(Guid ballotId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<BallotCandidate>(
                "SELECT * FROM ballot_candidates WHERE ballotid = @BallotId ORDER BY RANDOM()",
                new { BallotId = ballotId.ToString() }
            );

            return candidates.ToList();
        }

        public async Task<BallotCandidate> AddCandidateAsync(Guid ballotId, CreateCandidateModel model)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

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
                @"INSERT INTO ballot_candidates (id, ballotid, name, description, imagelink, createdat)
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

            await connection.ExecuteAsync(
                "UPDATE ballots SET candidatecount = candidatecount + 1 WHERE id = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            return candidate;
        }

        public async Task UpdateCandidateAsync(BallotCandidate candidate)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                @"UPDATE ballot_candidates
                  SET name = @Name, description = @Description, imagelink = @ImageLink
                  WHERE id = @Id",
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var candidate = await connection.QueryFirstOrDefaultAsync<BallotCandidate>(
                "SELECT * FROM ballot_candidates WHERE id = @Id",
                new { Id = candidateId.ToString() }
            );

            if (candidate != null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM ballot_candidates WHERE id = @Id",
                    new { Id = candidateId.ToString() }
                );

                await connection.ExecuteAsync(
                    "UPDATE ballots SET candidatecount = candidatecount - 1 WHERE id = @BallotId",
                    new { BallotId = candidate.BallotId.ToString() }
                );
            }
        }

        // Voting
        public async Task<List<Vote>> GetUserVotesAsync(Guid ballotId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var votes = await connection.QueryAsync<Vote>(
                "SELECT * FROM votes WHERE ballotid = @BallotId AND userid = @UserId",
                new { BallotId = ballotId.ToString(), UserId = userId.ToString() }
            );

            return votes.ToList();
        }

        public async Task SaveVotesAsync(Guid ballotId, Guid userId, Dictionary<Guid, int> candidateScores)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);
            var now = DateTime.UtcNow;

            foreach (var kvp in candidateScores)
            {
                var candidateId = kvp.Key;
                var score = kvp.Value;

                var existing = await connection.QueryFirstOrDefaultAsync<Vote>(
                    "SELECT * FROM votes WHERE ballotid = @BallotId AND candidateid = @CandidateId AND userid = @UserId",
                    new { BallotId = ballotId.ToString(), CandidateId = candidateId.ToString(), UserId = userId.ToString() }
                );

                if (existing != null)
                {
                    await connection.ExecuteAsync(
                        "UPDATE votes SET score = @Score, updatedat = @UpdatedAt WHERE id = @Id",
                        new { Score = score, UpdatedAt = now, Id = existing.Id.ToString() }
                    );
                }
                else
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO votes (id, ballotid, candidateid, userid, score, createdat, updatedat)
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

            var voteCount = await connection.QueryFirstAsync<int>(
                "SELECT COUNT(DISTINCT userid) FROM votes WHERE ballotid = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            await connection.ExecuteAsync(
                "UPDATE ballots SET votecount = @VoteCount WHERE id = @BallotId",
                new { VoteCount = voteCount, BallotId = ballotId.ToString() }
            );
        }

        public async Task<Dictionary<Guid, double>> GetResultsAsync(Guid ballotId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT candidateid, AVG(CAST(score AS REAL)) as avgscore
                  FROM votes
                  WHERE ballotid = @BallotId
                  GROUP BY candidateid",
                new { BallotId = ballotId.ToString() }
            );

            var dict = new Dictionary<Guid, double>();
            foreach (var r in results)
            {
                dict[Guid.Parse((string)r.candidateid)] = (double)r.avgscore;
            }

            return dict;
        }

        // Share Links
        public async Task<BallotShareLink> CreateShareLinkAsync(Guid ballotId, ShareLinkPermission permission, Guid creatorId, DateTime? expiresAt = null)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

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
                @"INSERT INTO ballot_share_links (id, ballotid, sharetoken, permission, createdat, createdby, expiresat, isactive, usecount)
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
                    IsActive = shareLink.IsActive,
                    UseCount = shareLink.UseCount
                }
            );

            return shareLink;
        }

        public async Task<List<BallotShareLink>> GetShareLinksAsync(Guid ballotId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var links = await connection.QueryAsync<dynamic>(
                "SELECT * FROM ballot_share_links WHERE ballotid = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            var result = new List<BallotShareLink>();
            foreach (var link in links)
            {
                result.Add(new BallotShareLink
                {
                    Id = Guid.Parse((string)link.id),
                    BallotId = Guid.Parse((string)link.ballotid),
                    ShareToken = (string)link.sharetoken,
                    Permission = Enum.Parse<ShareLinkPermission>((string)link.permission),
                    CreatedAt = (DateTime)link.createdat,
                    CreatedBy = Guid.Parse((string)link.createdby),
                    ExpiresAt = (DateTime?)link.expiresat,
                    IsActive = (bool)link.isactive,
                    UseCount = (int)link.usecount
                });
            }

            return result;
        }

        public async Task<BallotShareLink?> GetShareLinkByTokenAsync(string token)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var link = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM ballot_share_links WHERE sharetoken = @Token",
                new { Token = token }
            );

            if (link == null) return null;

            return new BallotShareLink
            {
                Id = Guid.Parse((string)link.id),
                BallotId = Guid.Parse((string)link.ballotid),
                ShareToken = (string)link.sharetoken,
                Permission = Enum.Parse<ShareLinkPermission>((string)link.permission),
                CreatedAt = (DateTime)link.createdat,
                CreatedBy = Guid.Parse((string)link.createdby),
                ExpiresAt = (DateTime?)link.expiresat,
                IsActive = (bool)link.isactive,
                UseCount = (int)link.usecount
            };
        }

        public async Task DeactivateShareLinkAsync(Guid shareLinkId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballot_share_links SET isactive = FALSE WHERE id = @Id",
                new { Id = shareLinkId.ToString() }
            );
        }

        public async Task IncrementShareLinkUseCountAsync(Guid shareLinkId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballot_share_links SET usecount = usecount + 1 WHERE id = @Id",
                new { Id = shareLinkId.ToString() }
            );
        }

        // User Permissions
        public async Task<BallotPermission> InviteUserAsync(Guid ballotId, string email, UserPermission permission, Guid inviterId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE email = @Email",
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
                @"INSERT INTO ballot_permissions (id, ballotid, userid, invitedemail, permission, createdat, createdby, acceptedat)
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var perms = await connection.QueryAsync<dynamic>(
                "SELECT * FROM ballot_permissions WHERE ballotid = @BallotId",
                new { BallotId = ballotId.ToString() }
            );

            var result = new List<BallotPermission>();
            foreach (var p in perms)
            {
                result.Add(new BallotPermission
                {
                    Id = Guid.Parse((string)p.id),
                    BallotId = Guid.Parse((string)p.ballotid),
                    UserId = p.userid != null ? Guid.Parse((string)p.userid) : null,
                    InvitedEmail = (string?)p.invitedemail,
                    Permission = Enum.Parse<UserPermission>((string)p.permission),
                    CreatedAt = (DateTime)p.createdat,
                    CreatedBy = Guid.Parse((string)p.createdby),
                    AcceptedAt = (DateTime?)p.acceptedat
                });
            }

            return result;
        }

        public async Task UpdatePermissionAsync(Guid permissionId, UserPermission newPermission)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballot_permissions SET permission = @Permission WHERE id = @Id",
                new { Permission = newPermission.ToString(), Id = permissionId.ToString() }
            );
        }

        public async Task RevokePermissionAsync(Guid permissionId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "DELETE FROM ballot_permissions WHERE id = @Id",
                new { Id = permissionId.ToString() }
            );
        }

        // Permission checking
        public async Task<UserPermission?> GetUserPermissionAsync(Guid ballotId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var ballot = await GetBallotMetadataAsync(ballotId);
            if (ballot == null) return null;

            if (ballot.OwnerId == userId)
                return UserPermission.Admin;

            var permission = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT permission FROM ballot_permissions WHERE ballotid = @BallotId AND userid = @UserId",
                new { BallotId = ballotId.ToString(), UserId = userId.ToString() }
            );

            if (permission != null)
            {
                return Enum.Parse<UserPermission>((string)permission.permission);
            }

            if (ballot.OrganizationId.HasValue)
            {
                var orgs = await GetUserOrganizationsAsync(userId);
                if (orgs.Contains(ballot.OrganizationId.Value))
                {
                    return UserPermission.Voter;
                }
            }

            return null;
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballots SET status = @Status, isopen = FALSE WHERE id = @Id",
                new { Status = BallotStatus.Closed.ToString(), Id = ballotId.ToString() }
            );
        }

        public async Task OpenBallotAsync(Guid ballotId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE ballots SET status = @Status, isopen = TRUE WHERE id = @Id",
                new { Status = BallotStatus.Open.ToString(), Id = ballotId.ToString() }
            );
        }

        public async Task<List<BallotMetadata>> GetBallotsToAutoCloseAsync(DateTime now)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var ballots = await connection.QueryAsync<dynamic>(
                @"SELECT * FROM ballots
                  WHERE status = @Status
                    AND isopen = TRUE
                    AND closedate IS NOT NULL
                    AND closedate <= @Now",
                new { Status = BallotStatus.Open.ToString(), Now = now }
            );

            return ballots.Select(r => MapBallotMetadata(r)).Cast<BallotMetadata>().ToList();
        }

        // Helper Methods
        public async Task<List<Guid>> GetUserOrganizationsAsync(Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var orgIds = await connection.QueryAsync<string>(
                "SELECT organizationid FROM organization_members WHERE userid = @UserId",
                new { UserId = userId.ToString() }
            );

            return orgIds.Select(id => Guid.Parse(id)).ToList();
        }

        // ========== ORGANIZATION/GROUP MANAGEMENT IMPLEMENTATIONS ==========

        public async Task<Organization?> GetOrganizationAsync(Guid orgId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var org = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM organizations WHERE id = @Id",
                new { Id = orgId.ToString() }
            );

            if (org == null) return null;

            return new Organization
            {
                Id = Guid.Parse((string)org.id),
                Name = (string)org.name,
                Description = (string?)org.description,
                OwnerId = Guid.Parse((string)org.ownerid),
                IsPublic = (bool)org.ispublic,
                CreatedAt = (DateTime)org.createdat
            };
        }

        public async Task<List<OrganizationListItem>> GetOrganizationsForUserAsync(Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);
            var userIdString = userId.ToString();

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT
                    o.id,
                    o.name,
                    o.description,
                    o.ispublic,
                    o.ownerid,
                    o.createdat,
                    om.role,
                    (SELECT COUNT(*) FROM organization_members WHERE organizationid = o.id) as membercount,
                    (SELECT COUNT(*) FROM ballots WHERE organizationid = o.id) as ballotcount
                FROM organizations o
                INNER JOIN organization_members om ON o.id = om.organizationid
                WHERE om.userid = @UserId
                ORDER BY o.name",
                new { UserId = userIdString }
            );

            var items = new List<OrganizationListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationListItem
                {
                    Id = Guid.Parse((string)r.id),
                    Name = (string)r.name,
                    Description = (string?)r.description,
                    IsPublic = (bool)r.ispublic,
                    IsOwner = (string)r.ownerid == userIdString,
                    IsMember = true,
                    MyRole = (string)r.role,
                    MemberCount = (int)(long)r.membercount,
                    BallotCount = (int)(long)r.ballotcount,
                    CreatedAt = (DateTime)r.createdat
                });
            }

            return items;
        }

        public async Task<List<OrganizationListItem>> GetPublicOrganizationsAsync(Guid? excludeUserId = null)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            string sql;
            object parameters;

            if (excludeUserId.HasValue)
            {
                sql = @"SELECT
                        o.id,
                        o.name,
                        o.description,
                        o.ispublic,
                        o.ownerid,
                        o.createdat,
                        (SELECT COUNT(*) FROM organization_members WHERE organizationid = o.id) as membercount,
                        (SELECT COUNT(*) FROM ballots WHERE organizationid = o.id) as ballotcount
                    FROM organizations o
                    WHERE o.ispublic = TRUE
                    AND NOT EXISTS (SELECT 1 FROM organization_members om WHERE om.organizationid = o.id AND om.userid = @UserId)
                    ORDER BY o.name";
                parameters = new { UserId = excludeUserId.Value.ToString() };
            }
            else
            {
                sql = @"SELECT
                        o.id,
                        o.name,
                        o.description,
                        o.ispublic,
                        o.ownerid,
                        o.createdat,
                        (SELECT COUNT(*) FROM organization_members WHERE organizationid = o.id) as membercount,
                        (SELECT COUNT(*) FROM ballots WHERE organizationid = o.id) as ballotcount
                    FROM organizations o
                    WHERE o.ispublic = TRUE
                    ORDER BY o.name";
                parameters = new { };
            }

            var results = await connection.QueryAsync<dynamic>(sql, parameters);

            var items = new List<OrganizationListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationListItem
                {
                    Id = Guid.Parse((string)r.id),
                    Name = (string)r.name,
                    Description = (string?)r.description,
                    IsPublic = true,
                    IsOwner = false,
                    IsMember = false,
                    MyRole = null,
                    MemberCount = (int)(long)r.membercount,
                    BallotCount = (int)(long)r.ballotcount,
                    CreatedAt = (DateTime)r.createdat
                });
            }

            return items;
        }

        public async Task<Organization> CreateOrganizationAsync(string name, string? description, bool isPublic, Guid ownerId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

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
                @"INSERT INTO organizations (id, name, description, ownerid, ispublic, createdat)
                  VALUES (@Id, @Name, @Description, @OwnerId, @IsPublic, @CreatedAt)",
                new
                {
                    Id = org.Id.ToString(),
                    org.Name,
                    org.Description,
                    OwnerId = org.OwnerId.ToString(),
                    org.IsPublic,
                    org.CreatedAt
                }
            );

            await connection.ExecuteAsync(
                @"INSERT INTO organization_members (id, organizationid, userid, role, joinedat)
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                @"UPDATE organizations
                  SET name = @Name, description = @Description, ispublic = @IsPublic
                  WHERE id = @Id",
                new
                {
                    Id = org.Id.ToString(),
                    org.Name,
                    org.Description,
                    org.IsPublic
                }
            );
        }

        public async Task DeleteOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var org = await GetOrganizationAsync(orgId);
            if (org == null || org.OwnerId != userId)
            {
                throw new UnauthorizedAccessException("Only the organization owner can delete the organization");
            }

            await connection.ExecuteAsync(
                "DELETE FROM organization_members WHERE organizationid = @OrgId",
                new { OrgId = orgId.ToString() }
            );

            await connection.ExecuteAsync(
                "DELETE FROM organizations WHERE id = @Id",
                new { Id = orgId.ToString() }
            );
        }

        public async Task JoinOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var org = await GetOrganizationAsync(orgId);
            if (org == null)
                throw new InvalidOperationException("Organization not found");
            if (!org.IsPublic)
                throw new UnauthorizedAccessException("Cannot join a private organization");

            var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id FROM organization_members WHERE organizationid = @OrgId AND userid = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );

            if (existing != null)
                throw new InvalidOperationException("Already a member of this organization");

            await connection.ExecuteAsync(
                @"INSERT INTO organization_members (id, organizationid, userid, role, joinedat)
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var org = await GetOrganizationAsync(orgId);
            if (org == null)
                throw new InvalidOperationException("Organization not found");
            if (org.OwnerId == userId)
                throw new InvalidOperationException("Owner cannot leave the organization. Transfer ownership or delete the organization instead.");

            await connection.ExecuteAsync(
                "DELETE FROM organization_members WHERE organizationid = @OrgId AND userid = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );
        }

        public async Task<List<OrganizationMemberListItem>> GetOrganizationMembersAsync(Guid orgId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT
                    om.id as memberid,
                    om.userid,
                    om.role,
                    om.joinedat,
                    u.displayname,
                    u.email
                FROM organization_members om
                INNER JOIN users u ON om.userid = u.id
                WHERE om.organizationid = @OrgId
                ORDER BY
                    CASE om.role
                        WHEN 'Owner' THEN 1
                        WHEN 'Admin' THEN 2
                        ELSE 3
                    END,
                    u.displayname",
                new { OrgId = orgId.ToString() }
            );

            var items = new List<OrganizationMemberListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationMemberListItem
                {
                    MemberId = Guid.Parse((string)r.memberid),
                    UserId = Guid.Parse((string)r.userid),
                    DisplayName = (string)r.displayname,
                    Email = (string)r.email,
                    Role = (string)r.role,
                    JoinedAt = (DateTime)r.joinedat
                });
            }

            return items;
        }

        public async Task<bool> IsUserMemberOfOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var count = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM organization_members WHERE organizationid = @OrgId AND userid = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );

            return count > 0;
        }

        public async Task<string?> GetUserRoleInOrganizationAsync(Guid orgId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var role = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT role FROM organization_members WHERE organizationid = @OrgId AND userid = @UserId",
                new { OrgId = orgId.ToString(), UserId = userId.ToString() }
            );

            return role;
        }

        // ========== BALLOT MARKETPLACE IMPLEMENTATIONS ==========

        public async Task<List<PublicBallotListItem>> GetPublicBallotsAsync(string? searchTerm, Guid? organizationId, bool? closingSoon, Guid excludeUserId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);
            var userIdString = excludeUserId.ToString();
            var now = DateTime.UtcNow;
            var sevenDaysFromNow = now.AddDays(7);

            var sql = @"SELECT
                    b.id,
                    b.name,
                    b.description,
                    b.organizationid,
                    b.candidatecount,
                    b.votecount,
                    b.closedate,
                    b.createdat,
                    o.name as organizationname
                FROM ballots b
                LEFT JOIN organizations o ON b.organizationid = o.id
                WHERE b.ispublic = TRUE
                AND b.status = 'Open'
                AND b.isopen = TRUE
                AND b.ownerid != @UserId
                AND NOT EXISTS (
                    SELECT 1 FROM ballot_permissions bp
                    WHERE bp.ballotid = b.id AND bp.userid = @UserId
                )";

            var parameters = new DynamicParameters();
            parameters.Add("UserId", userIdString);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sql += " AND (b.name ILIKE @SearchTerm OR b.description ILIKE @SearchTerm)";
                parameters.Add("SearchTerm", $"%{searchTerm}%");
            }

            if (organizationId.HasValue)
            {
                sql += " AND b.organizationid = @OrganizationId";
                parameters.Add("OrganizationId", organizationId.Value.ToString());
            }

            if (closingSoon == true)
            {
                sql += " AND b.closedate IS NOT NULL AND b.closedate <= @SevenDaysFromNow AND b.closedate > @Now";
                parameters.Add("SevenDaysFromNow", sevenDaysFromNow);
                parameters.Add("Now", now);
            }

            sql += " ORDER BY b.createdat DESC";

            var results = await connection.QueryAsync<dynamic>(sql, parameters);

            var items = new List<PublicBallotListItem>();
            foreach (var r in results)
            {
                DateTime? closeDate = (DateTime?)r.closedate;
                Guid? orgIdParsed = r.organizationid != null ? Guid.Parse((string)r.organizationid) : null;
                string? orgName = (string?)r.organizationname;
                string? description = (string?)r.description;

                items.Add(new PublicBallotListItem
                {
                    Id = Guid.Parse((string)r.id),
                    Name = (string)r.name,
                    Description = description,
                    OrganizationId = orgIdParsed,
                    OrganizationName = orgName,
                    CandidateCount = (int)r.candidatecount,
                    VoteCount = (int)r.votecount,
                    CloseDate = closeDate,
                    IsClosingSoon = closeDate.HasValue && closeDate.Value <= sevenDaysFromNow && closeDate.Value > now,
                    CreatedAt = (DateTime)r.createdat
                });
            }

            return items;
        }

        public async Task JoinBallotAsync(Guid ballotId, Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var ballot = await GetBallotMetadataAsync(ballotId);
            if (ballot == null)
                throw new InvalidOperationException("Ballot not found");
            if (!ballot.IsPublic)
                throw new UnauthorizedAccessException("Cannot join a non-public ballot");
            if (ballot.Status != BallotStatus.Open)
                throw new InvalidOperationException("Cannot join a closed ballot");

            var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id FROM ballot_permissions WHERE ballotid = @BallotId AND userid = @UserId",
                new { BallotId = ballotId.ToString(), UserId = userId.ToString() }
            );

            if (existing != null)
                throw new InvalidOperationException("Already joined this ballot");

            await connection.ExecuteAsync(
                @"INSERT INTO ballot_permissions (id, ballotid, userid, permission, createdat, createdby, acceptedat)
                  VALUES (@Id, @BallotId, @UserId, @Permission, @CreatedAt, @CreatedBy, @AcceptedAt)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    BallotId = ballotId.ToString(),
                    UserId = userId.ToString(),
                    Permission = UserPermission.Voter.ToString(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId.ToString(),
                    AcceptedAt = DateTime.UtcNow
                }
            );
        }

        public async Task<List<OrganizationListItem>> GetOrganizationsWithPublicBallotsAsync()
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var results = await connection.QueryAsync<dynamic>(
                @"SELECT DISTINCT
                    o.id,
                    o.name,
                    o.description,
                    o.ispublic,
                    o.createdat,
                    (SELECT COUNT(*) FROM organization_members WHERE organizationid = o.id) as membercount,
                    (SELECT COUNT(*) FROM ballots WHERE organizationid = o.id AND ispublic = TRUE AND status = 'Open') as ballotcount
                FROM organizations o
                INNER JOIN ballots b ON o.id = b.organizationid
                WHERE b.ispublic = TRUE AND b.status = 'Open' AND b.isopen = TRUE
                ORDER BY o.name"
            );

            var items = new List<OrganizationListItem>();
            foreach (var r in results)
            {
                items.Add(new OrganizationListItem
                {
                    Id = Guid.Parse((string)r.id),
                    Name = (string)r.name,
                    Description = (string?)r.description,
                    IsPublic = (bool)r.ispublic,
                    IsOwner = false,
                    IsMember = false,
                    MyRole = null,
                    MemberCount = (int)(long)r.membercount,
                    BallotCount = (int)(long)r.ballotcount,
                    CreatedAt = (DateTime)r.createdat
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
                string filePath = jsonFilePath ?? "Ballots.json";

                if (!File.Exists(filePath))
                    return preview;

                string json = await File.ReadAllTextAsync(filePath);
                var allCandidates = JsonConvert.DeserializeObject<Candidate[]>(json);

                if (allCandidates == null || allCandidates.Length == 0)
                    return preview;

                return allCandidates
                    .Where(c => !string.IsNullOrEmpty(c.ElectionID))
                    .GroupBy(c => c.ElectionID)
                    .ToDictionary(g => g.Key!, g => g.Count());
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
                string filePath = jsonFilePath ?? "Ballots.json";

                if (!File.Exists(filePath))
                {
                    result.Errors.Add($"File not found: {filePath}");
                    return result;
                }

                string json = await File.ReadAllTextAsync(filePath);
                var allCandidates = JsonConvert.DeserializeObject<Candidate[]>(json);

                if (allCandidates == null || allCandidates.Length == 0)
                {
                    result.Errors.Add("No candidates found in JSON file");
                    return result;
                }

                var groupedByElection = allCandidates
                    .Where(c => !string.IsNullOrEmpty(c.ElectionID))
                    .GroupBy(c => c.ElectionID)
                    .ToList();

                if (electionIdsToImport != null && electionIdsToImport.Any())
                {
                    groupedByElection = groupedByElection
                        .Where(g => electionIdsToImport.Contains(g.Key!))
                        .ToList();
                }

                using var connection = new NpgsqlConnection(_config.DatabaseName);

                foreach (var group in groupedByElection)
                {
                    string electionId = group.Key!;
                    var candidates = group.ToList();

                    var existingBallot = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT id FROM ballots WHERE name = @Name",
                        new { Name = electionId }
                    );

                    Guid ballotId;

                    if (existingBallot != null)
                    {
                        ballotId = Guid.Parse((string)existingBallot.id);
                        Console.WriteLine($"[ImportBallotsFromJsonAsync] Found existing ballot: {electionId} ({ballotId})");
                    }
                    else
                    {
                        ballotId = Guid.NewGuid();
                        var now = DateTime.UtcNow;

                        await connection.ExecuteAsync(
                            @"INSERT INTO ballots (id, name, description, ownerid, organizationid, status, createdat, closedate, isopen, ispublic, candidatecount, votecount)
                              VALUES (@Id, @Name, @Description, @OwnerId, @OrganizationId, @Status, @CreatedAt, @CloseDate, @IsOpen, @IsPublic, @CandidateCount, @VoteCount)",
                            new
                            {
                                Id = ballotId.ToString(),
                                Name = electionId,
                                Description = $"Imported from Ballots.json - {candidates.Count} options",
                                OwnerId = userId.ToString(),
                                OrganizationId = (string?)null,
                                Status = BallotStatus.Open.ToString(),
                                CreatedAt = now,
                                CloseDate = (DateTime?)null,
                                IsOpen = true,
                                IsPublic = true,
                                CandidateCount = candidates.Count,
                                VoteCount = 0
                            }
                        );

                        result.BallotsImported++;
                        Console.WriteLine($"[ImportBallotsFromJsonAsync] Created ballot: {electionId} ({ballotId})");
                    }

                    foreach (var candidate in candidates)
                    {
                        var existingCandidate = await connection.QueryFirstOrDefaultAsync<dynamic>(
                            @"SELECT id FROM ballot_candidates WHERE ballotid = @BallotId AND name = @Name",
                            new { BallotId = ballotId.ToString(), Name = candidate.Name }
                        );

                        if (existingCandidate == null)
                        {
                            await connection.ExecuteAsync(
                                @"INSERT INTO ballot_candidates (id, ballotid, name, description, imagelink, createdat)
                                  VALUES (@Id, @BallotId, @Name, @Description, @ImageLink, @CreatedAt)",
                                new
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    BallotId = ballotId.ToString(),
                                    Name = candidate.Name ?? "Unnamed",
                                    Description = candidate.Description,
                                    ImageLink = candidate.Image_link,
                                    CreatedAt = DateTime.UtcNow
                                }
                            );

                            result.CandidatesImported++;
                        }
                    }

                    if (existingBallot != null)
                    {
                        var candidateCount = await connection.ExecuteScalarAsync<int>(
                            @"SELECT COUNT(*) FROM ballot_candidates WHERE ballotid = @BallotId",
                            new { BallotId = ballotId.ToString() }
                        );

                        await connection.ExecuteAsync(
                            @"UPDATE ballots SET candidatecount = @CandidateCount WHERE id = @Id",
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
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE users SET preferredtheme = @Theme WHERE id = @UserId",
                new { Theme = theme, UserId = userId.ToString() }
            );
        }

        public async Task<string> GetUserThemeAsync(Guid userId)
        {
            using var connection = new NpgsqlConnection(_config.DatabaseName);

            var theme = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT preferredtheme FROM users WHERE id = @UserId",
                new { UserId = userId.ToString() }
            );

            return theme ?? "plain";
        }
    }

    public class VoteCount
    {
        public String ElectionID { get; set; } = string.Empty;
        public int Voters { get; set; }
    }
}
