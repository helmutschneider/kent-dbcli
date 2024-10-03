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
    public readonly string ConnectionString;
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
        var builder = GetConnectionStringBuilder(dbName);

        this.ConnectionStringBuilder = builder;
        this.ConnectionString = builder.ConnectionString;
        this.DatabaseName = builder.InitialCatalog;

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
        if (WantsLocalDBConnection())
        {
            return new SqlConnectionStringBuilder()
            {
                CommandTimeout = 10,
                ConnectTimeout = 10,
                DataSource = @"(localdb)\MSSQLLocalDB",
                Encrypt = false,
                InitialCatalog = database,
                IntegratedSecurity = true,
            };
        }
        return new SqlConnectionStringBuilder()
        {
            CommandTimeout = 10,
            ConnectTimeout = 10,
            DataSource = Environment.GetEnvironmentVariable("DATABASE_HOST"),
            Encrypt = false,
            InitialCatalog = database,
            Password = Environment.GetEnvironmentVariable("DATABASE_PASSWORD"),
            UserID = Environment.GetEnvironmentVariable("DATABASE_USER"),
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

    static bool WantsLocalDBConnection()
    {
        var host = Environment.GetEnvironmentVariable("DATABASE_HOST");
        return string.IsNullOrEmpty(host);
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
