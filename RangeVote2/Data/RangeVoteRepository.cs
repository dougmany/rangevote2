using Dapper;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System.Globalization;

namespace RangeVote2.Data
{
    public interface IRangeVoteRepository
    {
        Task<Ballot> GetBallotAsync(Guid id, string electionId);
        Task<List<DBCandidate>> GetBallotsAsync(string electionId);
        Task PutBallotAsync(Ballot ballot, string electionId);
        Task<Ballot> GetResultAsync(string electionId);
        Task<List<VoteCount>> GetVotersAsync();
        List<string> GetBallots();

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
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<DBCandidate>(
                @"SELECT * FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = guid.ToString(), electionID = electionId }
            );

            var ballot = new Ballot
            {
                Id = guid,
                Candidates = candidates.Select(c => new Candidate
                {
                    Name = c.Name,
                    Score = c.Score,
                    ElectionID = c.ElectionID,
                    Description = c.Description
                }).ToArray()
            };
            if (ballot.Candidates.Length > 0)
            {
                return ballot;
            }

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
            using var connection = new SqliteConnection(_config.DatabaseName);
            await connection.ExecuteAsync(
                @"DELETE FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = ballot.Id.ToString(), electionID = electionId }
            );

            if (ballot.Candidates is not null)
            {
                var data = ballot.Candidates.Select(c => new DBCandidate
                {
                    Guid = ballot.Id.ToString(),
                    Name = c.Name,
                    Score = c.Score,
                    ElectionID = c.ElectionID,
                    Description = c.Description
                });

                String query = "INSERT INTO candidate (Guid, Name, Score, ElectionID, Description) Values (@Guid, @Name, @Score, @ElectionID, @Description)";
                await connection.ExecuteAsync(query, data);
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
            
            if (_config.ElectionIds is not null)
            {
                using var connection = new SqliteConnection(_config.DatabaseName);
                var query = @"SELECT ElectionID, COUNT(1) Voters FROM ( SELECT ElectionID, COUNT(GUID) FROM candidate GROUP BY GUID) GROUP BY ElectionID;";
                var results = await connection.QueryAsync<VoteCount>(query);
                voters = results.Where(r => _config.ElectionIds.Contains(r.ElectionID)).ToList();
            }
            return voters;
        }

        public List<string> GetBallots()
        {
            return _config.ElectionIds.ToList();
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
    }

    public class VoteCount
    {
        public String ElectionID { get; set; }
        public int Voters { get; set; }
    }
}