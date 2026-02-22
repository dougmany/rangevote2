using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RangeVote2.Data
{
    public interface IVoteChainService
    {
        /// <summary>
        /// Records all votes for a ballot save operation and returns a receipt.
        /// This is the main entry point called after SaveVotesAsync.
        /// </summary>
        Task<VoteReceiptViewModel> RecordVotesAndGetReceiptAsync(
            Guid ballotId, Guid userId, Dictionary<Guid, int> candidateScores, string ballotName);

        /// <summary>
        /// Verifies a receipt hash matches the current chain state for a user.
        /// </summary>
        Task<bool> VerifyReceiptAsync(string receiptHash, Guid ballotId, Guid userId);

        /// <summary>
        /// Walks the entire chain from genesis, verifying every hash link.
        /// </summary>
        Task<ChainVerificationResult> VerifyChainAsync(Guid ballotId);

        /// <summary>
        /// Gets a lightweight integrity status for display on results page.
        /// </summary>
        Task<BallotIntegrityStatus> GetIntegrityStatusAsync(Guid ballotId);

        /// <summary>
        /// Gets paginated ledger entries (anonymized) for the audit browser.
        /// </summary>
        Task<List<VoteLedgerEntry>> GetLedgerEntriesAsync(Guid ballotId, int limit, int offset);

        /// <summary>
        /// Gets total entry count for pagination.
        /// </summary>
        Task<long> GetLedgerCountAsync(Guid ballotId);
    }

    public class VoteChainService : IVoteChainService
    {
        private readonly IRangeVoteRepository _repository;
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _ballotLocks = new();
        private static readonly string GenesisHash = new string('0', 64);

        public VoteChainService(IRangeVoteRepository repository)
        {
            _repository = repository;
        }

        public async Task<VoteReceiptViewModel> RecordVotesAndGetReceiptAsync(
            Guid ballotId, Guid userId, Dictionary<Guid, int> candidateScores, string ballotName)
        {
            var semaphore = _ballotLocks.GetOrAdd(ballotId, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var anonVoterId = ComputeAnonVoterId(ballotId, userId);
                var now = DateTime.UtcNow;

                // Get existing votes to determine CAST vs UPDATE
                var existingVotes = await _repository.GetUserVotesAsync(ballotId, userId);

                // Get current chain head
                var chainStatus = await _repository.GetChainStatusAsync(ballotId)
                    ?? new BallotChainStatus
                    {
                        BallotId = ballotId,
                        LatestSequenceNumber = 0,
                        LatestHash = GenesisHash,
                        TotalEntries = 0
                    };

                var currentHash = chainStatus.LatestHash;
                var currentSeq = chainStatus.LatestSequenceNumber;

                foreach (var kvp in candidateScores)
                {
                    var candidateId = kvp.Key;
                    var score = kvp.Value;
                    var existingVote = existingVotes.FirstOrDefault(v => v.CandidateId == candidateId);

                    var action = existingVote != null ? "UPDATE" : "CAST";
                    var previousScore = existingVote?.Score;

                    // Skip if score hasn't changed (no need to record a no-op)
                    if (existingVote != null && existingVote.Score == score)
                        continue;

                    currentSeq++;

                    var entry = new VoteLedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        BallotId = ballotId,
                        AnonVoterId = anonVoterId,
                        CandidateId = candidateId,
                        Score = score,
                        Action = action,
                        PreviousScore = previousScore,
                        SequenceNumber = currentSeq,
                        CreatedAt = now,
                        PreviousHash = currentHash
                    };

                    entry.EntryHash = ComputeEntryHash(entry);
                    currentHash = entry.EntryHash;

                    await _repository.InsertLedgerEntryAsync(entry);
                }

                // Update chain status
                chainStatus.LatestSequenceNumber = currentSeq;
                chainStatus.LatestHash = currentHash;
                chainStatus.TotalEntries = currentSeq; // sequence is 1-based monotonic
                await _repository.UpsertChainStatusAsync(chainStatus);

                // Generate receipt
                var receiptHash = await ComputeReceiptHashAsync(ballotId, anonVoterId);

                return new VoteReceiptViewModel
                {
                    ReceiptHash = receiptHash,
                    BallotName = ballotName,
                    IssuedAt = now,
                    CandidatesVotedOn = candidateScores.Count
                };
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<bool> VerifyReceiptAsync(string receiptHash, Guid ballotId, Guid userId)
        {
            var anonVoterId = ComputeAnonVoterId(ballotId, userId);
            var currentHash = await ComputeReceiptHashAsync(ballotId, anonVoterId);
            return string.Equals(receiptHash, currentHash, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ChainVerificationResult> VerifyChainAsync(Guid ballotId)
        {
            var sw = Stopwatch.StartNew();
            var result = new ChainVerificationResult
            {
                BallotId = ballotId,
                VerifiedAt = DateTime.UtcNow
            };

            var entries = await _repository.GetAllLedgerEntriesOrderedAsync(ballotId);
            result.TotalEntries = entries.Count;

            var expectedPreviousHash = GenesisHash;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                result.EntriesVerified++;

                // Verify previous hash linkage
                if (entry.PreviousHash != expectedPreviousHash)
                {
                    result.Errors.Add(new ChainVerificationError
                    {
                        SequenceNumber = entry.SequenceNumber,
                        ExpectedHash = expectedPreviousHash,
                        ActualHash = entry.PreviousHash,
                        Description = $"Chain broken at sequence {entry.SequenceNumber}: previous hash mismatch"
                    });
                }

                // Verify entry hash is correctly computed
                var recomputedHash = ComputeEntryHash(entry);
                if (entry.EntryHash != recomputedHash)
                {
                    result.Errors.Add(new ChainVerificationError
                    {
                        SequenceNumber = entry.SequenceNumber,
                        ExpectedHash = recomputedHash,
                        ActualHash = entry.EntryHash,
                        Description = $"Entry hash mismatch at sequence {entry.SequenceNumber}: data may have been tampered with"
                    });
                }

                // Verify sequence is monotonically increasing
                if (entry.SequenceNumber != i + 1)
                {
                    result.Errors.Add(new ChainVerificationError
                    {
                        SequenceNumber = entry.SequenceNumber,
                        ExpectedHash = "",
                        ActualHash = "",
                        Description = $"Sequence gap: expected {i + 1} but found {entry.SequenceNumber}"
                    });
                }

                expectedPreviousHash = entry.EntryHash;
            }

            // Verify chain head matches stored status
            var chainStatus = await _repository.GetChainStatusAsync(ballotId);
            if (chainStatus != null && entries.Count > 0)
            {
                var lastEntry = entries.Last();
                if (chainStatus.LatestHash != lastEntry.EntryHash)
                {
                    result.Errors.Add(new ChainVerificationError
                    {
                        SequenceNumber = lastEntry.SequenceNumber,
                        ExpectedHash = lastEntry.EntryHash,
                        ActualHash = chainStatus.LatestHash,
                        Description = "Chain status head hash does not match last ledger entry"
                    });
                }
            }

            result.IsValid = result.Errors.Count == 0;
            sw.Stop();
            result.VerificationDuration = sw.Elapsed;

            return result;
        }

        public async Task<BallotIntegrityStatus> GetIntegrityStatusAsync(Guid ballotId)
        {
            var chainStatus = await _repository.GetChainStatusAsync(ballotId);
            if (chainStatus == null)
            {
                return new BallotIntegrityStatus
                {
                    ChainIsValid = true,
                    TotalLedgerEntries = 0,
                    UniqueVotersInChain = 0
                };
            }

            var uniqueVoters = await _repository.GetUniqueVoterCountInLedgerAsync(ballotId);

            return new BallotIntegrityStatus
            {
                ChainIsValid = true, // Lightweight check; full verify is on-demand
                TotalLedgerEntries = chainStatus.TotalEntries,
                UniqueVotersInChain = uniqueVoters
            };
        }

        public async Task<List<VoteLedgerEntry>> GetLedgerEntriesAsync(Guid ballotId, int limit, int offset)
        {
            return await _repository.GetLedgerEntriesPagedAsync(ballotId, limit, offset);
        }

        public async Task<long> GetLedgerCountAsync(Guid ballotId)
        {
            return await _repository.GetLedgerEntryCountAsync(ballotId);
        }

        // ========== HASH COMPUTATION ==========

        /// <summary>
        /// Compute an anonymous voter ID from ballot + user. First 16 hex chars of SHA-256.
        /// Deterministic but not reversible back to the user.
        /// </summary>
        public static string ComputeAnonVoterId(Guid ballotId, Guid userId)
        {
            var payload = $"{ballotId}|{userId}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16];
        }

        /// <summary>
        /// Compute the SHA-256 hash for a ledger entry from its canonical fields.
        /// </summary>
        public static string ComputeEntryHash(VoteLedgerEntry entry)
        {
            var timestamp = entry.CreatedAt.ToUniversalTime().ToString("O");
            var payload = $"{entry.PreviousHash}|{entry.BallotId}|{entry.AnonVoterId}|{entry.CandidateId}|{entry.Score}|{entry.Action}|{entry.SequenceNumber}|{timestamp}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Compute a receipt hash from a voter's latest entries (sorted by candidateId).
        /// </summary>
        private async Task<string> ComputeReceiptHashAsync(Guid ballotId, string anonVoterId)
        {
            var latestEntries = await _repository.GetLatestEntriesForVoterAsync(ballotId, anonVoterId);

            if (latestEntries.Count == 0)
                return new string('0', 64);

            var sorted = latestEntries.OrderBy(e => e.CandidateId).ToList();
            var payload = string.Join("|", sorted.Select(e => e.EntryHash));
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
