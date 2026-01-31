using RangeVote2.Data;
using Xunit;

namespace RangeVote2.Tests;

public class OrganizationTests : IDisposable
{
    private readonly TestHelper _helper;

    public OrganizationTests()
    {
        _helper = new TestHelper();
    }

    public void Dispose()
    {
        _helper.Dispose();
    }

    [Fact]
    public async Task CreateOrganization_ShouldCreateOrgAndAddOwnerAsMember()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();

        // Act
        var org = await _helper.Repository.CreateOrganizationAsync(
            "Test Organization",
            "Test Description",
            isPublic: false,
            userId
        );

        // Assert
        Assert.NotEqual(Guid.Empty, org.Id);
        Assert.Equal("Test Organization", org.Name);
        Assert.Equal("Test Description", org.Description);
        Assert.Equal(userId, org.OwnerId);
        Assert.False(org.IsPublic);

        // Verify owner is a member
        var isMember = await _helper.Repository.IsUserMemberOfOrganizationAsync(org.Id, userId);
        Assert.True(isMember);

        var role = await _helper.Repository.GetUserRoleInOrganizationAsync(org.Id, userId);
        Assert.Equal("Owner", role);
    }

    [Fact]
    public async Task CreateOrganization_PublicOrg_ShouldSetIsPublicTrue()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();

        // Act
        var org = await _helper.Repository.CreateOrganizationAsync(
            "Public Org",
            null,
            isPublic: true,
            userId
        );

        // Assert
        Assert.True(org.IsPublic);
    }

    [Fact]
    public async Task GetOrganization_ShouldReturnOrg()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var createdOrg = await _helper.Repository.CreateOrganizationAsync(
            "Test Org",
            "Description",
            false,
            userId
        );

        // Act
        var org = await _helper.Repository.GetOrganizationAsync(createdOrg.Id);

        // Assert
        Assert.NotNull(org);
        Assert.Equal("Test Org", org.Name);
        Assert.Equal("Description", org.Description);
    }

    [Fact]
    public async Task GetOrganization_NonExistent_ShouldReturnNull()
    {
        // Act
        var org = await _helper.Repository.GetOrganizationAsync(Guid.NewGuid());

        // Assert
        Assert.Null(org);
    }

    [Fact]
    public async Task GetOrganizationsForUser_ShouldReturnUserOrgs()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        await _helper.Repository.CreateOrganizationAsync("Org 1", null, false, userId);
        await _helper.Repository.CreateOrganizationAsync("Org 2", null, true, userId);

        // Act
        var orgs = await _helper.Repository.GetOrganizationsForUserAsync(userId);

        // Assert
        Assert.Equal(2, orgs.Count);
        Assert.Contains(orgs, o => o.Name == "Org 1");
        Assert.Contains(orgs, o => o.Name == "Org 2");
        Assert.All(orgs, o => Assert.True(o.IsOwner));
        Assert.All(orgs, o => Assert.Equal("Owner", o.MyRole));
    }

    [Fact]
    public async Task GetPublicOrganizations_ShouldReturnOnlyPublicOrgs()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        await _helper.Repository.CreateOrganizationAsync("Private Org", null, false, userId);
        await _helper.Repository.CreateOrganizationAsync("Public Org", null, true, userId);

        var otherUserId = await _helper.CreateTestUserAsync("other@example.com");

        // Act
        var publicOrgs = await _helper.Repository.GetPublicOrganizationsAsync(otherUserId);

        // Assert
        Assert.Single(publicOrgs);
        Assert.Equal("Public Org", publicOrgs[0].Name);
        Assert.True(publicOrgs[0].IsPublic);
    }

    [Fact]
    public async Task GetPublicOrganizations_ShouldExcludeUserOrgs()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var publicOrg = await _helper.Repository.CreateOrganizationAsync("Public Org", null, true, userId);

        // Act - same user should not see their own org in public list
        var publicOrgs = await _helper.Repository.GetPublicOrganizationsAsync(userId);

        // Assert - user's own orgs are excluded
        Assert.Empty(publicOrgs);
    }

    [Fact]
    public async Task JoinOrganization_PublicOrg_ShouldAddUserAsMember()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicOrg = await _helper.Repository.CreateOrganizationAsync("Public Org", null, true, ownerId);

        var newUserId = await _helper.CreateTestUserAsync("newuser@example.com");

        // Act
        await _helper.Repository.JoinOrganizationAsync(publicOrg.Id, newUserId);

        // Assert
        var isMember = await _helper.Repository.IsUserMemberOfOrganizationAsync(publicOrg.Id, newUserId);
        Assert.True(isMember);

        var role = await _helper.Repository.GetUserRoleInOrganizationAsync(publicOrg.Id, newUserId);
        Assert.Equal("Member", role);
    }

    [Fact]
    public async Task JoinOrganization_PrivateOrg_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var privateOrg = await _helper.Repository.CreateOrganizationAsync("Private Org", null, false, ownerId);

        var newUserId = await _helper.CreateTestUserAsync("newuser@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _helper.Repository.JoinOrganizationAsync(privateOrg.Id, newUserId)
        );
    }

    [Fact]
    public async Task JoinOrganization_AlreadyMember_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicOrg = await _helper.Repository.CreateOrganizationAsync("Public Org", null, true, ownerId);

        var newUserId = await _helper.CreateTestUserAsync("newuser@example.com");
        await _helper.Repository.JoinOrganizationAsync(publicOrg.Id, newUserId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _helper.Repository.JoinOrganizationAsync(publicOrg.Id, newUserId)
        );
    }

    [Fact]
    public async Task LeaveOrganization_Member_ShouldRemoveUser()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var publicOrg = await _helper.Repository.CreateOrganizationAsync("Public Org", null, true, ownerId);

        var memberId = await _helper.CreateTestUserAsync("member@example.com");
        await _helper.Repository.JoinOrganizationAsync(publicOrg.Id, memberId);

        // Act
        await _helper.Repository.LeaveOrganizationAsync(publicOrg.Id, memberId);

        // Assert
        var isMember = await _helper.Repository.IsUserMemberOfOrganizationAsync(publicOrg.Id, memberId);
        Assert.False(isMember);
    }

    [Fact]
    public async Task LeaveOrganization_Owner_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var org = await _helper.Repository.CreateOrganizationAsync("Test Org", null, false, ownerId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _helper.Repository.LeaveOrganizationAsync(org.Id, ownerId)
        );
    }

    [Fact]
    public async Task UpdateOrganization_ShouldUpdateFields()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var org = await _helper.Repository.CreateOrganizationAsync("Original Name", "Original Desc", false, userId);

        // Act
        org.Name = "Updated Name";
        org.Description = "Updated Description";
        org.IsPublic = true;
        await _helper.Repository.UpdateOrganizationAsync(org);

        // Assert
        var updated = await _helper.Repository.GetOrganizationAsync(org.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Updated Description", updated.Description);
        Assert.True(updated.IsPublic);
    }

    [Fact]
    public async Task DeleteOrganization_Owner_ShouldDelete()
    {
        // Arrange
        var userId = await _helper.CreateTestUserAsync();
        var org = await _helper.Repository.CreateOrganizationAsync("Test Org", null, false, userId);

        // Act
        await _helper.Repository.DeleteOrganizationAsync(org.Id, userId);

        // Assert
        var deleted = await _helper.Repository.GetOrganizationAsync(org.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteOrganization_NonOwner_ShouldThrow()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync();
        var org = await _helper.Repository.CreateOrganizationAsync("Test Org", null, false, ownerId);

        var otherUserId = await _helper.CreateTestUserAsync("other@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _helper.Repository.DeleteOrganizationAsync(org.Id, otherUserId)
        );
    }

    [Fact]
    public async Task GetOrganizationMembers_ShouldReturnAllMembers()
    {
        // Arrange
        var ownerId = await _helper.CreateTestUserAsync("owner@example.com", "Owner User");
        var publicOrg = await _helper.Repository.CreateOrganizationAsync("Public Org", null, true, ownerId);

        var member1Id = await _helper.CreateTestUserAsync("member1@example.com", "Member One");
        var member2Id = await _helper.CreateTestUserAsync("member2@example.com", "Member Two");

        await _helper.Repository.JoinOrganizationAsync(publicOrg.Id, member1Id);
        await _helper.Repository.JoinOrganizationAsync(publicOrg.Id, member2Id);

        // Act
        var members = await _helper.Repository.GetOrganizationMembersAsync(publicOrg.Id);

        // Assert
        Assert.Equal(3, members.Count); // Owner + 2 members
        Assert.Contains(members, m => m.Role == "Owner");
        Assert.Equal(2, members.Count(m => m.Role == "Member"));
    }
}
