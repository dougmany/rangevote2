
using Dapper;
using Npgsql;
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

    private bool TableExists(NpgsqlConnection connection, string tableName)
    {
      var count = connection.QueryFirstOrDefault<int>(
        "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @TableName;",
        new { TableName = tableName }
      );
      return count > 0;
    }

    private bool ColumnExists(NpgsqlConnection connection, string tableName, string columnName)
    {
      var count = connection.QueryFirstOrDefault<int>(
        "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @TableName AND column_name = @ColumnName;",
        new { TableName = tableName, ColumnName = columnName }
      );
      return count > 0;
    }

    public void Setup()
    {
      using (var connection = new NpgsqlConnection(_databaseConfig.DatabaseName))
      {
        // Create candidate table if it doesn't exist
        if (!TableExists(connection, "candidate"))
        {
          connection.Execute(
            @"CREATE TABLE candidate (
              guid VARCHAR(50) NOT NULL,
              name VARCHAR(100) NOT NULL,
              description VARCHAR(1000) NULL,
              score INTEGER NULL,
              electionid VARCHAR(50),
              organizationid VARCHAR(36),
              image_link VARCHAR(500));"
          );
        }

        // Create users table
        if (!TableExists(connection, "users"))
        {
          connection.Execute(
            @"CREATE TABLE users (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              email VARCHAR(255) UNIQUE NOT NULL,
              passwordhash VARCHAR(255) NOT NULL,
              displayname VARCHAR(100),
              organizationid VARCHAR(36),
              createdat TIMESTAMPTZ NOT NULL,
              lastloginat TIMESTAMPTZ,
              preferredtheme VARCHAR(20) NOT NULL DEFAULT 'cow');"
          );
        }

        // Create organizations table
        if (!TableExists(connection, "organizations"))
        {
          connection.Execute(
            @"CREATE TABLE organizations (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              name VARCHAR(255) NOT NULL,
              ownerid VARCHAR(36) NOT NULL,
              createdat TIMESTAMPTZ NOT NULL,
              ispublic BOOLEAN NOT NULL DEFAULT FALSE,
              description TEXT);"
          );
        }

        // Create organization_members table
        if (!TableExists(connection, "organization_members"))
        {
          connection.Execute(
            @"CREATE TABLE organization_members (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              organizationid VARCHAR(36) NOT NULL,
              userid VARCHAR(36) NOT NULL,
              role VARCHAR(50) NOT NULL DEFAULT 'Member',
              joinedat TIMESTAMPTZ NOT NULL);"
          );
        }

        // Add columns to existing tables if missing (migration support)
        if (!ColumnExists(connection, "organizations", "ispublic"))
          connection.Execute("ALTER TABLE organizations ADD COLUMN ispublic BOOLEAN NOT NULL DEFAULT FALSE;");

        if (!ColumnExists(connection, "organizations", "description"))
          connection.Execute("ALTER TABLE organizations ADD COLUMN description TEXT;");

        if (!ColumnExists(connection, "users", "preferredtheme"))
          connection.Execute("ALTER TABLE users ADD COLUMN preferredtheme VARCHAR(20) NOT NULL DEFAULT 'cow';");

        if (!ColumnExists(connection, "candidate", "organizationid"))
          connection.Execute("ALTER TABLE candidate ADD COLUMN organizationid VARCHAR(36);");

        if (!ColumnExists(connection, "candidate", "image_link"))
          connection.Execute("ALTER TABLE candidate ADD COLUMN image_link VARCHAR(500);");

        // ========== NEW BALLOT SYSTEM TABLES ==========

        if (!TableExists(connection, "ballots"))
        {
          connection.Execute(
            @"CREATE TABLE ballots (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              name VARCHAR(200) NOT NULL,
              description TEXT,
              ownerid VARCHAR(36) NOT NULL,
              organizationid VARCHAR(36),
              status VARCHAR(20) NOT NULL DEFAULT 'Draft',
              createdat TIMESTAMPTZ NOT NULL,
              opendate TIMESTAMPTZ,
              closedate TIMESTAMPTZ,
              isopen BOOLEAN NOT NULL DEFAULT TRUE,
              ispublic BOOLEAN NOT NULL DEFAULT FALSE,
              candidatecount INTEGER NOT NULL DEFAULT 0,
              votecount INTEGER NOT NULL DEFAULT 0,
              FOREIGN KEY (ownerid) REFERENCES users(id),
              FOREIGN KEY (organizationid) REFERENCES organizations(id)
            );"
          );

          connection.Execute("CREATE INDEX idx_ballots_owner ON ballots(ownerid);");
          connection.Execute("CREATE INDEX idx_ballots_org ON ballots(organizationid);");
          connection.Execute("CREATE INDEX idx_ballots_status ON ballots(status);");
          connection.Execute("CREATE INDEX idx_ballots_public ON ballots(ispublic);");
        }
        else
        {
          if (!ColumnExists(connection, "ballots", "ispublic"))
          {
            connection.Execute("ALTER TABLE ballots ADD COLUMN ispublic BOOLEAN NOT NULL DEFAULT FALSE;");
            connection.Execute("CREATE INDEX IF NOT EXISTS idx_ballots_public ON ballots(ispublic);");
          }
        }

        if (!TableExists(connection, "ballot_candidates"))
        {
          connection.Execute(
            @"CREATE TABLE ballot_candidates (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              ballotid VARCHAR(36) NOT NULL,
              name VARCHAR(200) NOT NULL,
              description TEXT,
              imagelink VARCHAR(500),
              createdat TIMESTAMPTZ NOT NULL,
              FOREIGN KEY (ballotid) REFERENCES ballots(id) ON DELETE CASCADE
            );"
          );

          connection.Execute("CREATE INDEX idx_ballot_candidates_ballot ON ballot_candidates(ballotid);");
        }

        if (!TableExists(connection, "ballot_share_links"))
        {
          connection.Execute(
            @"CREATE TABLE ballot_share_links (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              ballotid VARCHAR(36) NOT NULL,
              sharetoken VARCHAR(100) NOT NULL UNIQUE,
              permission VARCHAR(20) NOT NULL,
              createdat TIMESTAMPTZ NOT NULL,
              createdby VARCHAR(36) NOT NULL,
              expiresat TIMESTAMPTZ,
              isactive BOOLEAN NOT NULL DEFAULT TRUE,
              usecount INTEGER NOT NULL DEFAULT 0,
              FOREIGN KEY (ballotid) REFERENCES ballots(id) ON DELETE CASCADE,
              FOREIGN KEY (createdby) REFERENCES users(id)
            );"
          );

          connection.Execute("CREATE INDEX idx_share_links_ballot ON ballot_share_links(ballotid);");
          connection.Execute("CREATE INDEX idx_share_links_token ON ballot_share_links(sharetoken);");
        }

        if (!TableExists(connection, "ballot_permissions"))
        {
          connection.Execute(
            @"CREATE TABLE ballot_permissions (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              ballotid VARCHAR(36) NOT NULL,
              userid VARCHAR(36),
              invitedemail VARCHAR(255),
              permission VARCHAR(20) NOT NULL,
              createdat TIMESTAMPTZ NOT NULL,
              createdby VARCHAR(36) NOT NULL,
              acceptedat TIMESTAMPTZ,
              FOREIGN KEY (ballotid) REFERENCES ballots(id) ON DELETE CASCADE,
              FOREIGN KEY (userid) REFERENCES users(id),
              FOREIGN KEY (createdby) REFERENCES users(id)
            );"
          );

          connection.Execute("CREATE INDEX idx_ballot_permissions_ballot ON ballot_permissions(ballotid);");
          connection.Execute("CREATE INDEX idx_ballot_permissions_user ON ballot_permissions(userid);");
          connection.Execute("CREATE INDEX idx_ballot_permissions_email ON ballot_permissions(invitedemail);");
        }

        if (!TableExists(connection, "votes"))
        {
          connection.Execute(
            @"CREATE TABLE votes (
              id VARCHAR(36) PRIMARY KEY NOT NULL,
              ballotid VARCHAR(36) NOT NULL,
              candidateid VARCHAR(36) NOT NULL,
              userid VARCHAR(36) NOT NULL,
              score INTEGER NOT NULL,
              createdat TIMESTAMPTZ NOT NULL,
              updatedat TIMESTAMPTZ NOT NULL,
              FOREIGN KEY (ballotid) REFERENCES ballots(id) ON DELETE CASCADE,
              FOREIGN KEY (candidateid) REFERENCES ballot_candidates(id) ON DELETE CASCADE,
              FOREIGN KEY (userid) REFERENCES users(id),
              UNIQUE(ballotid, candidateid, userid)
            );"
          );

          connection.Execute("CREATE INDEX idx_votes_ballot ON votes(ballotid);");
          connection.Execute("CREATE INDEX idx_votes_user ON votes(userid);");
          connection.Execute("CREATE INDEX idx_votes_candidate ON votes(candidateid);");
        }
      }
    }
  }
}
