using Dapper;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace RangeVote2.Data
{
    public interface IRangeVoteRepository
    {
        Task<Ballot> GetBallotAsync(Guid id);
        Task PutBallotAsync(Ballot ballot);
        Task<Ballot> GetResultAsync();
        Task<Int32> GetVotersAsync();
        String GetElectionId();
        List<String> GetElectionIds();
    }

    public class RangeVoteRepository : IRangeVoteRepository
    {
        private readonly ApplicationConfig _config;

        public RangeVoteRepository(ApplicationConfig config)
        {
            _config = config;
        }


        public async Task<Ballot> GetBallotAsync(Guid guid)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<Candidate>(
                "SELECT * FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = guid, electionID = _config.ElectionId }
            );

            var ballot = new Ballot
            {
                Id = guid,
                Candidates = candidates.ToArray()
            };
            if (ballot.Candidates.Length > 0)
            {
                return ballot;
            }

            return new Ballot { Id = guid, Candidates = DefaultCandidates };
        }

        public async Task PutBallotAsync(Ballot ballot)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);
            await connection.ExecuteAsync(
                @"DELETE FROM candidate WHERE Guid = @guid AND ElectionID = @electionID;",
                new { guid = ballot.Id, electionID = _config.ElectionId }
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

        public async Task<Ballot> GetResultAsync()
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var candidates = await connection.QueryAsync<Candidate>(
                    @"SELECT Name, CAST(ROUND(SUM(Score)/COUNT(DISTINCT Guid))AS SIGNED) AS Score 
                        FROM candidate
                        WHERE ElectionID = @electionID
                        GROUP BY Name 
                        ORDER BY SUM(Score) DESC;",
                    new { electionID = _config.ElectionId }
                );
            var ballot = new Ballot
            {
                Candidates = candidates.ToArray()
            };
            if (ballot.Candidates.Length > 0)
            {
                return ballot;
            }

            return new Ballot { Candidates = DefaultCandidates };
        }

        public async Task<int> GetVotersAsync()
        {
            var query = "SELECT COUNT(DISTINCT Guid) FROM candidate WHERE ElectionID = @electionID;";

            using var connection = new SqliteConnection(_config.DatabaseName);
            var voters = await connection.QueryAsync<Int32>(query, new { electionID = _config.ElectionId });
            return voters.FirstOrDefault();
        }

        public String GetElectionId()
        {
            var electionId = _config.ElectionId;

            return electionId ?? "Range Vote";
        }
        
        public List<String> GetElectionIds() 
        {
            return DefaultCandidates.Select(dc => dc.ElectionID ?? "").Where(e => e != "").Distinct().ToList();
        }

        public Candidate[] DefaultCandidates
        {
            get
            {
                string json = File.ReadAllText("ballots.json");
                var allBallots = JsonConvert.DeserializeObject<Candidate[]>(json);
                if (allBallots is not null)
                {
                    return allBallots.Where(b => b.ElectionID == _config.ElectionId).ToArray();
                }

                return new Candidate[] { new Candidate() };
                
            }
        }
    }
}