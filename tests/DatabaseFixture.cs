using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kent.DbCli;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kent.DbCli.Tests;

public class DatabaseFixture : IDisposable
{
    public readonly SqlConnectionStringBuilder ConnectionStringBuilder;
    public readonly string DatabaseName;
    public readonly SqlConnection Connection;

    const string DATABASE_SCHEMA = @"
    CREATE TABLE [Car] (
      [Id] INTEGER PRIMARY KEY NOT NULL IDENTITY(1, 1),
      [Make] NVARCHAR(MAX) NOT NULL,
      [Model] NVARCHAR(MAX) NOT NULL
    )
    ";

    public DatabaseFixture()
    {
        var dbName = GetRandomDatabaseName();

        this.ConnectionStringBuilder = GetConnectionStringBuilder(dbName);
        this.DatabaseName = dbName;

        using (var conn = GetConnection(string.Empty))
        {
            ExecuteSql(conn, $"CREATE DATABASE [{dbName}]");
        }

        this.Connection = GetConnection(dbName);
        ExecuteSql(Connection, DATABASE_SCHEMA);
    }

    public void ExecuteSql(string query)
    {
        ExecuteSql(Connection, query);
    }

    static string GetRandomDatabaseName()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var suffix = Regex.Replace(
            Convert.ToBase64String(bytes),
            @"[^A-Za-z0-9]",
            string.Empty
        );
        return $"kent-{suffix.Substring(0, 8)}";
    }

    static SqlConnectionStringBuilder GetConnectionStringBuilder(string database)
    {
        var host = Environment.GetEnvironmentVariable("DATABASE_HOST") ?? @"(localdb)\mssqllocaldb";
        var username = Environment.GetEnvironmentVariable("DATABASE_USER") ?? string.Empty;
        var password = Environment.GetEnvironmentVariable("DATABASE_PASSWORD") ?? string.Empty;
        var isLocalDb = host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);

        return new SqlConnectionStringBuilder()
        {
            CommandTimeout = 10,
            ConnectTimeout = 10,
            DataSource = host,
            Encrypt = false,
            InitialCatalog = database,
            IntegratedSecurity = isLocalDb,
            Password = password,
            UserID = username,
        };
    }

    static SqlConnection GetConnection(string database)
    {
        var builder = GetConnectionStringBuilder(database);
        var conn = new SqlConnection(builder.ConnectionString);
        conn.Open();

        return conn;
    }

    static void ExecuteSql(SqlConnection conn, string query)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = query;
            cmd.Prepare();
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        Connection.Dispose();

        using (var conn = GetConnection(string.Empty))
        {
            ExecuteSql(conn, $"ALTER DATABASE [{DatabaseName}] SET OFFLINE WITH ROLLBACK IMMEDIATE");
            ExecuteSql(conn, $"DROP DATABASE [{DatabaseName}]");
        }
    }
}
