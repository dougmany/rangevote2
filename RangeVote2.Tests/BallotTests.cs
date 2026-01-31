using RangeVote2.Data;
using Xunit;

namespace RangeVote2.Tests;

public class BallotTests : IDisposable
{
    private readonly TestHelper _helper;

    public BallotTests()
    {
        _helper = new TestHelper();
    }

    public void Dispose()
    {
        _helper.Dispose();
    }

    [Fact]
    public async Task CreateBallot_ShouldCreateBallotWithCandidates()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Description = "Test Description",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate A", Description = "Description A" },
                new() { Name = "Candidate B", Description = "Description B" },
                new() { Name = "Candidate C" }
            }
        };

        // Act
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);

        // Assert
        Assert.NotEqual(Guid.Empty, ballot.Id);
        Assert.Equal("Test Ballot", ballot.Name);
        Assert.Equal("Test Description", ballot.Description);
        Assert.Equal(userId, ballot.OwnerId);
        Assert.Equal(BallotStatus.Open, ballot.Status);
        Assert.Equal(3, ballot.CandidateCount);
        Assert.Equal(0, ballot.VoteCount);

        var candidates = await _helper.Repository.GetCandidatesAsync(ballot.Id);
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public async Task CreateBallot_WithOrganization_ShouldSetOrgId()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var org = await _helper.Repository.CreateOrganizationAsync("Test Org", null, false, userId);

        var model = new CreateBallotModel
        {
            Name = "Org Ballot",
            OrganizationId = org.Id,
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };

        // Act
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);

        // Assert
        Assert.Equal(org.Id, ballot.OrganizationId);
    }

    [Fact]
    public async Task GetBallotMetadata_ShouldReturnBallot()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var created = await _helper.Repository.CreateBallotAsync(model, userId);

        // Act
        var ballot = await _helper.Repository.GetBallotMetadataAsync(created.Id);

        // Assert
        Assert.NotNull(ballot);
        Assert.Equal("Test Ballot", ballot.Name);
    }

    [Fact]
    public async Task GetBallotMetadata_NonExistent_ShouldReturnNull()
    {
        // Act
        var ballot = await _helper.Repository.GetBallotMetadataAsync(Guid.NewGuid());

        // Assert
        Assert.Null(ballot);
    }

    [Fact]
    public async Task CloseBallot_ShouldSetStatusAndIsOpen()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);

        // Act
        await _helper.Repository.CloseBallotAsync(ballot.Id);

        // Assert
        var closed = await _helper.Repository.GetBallotMetadataAsync(ballot.Id);
        Assert.NotNull(closed);
        Assert.Equal(BallotStatus.Closed, closed.Status);
        Assert.False(closed.IsOpen);
    }

    [Fact]
    public async Task OpenBallot_ShouldSetStatusAndIsOpen()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);
        await _helper.Repository.CloseBallotAsync(ballot.Id);

        // Act
        await _helper.Repository.OpenBallotAsync(ballot.Id);

        // Assert
        var opened = await _helper.Repository.GetBallotMetadataAsync(ballot.Id);
        Assert.NotNull(opened);
        Assert.Equal(BallotStatus.Open, opened.Status);
        Assert.True(opened.IsOpen);
    }

    [Fact]
    public async Task DeleteBallot_Owner_ShouldDelete()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);

        // Act
        await _helper.Repository.DeleteBallotAsync(ballot.Id, userId);

        // Assert
        var deleted = await _helper.Repository.GetBallotMetadataAsync(ballot.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteBallot_NonOwner_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, ownerId);

        var otherUserId = await _helper.CreateTestUserAsync("other@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _helper.Repository.DeleteBallotAsync(ballot.Id, otherUserId)
        );
    }

    [Fact]
    public async Task SaveVotes_ShouldSaveAndRetrieveVotes()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);
        var candidates = await _helper.Repository.GetCandidatesAsync(ballot.Id);

        var scores = new Dictionary<Guid, int>
        {
            { candidates[0].Id, 90 },
            { candidates[1].Id, 45 }
        };

        // Act
        await _helper.Repository.SaveVotesAsync(ballot.Id, userId, scores);

        // Assert
        var votes = await _helper.Repository.GetUserVotesAsync(ballot.Id, userId);
        Assert.Equal(2, votes.Count);

        var results = await _helper.Repository.GetResultsAsync(ballot.Id);
        Assert.Equal(90, results[candidates[0].Id]);
        Assert.Equal(45, results[candidates[1].Id]);
    }

    [Fact]
    public async Task SaveVotes_UpdateExisting_ShouldUpdate()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);
        var candidates = await _helper.Repository.GetCandidatesAsync(ballot.Id);

        // Initial vote
        await _helper.Repository.SaveVotesAsync(ballot.Id, userId,
            new Dictionary<Guid, int> { { candidates[0].Id, 50 } });

        // Act - update vote
        await _helper.Repository.SaveVotesAsync(ballot.Id, userId,
            new Dictionary<Guid, int> { { candidates[0].Id, 80 } });

        // Assert
        var results = await _helper.Repository.GetResultsAsync(ballot.Id);
        Assert.Equal(80, results[candidates[0].Id]);
    }

    [Fact]
    public async Task GetBallotsForUser_ShouldReturnOwnedBallots()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model1 = new CreateBallotModel
        {
            Name = "Ballot 1",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var model2 = new CreateBallotModel
        {
            Name = "Ballot 2",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };

        await _helper.Repository.CreateBallotAsync(model1, userId);
        await _helper.Repository.CreateBallotAsync(model2, userId);

        // Act
        var ballots = await _helper.Repository.GetBallotsForUserAsync(userId);

        // Assert
        Assert.Equal(2, ballots.Count);
        Assert.All(ballots, b => Assert.True(b.IsOwner));
    }

    [Fact]
    public async Task CanUserVoteAsync_Owner_ShouldReturnTrue()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, userId);

        // Act
        var canVote = await _helper.Repository.CanUserVoteAsync(ballot.Id, userId);

        // Assert
        Assert.True(canVote);
    }

    [Fact]
    public async Task CanUserVoteAsync_Unauthorized_ShouldReturnFalse()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Private Ballot",
            IsPublic = false,
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, ownerId);

        var otherUserId = await _helper.CreateTestUserAsync("other@example.com");

        // Act
        var canVote = await _helper.Repository.CanUserVoteAsync(ballot.Id, otherUserId);

        // Assert
        Assert.False(canVote);
    }

    [Fact]
    public async Task InviteUser_ShouldAddPermission()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var model = new CreateBallotModel
        {
            Name = "Test Ballot",
            Candidates = new List<CreateCandidateModel>
            {
                new() { Name = "Candidate 1" },
                new() { Name = "Candidate 2" }
            }
        };
        var ballot = await _helper.Repository.CreateBallotAsync(model, ownerId);

        var inviteeId = await _helper.CreateTestUserAsync("invitee@example.com");

        // Act
        await _helper.Repository.InviteUserAsync(ballot.Id, "invitee@example.com", UserPermission.Voter, ownerId);

        // Assert
        var canVote = await _helper.Repository.CanUserVoteAsync(ballot.Id, inviteeId);
        Assert.True(canVote);
    }
}
