using Dapper;
using Microsoft.Data.Sqlite;
using RangeVote2.Data;

namespace RangeVote2.Tests;

/// <summary>
/// Helper class for setting up SQLite database for testing.
/// Uses a file-based temp database for each test instance.
/// </summary>
public class TestHelper : IDisposable
{
    private readonly string _dbPath;
    public ApplicationConfig Config { get; }
    public RangeVoteRepository Repository { get; }

    public TestHelper()
    {
        // Create a unique temp file for this test instance
        _dbPath = Path.Combine(Path.GetTempPath(), $"rangevote_test_{Guid.NewGuid()}.db");
        var connectionString = $"DataSource={_dbPath}";

        // Register the GuidTypeHandler (only needs to be done once, but safe to call multiple times)
        try
        {
            SqlMapper.AddTypeHandler(new GuidTypeHandler());
        }
        catch
        {
            // Handler already registered, ignore
        }

        Config = new ApplicationConfig
        {
            DatabaseName = connectionString
        };

        // Create the tables
        SetupDatabase();

        Repository = new RangeVoteRepository(Config);
    }

    private void SetupDatabase()
    {
        using var connection = new SqliteConnection(Config.DatabaseName);
        connection.Open();

        // Create users table
        connection.Execute(@"
            CREATE TABLE users (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                Email VARCHAR(255) UNIQUE NOT NULL,
                PasswordHash VARCHAR(255) NOT NULL,
                DisplayName VARCHAR(100),
                OrganizationId VARCHAR(36),
                CreatedAt DATETIME NOT NULL,
                LastLoginAt DATETIME
            )");

        // Create organizations table
        connection.Execute(@"
            CREATE TABLE organizations (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                Name VARCHAR(255) NOT NULL,
                Description TEXT,
                OwnerId VARCHAR(36) NOT NULL,
                IsPublic BOOLEAN NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL
            )");

        // Create organization_members table
        connection.Execute(@"
            CREATE TABLE organization_members (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                OrganizationId VARCHAR(36) NOT NULL,
                UserId VARCHAR(36) NOT NULL,
                Role VARCHAR(50) NOT NULL DEFAULT 'Member',
                JoinedAt DATETIME NOT NULL
            )");

        // Create ballots table
        connection.Execute(@"
            CREATE TABLE ballots (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                Name VARCHAR(200) NOT NULL,
                Description TEXT,
                OwnerId VARCHAR(36) NOT NULL,
                OrganizationId VARCHAR(36),
                Status VARCHAR(20) NOT NULL DEFAULT 'Draft',
                CreatedAt DATETIME NOT NULL,
                OpenDate DATETIME,
                CloseDate DATETIME,
                IsOpen BOOLEAN NOT NULL DEFAULT 1,
                IsPublic BOOLEAN NOT NULL DEFAULT 0,
                CandidateCount INTEGER NOT NULL DEFAULT 0,
                VoteCount INTEGER NOT NULL DEFAULT 0
            )");

        // Create ballot_candidates table
        connection.Execute(@"
            CREATE TABLE ballot_candidates (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                BallotId VARCHAR(36) NOT NULL,
                Name VARCHAR(200) NOT NULL,
                Description TEXT,
                ImageLink VARCHAR(500),
                CreatedAt DATETIME NOT NULL
            )");

        // Create ballot_permissions table
        connection.Execute(@"
            CREATE TABLE ballot_permissions (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                BallotId VARCHAR(36) NOT NULL,
                UserId VARCHAR(36),
                InvitedEmail VARCHAR(255),
                Permission VARCHAR(20) NOT NULL,
                CreatedAt DATETIME NOT NULL,
                CreatedBy VARCHAR(36) NOT NULL,
                AcceptedAt DATETIME
            )");

        // Create ballot_share_links table
        connection.Execute(@"
            CREATE TABLE ballot_share_links (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                BallotId VARCHAR(36) NOT NULL,
                ShareToken VARCHAR(100) NOT NULL UNIQUE,
                Permission VARCHAR(20) NOT NULL,
                CreatedAt DATETIME NOT NULL,
                CreatedBy VARCHAR(36) NOT NULL,
                ExpiresAt DATETIME,
                IsActive BOOLEAN NOT NULL DEFAULT 1,
                UseCount INTEGER NOT NULL DEFAULT 0
            )");

        // Create votes table
        connection.Execute(@"
            CREATE TABLE votes (
                Id VARCHAR(36) PRIMARY KEY NOT NULL,
                BallotId VARCHAR(36) NOT NULL,
                CandidateId VARCHAR(36) NOT NULL,
                UserId VARCHAR(36) NOT NULL,
                Score INTEGER NOT NULL,
                CreatedAt DATETIME NOT NULL,
                UpdatedAt DATETIME NOT NULL,
                UNIQUE(BallotId, CandidateId, UserId)
            )");

        // Create candidate table (legacy)
        connection.Execute(@"
            CREATE TABLE candidate (
                GUID VARCHAR(50) NOT NULL,
                Name VARCHAR(100) NOT NULL,
                Description VARCHAR(1000) NULL,
                Score INTEGER NULL,
                ElectionID VARCHAR(50),
                OrganizationId VARCHAR(36),
                Image_link VARCHAR(500)
            )");
    }

    /// <summary>
    /// Creates a test user in the database
    /// </summary>
    public async Task<Guid> CreateTestUserAsync(string email = "test@example.com", string displayName = "Test User")
    {
        var userId = Guid.NewGuid();
        using var connection = new SqliteConnection(Config.DatabaseName);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            INSERT INTO users (Id, Email, PasswordHash, DisplayName, CreatedAt)
            VALUES (@Id, @Email, @PasswordHash, @DisplayName, @CreatedAt)",
            new
            {
                Id = userId.ToString(),
                Email = email,
                PasswordHash = "hash",
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            });

        return userId;
    }

    /// <summary>
    /// Creates a test organization directly in the database
    /// </summary>
    public async Task<Guid> CreateTestOrganizationAsync(Guid ownerId, string name = "Test Org", bool isPublic = false)
    {
        var orgId = Guid.NewGuid();
        using var connection = new SqliteConnection(Config.DatabaseName);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            INSERT INTO organizations (Id, Name, OwnerId, IsPublic, CreatedAt)
            VALUES (@Id, @Name, @OwnerId, @IsPublic, @CreatedAt)",
            new
            {
                Id = orgId.ToString(),
                Name = name,
                OwnerId = ownerId.ToString(),
                IsPublic = isPublic ? 1 : 0,
                CreatedAt = DateTime.UtcNow
            });

        // Add owner as member
        await connection.ExecuteAsync(@"
            INSERT INTO organization_members (Id, OrganizationId, UserId, Role, JoinedAt)
            VALUES (@Id, @OrganizationId, @UserId, @Role, @JoinedAt)",
            new
            {
                Id = Guid.NewGuid().ToString(),
                OrganizationId = orgId.ToString(),
                UserId = ownerId.ToString(),
                Role = "Owner",
                JoinedAt = DateTime.UtcNow
            });

        return orgId;
    }

    public void Dispose()
    {
        // Delete the temp database file
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
