using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kent.DbCli;

public class RestoreBakCommand : ICommand
{
    public IArgument[] AcceptsArguments { get; } = new IArgument[]
    {
        Arguments.SERVER,
        Arguments.DATABASE,
        Arguments.USER,
        Arguments.PASSWORD,
        Arguments.INPUT_FILE,
    };

    public async Task<int> ExecuteAsync(string[] args)
    {
        var connStr = Arguments.BuildConnectionString(args);
        connStr.InitialCatalog = "master";
        connStr.CommandTimeout = 0;

        using var conn = new SqlConnection(connStr.ConnectionString);
        await conn.OpenAsync();
        var storageDir = await DetermineStorageDirectoryAsync(conn);

        if (string.IsNullOrEmpty(storageDir))
        {
            Console.WriteLine("Error: could not determine storage path");
            return 1;
        }

        var dataName = string.Empty;
        var logName = string.Empty;
        var inputFile = Arguments.INPUT_FILE.GetOrDefault(args)!;
        var dbName = Arguments.DATABASE.GetOrDefault(args);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "restore filelistonly from disk = @BackupPath";
            
            AddStringParam(cmd, "BackupPath", inputFile);

            await cmd.PrepareAsync();

            using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                var type = rdr.GetString("Type").ToUpper();
                
                switch (type)
                {
                    case "D":
                        dataName = rdr.GetString("LogicalName");
                        break;
                    case "L":
                        logName = rdr.GetString("LogicalName");
                        break;
                }
            }
        }

        var moveDataPath = $"{storageDir}{dbName}.mdf";
        var moveLogPath = $"{storageDir}{dbName}.ldf";

        Console.WriteLine($"Restoring '{dataName}' to '{moveDataPath}'");
        Console.WriteLine($"Restoring '{logName}' to '{moveLogPath}'");

        var ts = Stopwatch.StartNew();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = $"""
            restore database [{dbName}]
               from disk = @BackupPath
               with recovery,
               move @DataName to @DataPath,
               move @LogName to @LogPath
            """;

            AddStringParam(cmd, "BackupPath", inputFile);
            AddStringParam(cmd, "DataName", dataName);
            AddStringParam(cmd, "DataPath", moveDataPath);
            AddStringParam(cmd, "LogName", logName);
            AddStringParam(cmd, "LogPath", moveLogPath);

            await cmd.PrepareAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        ts.Stop();

        Console.WriteLine("OK: restored database '{0}' in {1} seconds", dbName, (int)ts.Elapsed.TotalSeconds);

        return 0;
    }

    static async Task<string> DetermineStorageDirectoryAsync(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = "select physical_name from sys.database_files";

        using var rdr = await cmd.ExecuteReaderAsync();

        if (await rdr.ReadAsync())
        {
            var path = rdr.GetString(0);
            var match = Regex.Match(path, @"(.+[\\/])[^\\/]+$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    static void AddStringParam(DbCommand cmd, string name, string value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.DbType = DbType.String;
        param.Size = -1;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
