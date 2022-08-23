
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
        var table = connection.Query<String>("SELECT name FROM sqlite_master WHERE type='table' AND name = 'candidate';");
        var tableName = table.FirstOrDefault();
        if (!string.IsNullOrEmpty(tableName) && tableName == "candidate")
          return;

      connection.Execute(
        @"Create Table candidate (
          GUID VARCHAR(50) NOT NULL,
          Name VARCHAR(100) NOT NULL,
          Description VARCHAR(1000) NULL,
          Score Int32 NULL,
          ElectionID VARCHAR(50));"
        );
      }
    }
  }
}