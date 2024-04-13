using System;
using System.IO;
using System.Linq;
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
        
        var ok = await Program.InvokeAsync(new [] {
            "dump-schema",
            "--connection-string", _db.ConnectionString,
            "--output", path,
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
        
        var ok = await Program.InvokeAsync(new [] {
            "dump-database",
            "--connection-string", _db.ConnectionString,
            "--output", path,
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
        
        var ok = await Program.InvokeAsync(new [] {
            "dump-database",
            "--connection-string", _db.ConnectionString,
            "--output", path,
            "--exclude-table", "Car"
        });

        Assert.Equal(0, ok);
        Assert.True(File.Exists(path));
        
        var dump = File.ReadAllText(path);
        var exists = Regex.IsMatch(dump, @"^INSERT \[dbo\]\.", RegexOptions.Multiline);
        
        Assert.False(exists);
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
