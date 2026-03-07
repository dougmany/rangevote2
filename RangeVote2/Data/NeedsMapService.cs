using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RangeVote2.Data
{
    public interface INeedsMapService
    {
        /// <summary>
        /// Notifies the Needs Map that a citizen has scored a ballot on range.vote,
        /// attaching their need-tags to the submission for disaggregated analytics.
        /// Returns bucket info on success, null if the API is unavailable or in stub mode.
        /// </summary>
        Task<NeedsMapBucketInfo[]?> SendNeedTagAsync(
            string needsMapCitizenId,
            string submissionId,
            string policyAreaSlug,
            string[] needTagIds,
            DateTime scoredAt);
    }

    public class NeedsMapService : INeedsMapService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NeedsMapService> _logger;

        public NeedsMapService(HttpClient httpClient, ILogger<NeedsMapService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<NeedsMapBucketInfo[]?> SendNeedTagAsync(
            string needsMapCitizenId,
            string submissionId,
            string policyAreaSlug,
            string[] needTagIds,
            DateTime scoredAt)
        {
            try
            {
                var payload = new
                {
                    citizenId = needsMapCitizenId,
                    rangeVoteSubmissionId = submissionId,
                    policyAreaSlug,
                    needTagIds,
                    scoredAt = scoredAt.ToString("O")
                };

                var response = await _httpClient.PostAsJsonAsync("/range-vote/need-tag", payload);

                if (response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
                {
                    // Phase 1 stub — expected, not an error
                    _logger.LogInformation("Needs Map integration is Phase 1 stub mode (501 returned). Will activate in Phase 2.");
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Needs Map API returned {StatusCode} for policyAreaSlug={Slug}", response.StatusCode, policyAreaSlug);
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<NeedsMapApiResponse>();
                return result?.Buckets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send need-tag to Needs Map for submission {SubmissionId}", submissionId);
                return null;
            }
        }

        private record NeedsMapApiResponse(
            [property: JsonPropertyName("acknowledged")] bool Acknowledged,
            [property: JsonPropertyName("buckets")] NeedsMapBucketInfo[] Buckets,
            [property: JsonPropertyName("summary")] string Summary);
    }
}
