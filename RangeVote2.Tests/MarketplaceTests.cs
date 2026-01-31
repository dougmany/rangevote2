using RangeVote2.Data;
using Xunit;

namespace RangeVote2.Tests;

public class MarketplaceTests : IDisposable
{
    private readonly TestHelper _helper;

    public MarketplaceTests()
    {
        _helper = new TestHelper();
    }

    public void Dispose()
    {
        _helper.Dispose();
    }

    private async Task<BallotMetadata> CreateTestBallotAsync(Guid ownerId, string name, bool isPublic, DateTime? closeDate = null)
    {
        var model = new CreateBallotModel
        {
            Name = name,
            Description = $"Description for {name}",
            IsPublic = isPublic,
            CloseDate = closeDate,
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };

        return await _helper.Repository.CreateBallotAsync(model, ownerId);
    }

    [Fact]
    public async Task CreateBallot_WithIsPublic_ShouldSetIsPublicTrue()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();

        // Act
        var ballot = await CreateTestBallotAsync(userId, "Public Ballot", isPublic: true);

        // Assert
        Assert.True(ballot.IsPublic);

        var retrieved = await _helper.Repository.GetBallotMetadataAsync(ballot.Id);
        Assert.NotNull(retrieved);
        Assert.True(retrieved.IsPublic);
    }

    [Fact]
    public async Task CreateBallot_WithIsPublicFalse_ShouldSetIsPublicFalse()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();

        // Act
        var ballot = await CreateTestBallotAsync(userId, "Private Ballot", isPublic: false);

        // Assert
        Assert.False(ballot.IsPublic);
    }

    [Fact]
    public async Task GetPublicBallots_ShouldReturnOnlyPublicBallots()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        await CreateTestBallotAsync(ownerId, "Private Ballot", isPublic: false);
        await CreateTestBallotAsync(ownerId, "Public Ballot 1", isPublic: true);
        await CreateTestBallotAsync(ownerId, "Public Ballot 2", isPublic: true);

        var searchUserId = await _helper.CreateTestUserAsync("searcher@example.com");

        // Act
        var publicBallots = await _helper.Repository.GetPublicBallotsAsync(null, null, null, searchUserId);

        // Assert
        Assert.Equal(2, publicBallots.Count);
        Assert.All(publicBallots, b => Assert.Contains("Public Ballot", b.Name));
    }

    [Fact]
    public async Task GetPublicBallots_ShouldExcludeOwnBallots()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        await CreateTestBallotAsync(userId, "My Public Ballot", isPublic: true);

        // Act - same user searches
        var publicBallots = await _helper.Repository.GetPublicBallotsAsync(null, null, null, userId);

        // Assert - should not see own ballots
        Assert.Empty(publicBallots);
    }

    [Fact]
    public async Task GetPublicBallots_WithSearchTerm_ShouldFilterByName()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        await CreateTestBallotAsync(ownerId, "Election for President", isPublic: true);
        await CreateTestBallotAsync(ownerId, "Best Pizza Vote", isPublic: true);
        await CreateTestBallotAsync(ownerId, "Team Lunch Decision", isPublic: true);

        var searchUserId = await _helper.CreateTestUserAsync("searcher@example.com");

        // Act
        var results = await _helper.Repository.GetPublicBallotsAsync("Pizza", null, null, searchUserId);

        // Assert
        Assert.Single(results);
        Assert.Equal("Best Pizza Vote", results[0].Name);
    }

    [Fact]
    public async Task GetPublicBallots_WithClosingSoonFilter_ShouldReturnOnlyClosingSoon()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        await CreateTestBallotAsync(ownerId, "Closing Soon Ballot", isPublic: true,
            closeDate: DateTime.UtcNow.AddDays(3));
        await CreateTestBallotAsync(ownerId, "Later Ballot", isPublic: true,
            closeDate: DateTime.UtcNow.AddDays(14));
        await CreateTestBallotAsync(ownerId, "No Close Date Ballot", isPublic: true,
            closeDate: null);

        var searchUserId = await _helper.CreateTestUserAsync("searcher@example.com");

        // Act
        var results = await _helper.Repository.GetPublicBallotsAsync(null, null, true, searchUserId);

        // Assert
        Assert.Single(results);
        Assert.Equal("Closing Soon Ballot", results[0].Name);
        Assert.True(results[0].IsClosingSoon);
    }

    [Fact]
    public async Task JoinBallot_PublicBallot_ShouldAddPermission()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicBallot = await CreateTestBallotAsync(ownerId, "Public Ballot", isPublic: true);

        var joinerId = await _helper.CreateTestUserAsync("joiner@example.com");

        // Act
        await _helper.Repository.JoinBallotAsync(publicBallot.Id, joinerId);

        // Assert
        var canVote = await _helper.Repository.CanUserVoteAsync(publicBallot.Id, joinerId);
        Assert.True(canVote);

        var permission = await _helper.Repository.GetUserPermissionAsync(publicBallot.Id, joinerId);
        Assert.Equal(UserPermission.Voter, permission);
    }

    [Fact]
    public async Task JoinBallot_PrivateBallot_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var privateBallot = await CreateTestBallotAsync(ownerId, "Private Ballot", isPublic: false);

        var joinerId = await _helper.CreateTestUserAsync("joiner@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _helper.Repository.JoinBallotAsync(privateBallot.Id, joinerId)
        );
    }

    [Fact]
    public async Task JoinBallot_AlreadyJoined_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicBallot = await CreateTestBallotAsync(ownerId, "Public Ballot", isPublic: true);

        var joinerId = await _helper.CreateTestUserAsync("joiner@example.com");
        await _helper.Repository.JoinBallotAsync(publicBallot.Id, joinerId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _helper.Repository.JoinBallotAsync(publicBallot.Id, joinerId)
        );
    }

    [Fact]
    public async Task JoinBallot_ClosedBallot_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicBallot = await CreateTestBallotAsync(ownerId, "Public Ballot", isPublic: true);

        // Close the ballot
        await _helper.Repository.CloseBallotAsync(publicBallot.Id);

        var joinerId = await _helper.CreateTestUserAsync("joiner@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _helper.Repository.JoinBallotAsync(publicBallot.Id, joinerId)
        );
    }

    [Fact]
    public async Task GetPublicBallots_AfterJoining_ShouldNotShowJoinedBallot()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicBallot = await CreateTestBallotAsync(ownerId, "Public Ballot", isPublic: true);

        var joinerId = await _helper.CreateTestUserAsync("joiner@example.com");

        // Before joining
        var beforeJoin = await _helper.Repository.GetPublicBallotsAsync(null, null, null, joinerId);
        Assert.Single(beforeJoin);

        // Act - join the ballot
        await _helper.Repository.JoinBallotAsync(publicBallot.Id, joinerId);

        // Assert - should no longer appear in public list
        var afterJoin = await _helper.Repository.GetPublicBallotsAsync(null, null, null, joinerId);
        Assert.Empty(afterJoin);
    }

    [Fact]
    public async Task UpdateBallot_ToggleIsPublic_ShouldUpdate()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var ballot = await CreateTestBallotAsync(userId, "Test Ballot", isPublic: false);

        // Act
        ballot.IsPublic = true;
        await _helper.Repository.UpdateBallotAsync(ballot);

        // Assert
        var updated = await _helper.Repository.GetBallotMetadataAsync(ballot.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsPublic);
    }

    [Fact]
    public async Task GetOrganizationsWithPublicBallots_ShouldReturnOrgsWithPublicBallots()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var orgWithPublicBallot = await _helper.Repository.CreateOrganizationAsync(
            "Org With Public Ballot", null, false, ownerId);
        var orgWithPrivateBallot = await _helper.Repository.CreateOrganizationAsync(
            "Org With Private Ballot", null, false, ownerId);

        // Create public ballot for first org
        var publicModel = new CreateBallotModel
        {
            Name = "Public Org Ballot",
            OrganizationId = orgWithPublicBallot.Id,
            IsPublic = true,
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        await _helper.Repository.CreateBallotAsync(publicModel, ownerId);

        // Create private ballot for second org
        var privateModel = new CreateBallotModel
        {
            Name = "Private Org Ballot",
            OrganizationId = orgWithPrivateBallot.Id,
            IsPublic = false,
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        await _helper.Repository.CreateBallotAsync(privateModel, ownerId);

        // Act
        var orgsWithPublicBallots = await _helper.Repository.GetOrganizationsWithPublicBallotsAsync();

        // Assert
        Assert.Single(orgsWithPublicBallots);
        Assert.Equal("Org With Public Ballot", orgsWithPublicBallots[0].Name);
    }
}
