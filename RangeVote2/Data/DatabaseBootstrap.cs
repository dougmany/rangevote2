
using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Linq;

namespace RangeVote2.Data
{

  public interface IDatabaseBootstrap
  {
    void Setup();
  }

  public class DatabaseBootstrap : IDatabaseBootstrap
  {
    private readonly ApplicationConfig _databaseConfig;

    public DatabaseBootstrap(ApplicationConfig databaseConfig)
    {
      _databaseConfig = databaseConfig;
    }

    public void Setup()
    {
      using(var connection = new SqliteConnection(_databaseConfig.DatabaseName))
      {
        // Create candidate table if it doesn't exist
        var table = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'candidate';");
        var tableName = table.FirstOrDefault();
        if (string.IsNullOrEmpty(tableName) || tableName != "candidate")
        {
          connection.Execute(
            @"Create Table candidate (
              GUID VARCHAR(50) NOT NULL,
              Name VARCHAR(100) NOT NULL,
              Description VARCHAR(1000) NULL,
              Score Int32 NULL,
              ElectionID VARCHAR(50));"
          );
        }

        // Create users table
        var usersTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'users';");
        if (!usersTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE users (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              Email VARCHAR(255) UNIQUE NOT NULL,
              PasswordHash VARCHAR(255) NOT NULL,
              DisplayName VARCHAR(100),
              OrganizationId VARCHAR(36),
              CreatedAt DATETIME NOT NULL,
              LastLoginAt DATETIME);"
          );
        }

        // Create organizations table
        var orgsTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'organizations';");
        if (!orgsTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE organizations (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              Name VARCHAR(255) NOT NULL,
              OwnerId VARCHAR(36) NOT NULL,
              CreatedAt DATETIME NOT NULL);"
          );
        }

        // Create organization_members table
        var membersTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'organization_members';");
        if (!membersTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE organization_members (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              OrganizationId VARCHAR(36) NOT NULL,
              UserId VARCHAR(36) NOT NULL,
              Role VARCHAR(50) NOT NULL DEFAULT 'Member',
              JoinedAt DATETIME NOT NULL);"
          );
        }

        // Add IsPublic and Description columns to organizations if they don't exist
        var orgColumns = connection.Query<dynamic>("PRAGMA table_info(organizations);");
        var hasIsPublic = orgColumns.Any(c => c.name == "IsPublic");
        if (!hasIsPublic)
        {
          connection.Execute("ALTER TABLE organizations ADD COLUMN IsPublic BOOLEAN NOT NULL DEFAULT 0;");
        }
        var hasOrgDescription = orgColumns.Any(c => c.name == "Description");
        if (!hasOrgDescription)
        {
          connection.Execute("ALTER TABLE organizations ADD COLUMN Description TEXT;");
        }

        // Add PreferredTheme column to users if it doesn't exist
        var userColumns = connection.Query<dynamic>("PRAGMA table_info(users);");
        var hasPreferredTheme = userColumns.Any(c => c.name == "PreferredTheme");
        if (!hasPreferredTheme)
        {
          connection.Execute("ALTER TABLE users ADD COLUMN PreferredTheme VARCHAR(20) NOT NULL DEFAULT 'cow';");
        }

        // Update candidate table to add OrganizationId if it doesn't exist
        var candidateColumns = connection.Query<dynamic>("PRAGMA table_info(candidate);");
        var hasOrgId = candidateColumns.Any(c => c.name == "OrganizationId");
        if (!hasOrgId)
        {
          connection.Execute("ALTER TABLE candidate ADD COLUMN OrganizationId VARCHAR(36);");
        }

        var hasImageLink = candidateColumns.Any(c => c.name == "Image_link");
        if (!hasImageLink)
        {
          connection.Execute("ALTER TABLE candidate ADD COLUMN Image_link VARCHAR(500);");
        }

        // ========== NEW BALLOT SYSTEM TABLES ==========

        // Create ballots table
        var ballotsTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'ballots';");
        if (!ballotsTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE ballots (
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
              CandidateCount INTEGER NOT NULL DEFAULT 0,
              VoteCount INTEGER NOT NULL DEFAULT 0,
              FOREIGN KEY (OwnerId) REFERENCES users(Id),
              FOREIGN KEY (OrganizationId) REFERENCES organizations(Id)
            );"
          );

          connection.Execute("CREATE INDEX idx_ballots_owner ON ballots(OwnerId);");
          connection.Execute("CREATE INDEX idx_ballots_org ON ballots(OrganizationId);");
          connection.Execute("CREATE INDEX idx_ballots_status ON ballots(Status);");
        }

        // Add IsPublic column to ballots if it doesn't exist
        var ballotColumns = connection.Query<dynamic>("PRAGMA table_info(ballots);");
        var hasBallotIsPublic = ballotColumns.Any(c => c.name == "IsPublic");
        if (!hasBallotIsPublic)
        {
          connection.Execute("ALTER TABLE ballots ADD COLUMN IsPublic BOOLEAN NOT NULL DEFAULT 0;");
          connection.Execute("CREATE INDEX idx_ballots_public ON ballots(IsPublic);");
        }

        // Create ballot_candidates table
        var ballotCandidatesTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'ballot_candidates';");
        if (!ballotCandidatesTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE ballot_candidates (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              BallotId VARCHAR(36) NOT NULL,
              Name VARCHAR(200) NOT NULL,
              Description TEXT,
              ImageLink VARCHAR(500),
              CreatedAt DATETIME NOT NULL,
              FOREIGN KEY (BallotId) REFERENCES ballots(Id) ON DELETE CASCADE
            );"
          );

          connection.Execute("CREATE INDEX idx_ballot_candidates_ballot ON ballot_candidates(BallotId);");
        }

        // Create ballot_share_links table
        var shareLinksTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'ballot_share_links';");
        if (!shareLinksTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE ballot_share_links (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              BallotId VARCHAR(36) NOT NULL,
              ShareToken VARCHAR(100) NOT NULL UNIQUE,
              Permission VARCHAR(20) NOT NULL,
              CreatedAt DATETIME NOT NULL,
              CreatedBy VARCHAR(36) NOT NULL,
              ExpiresAt DATETIME,
              IsActive BOOLEAN NOT NULL DEFAULT 1,
              UseCount INTEGER NOT NULL DEFAULT 0,
              FOREIGN KEY (BallotId) REFERENCES ballots(Id) ON DELETE CASCADE,
              FOREIGN KEY (CreatedBy) REFERENCES users(Id)
            );"
          );

          connection.Execute("CREATE INDEX idx_share_links_ballot ON ballot_share_links(BallotId);");
          connection.Execute("CREATE INDEX idx_share_links_token ON ballot_share_links(ShareToken);");
        }

        // Create ballot_permissions table
        var permissionsTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'ballot_permissions';");
        if (!permissionsTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE ballot_permissions (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              BallotId VARCHAR(36) NOT NULL,
              UserId VARCHAR(36),
              InvitedEmail VARCHAR(255),
              Permission VARCHAR(20) NOT NULL,
              CreatedAt DATETIME NOT NULL,
              CreatedBy VARCHAR(36) NOT NULL,
              AcceptedAt DATETIME,
              FOREIGN KEY (BallotId) REFERENCES ballots(Id) ON DELETE CASCADE,
              FOREIGN KEY (UserId) REFERENCES users(Id),
              FOREIGN KEY (CreatedBy) REFERENCES users(Id)
            );"
          );

          connection.Execute("CREATE INDEX idx_ballot_permissions_ballot ON ballot_permissions(BallotId);");
          connection.Execute("CREATE INDEX idx_ballot_permissions_user ON ballot_permissions(UserId);");
          connection.Execute("CREATE INDEX idx_ballot_permissions_email ON ballot_permissions(InvitedEmail);");
        }

        // Create votes table
        var votesTable = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'votes';");
        if (!votesTable.Any())
        {
          connection.Execute(
            @"CREATE TABLE votes (
              Id VARCHAR(36) PRIMARY KEY NOT NULL,
              BallotId VARCHAR(36) NOT NULL,
              CandidateId VARCHAR(36) NOT NULL,
              UserId VARCHAR(36) NOT NULL,
              Score INTEGER NOT NULL,
              CreatedAt DATETIME NOT NULL,
              UpdatedAt DATETIME NOT NULL,
              FOREIGN KEY (BallotId) REFERENCES ballots(Id) ON DELETE CASCADE,
              FOREIGN KEY (CandidateId) REFERENCES ballot_candidates(Id) ON DELETE CASCADE,
              FOREIGN KEY (UserId) REFERENCES users(Id),
              UNIQUE(BallotId, CandidateId, UserId)
            );"
          );

          connection.Execute("CREATE INDEX idx_votes_ballot ON votes(BallotId);");
          connection.Execute("CREATE INDEX idx_votes_user ON votes(UserId);");
          connection.Execute("CREATE INDEX idx_votes_candidate ON votes(CandidateId);");
        }
      }
    }
  }
}