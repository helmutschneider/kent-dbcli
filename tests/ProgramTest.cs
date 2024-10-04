using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Kent.DbCli.Tests;

public class ProgramTest : IClassFixture<DatabaseFixture>, IDisposable
{
    readonly DatabaseFixture _db;

    public ProgramTest(DatabaseFixture db)
    {
        _db = db;

        _db.ExecuteSql("INSERT INTO [Car] (Make, Model) VALUES ('Volvo', 'V70')");
        _db.ExecuteSql("INSERT INTO [Car] (Make, Model) VALUES ('Toyota', 'Prius')");
        _db.ExecuteSql("INSERT INTO [Car] (Make, Model) VALUES ('Tesla', 'Model S')");
    }

    [Fact]
    public async Task WithInvalidCommand()
    {
        var res = await Program.InvokeAsync(Array.Empty<string>());

        Assert.Equal(1, res);
    }

    [Fact]
    public async Task DumpSchema()
    {
        var sln = FindSolutionPath();
        var path = Path.Combine(sln!, "dump.sql");

        var ok = await Program.InvokeAsync(new[] {
            "backup",
            "-S", _db.ConnectionStringBuilder.DataSource,
            "-U", _db.ConnectionStringBuilder.UserID,
            "-P", _db.ConnectionStringBuilder.Password,
            "-d", _db.DatabaseName,
            "--schema-only",
            "--output-file", path,
        });

        Assert.Equal(0, ok);

        var dump = File.ReadAllText(path);

        Assert.DoesNotContain("INSERT", dump);
    }

    [Fact]
    public async Task DumpDatabase()
    {
        var sln = FindSolutionPath();
        var path = Path.Combine(sln!, "dump.sql");

        var ok = await Program.InvokeAsync(new[] {
            "backup",
            "-S", _db.ConnectionStringBuilder.DataSource,
            "-U", _db.ConnectionStringBuilder.UserID,
            "-P", _db.ConnectionStringBuilder.Password,
            "-d", _db.DatabaseName,
            "--output-file", path,
        });

        Assert.Equal(0, ok);
        Assert.True(File.Exists(path));

        var dump = File.ReadAllText(path);
        var numInserts = Regex.Matches(dump, @"^INSERT \[dbo\]\.", RegexOptions.Multiline);

        Assert.Equal(3, numInserts.Count);
    }

    [Fact]
    public async Task DumpDatabaseAndExcludeTable()
    {
        var sln = FindSolutionPath();
        var path = Path.Combine(sln!, "dump.sql");

        var ok = await Program.InvokeAsync(new[] {
            "backup",
            "-S", _db.ConnectionStringBuilder.DataSource,
            "-U", _db.ConnectionStringBuilder.UserID,
            "-P", _db.ConnectionStringBuilder.Password,
            "-d", _db.DatabaseName,
            "--output-file", path,
            "--exclude-table", "Car",
        });

        Assert.Equal(0, ok);
        Assert.True(File.Exists(path));

        var dump = File.ReadAllText(path);
        var exists = Regex.IsMatch(dump, @"^INSERT \[dbo\]\.", RegexOptions.Multiline);

        Assert.False(exists);
    }

    [Fact]
    public async Task DumpDatabaseOnlyIncludesOneByteOrderMark()
    {
        var sln = FindSolutionPath();
        var path = Path.Combine(sln!, "dump.sql");

        var ok = await Program.InvokeAsync(new[] {
            "backup",
            "-S", _db.ConnectionStringBuilder.DataSource,
            "-U", _db.ConnectionStringBuilder.UserID,
            "-P", _db.ConnectionStringBuilder.Password,
            "-d", _db.DatabaseName,
            "--output-file", path,
        });

        Assert.Equal(0, ok);
        Assert.True(File.Exists(path));

        var bytes = File.ReadAllBytes(path);
        var foundByteOrderMarks = 0;

        for (var i = 0; i < (bytes.Length - 1); ++i)
        {
            if (bytes[i] == '\xFF' && bytes[i + 1] == '\xFE')
            {
                foundByteOrderMarks += 1;
            }
        }

        Assert.Equal(1, foundByteOrderMarks);
    }

    [Fact]
    public async Task RestoreDoesStuff()
    {
        var script = """
        CREATE TABLE [Boat] (
            [Id] INTEGER PRIMARY KEY NOT NULL IDENTITY(1, 1),
            [Name] NVARCHAR(MAX) NOT NULL
        )
        GO
        INSERT INTO [Boat] ([Name]) VALUES ('Boaty McBoatFace')
        GO
        """;
        var sln = FindSolutionPath();
        var path = Path.Combine(sln!, "boat.sql");

        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(script));
        var ok = await Program.InvokeAsync(new[] {
            "restore",
            "-S", _db.ConnectionStringBuilder.DataSource,
            "-U", _db.ConnectionStringBuilder.UserID,
            "-P", _db.ConnectionStringBuilder.Password,
            "-d", _db.DatabaseName,
            "--input-file", path,
            "--verbose",
        });
        Assert.Equal(0, ok);

        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandType = System.Data.CommandType.Text;
        cmd.CommandText = "SELECT [Name] FROM [Boat]";
        cmd.Prepare();

        var names = new List<string>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            names.Add(rdr.GetString(0));
        }

        Assert.Single(names);
        Assert.Equal("Boaty McBoatFace", names[0]);
    }

    public void Dispose()
    {
        _db.ExecuteSql("TRUNCATE TABLE [Car]");
    }

    static string? FindSolutionPath()
    {
        var assembly = typeof(DatabaseFixture).Assembly;
        var path = Path.GetDirectoryName(assembly.Location);

        while (path != null && Directory.GetFiles(path, "*.sln").Length == 0)
        {
            path = Directory.GetParent(path)?.FullName;
        }

        return path;
    }
}
