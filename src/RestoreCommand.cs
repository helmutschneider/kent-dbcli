using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlTools.ServiceLayer.BatchParser;
using Microsoft.SqlTools.ServiceLayer.BatchParser.ExecutionEngineCode;

namespace Kent.DbCli;

public class RestoreCommand : ICommand
{
    public IArgument[] AcceptsArguments { get; } = new IArgument[]
    {
        Arguments.SERVER,
        Arguments.DATABASE,
        Arguments.USER,
        Arguments.PASSWORD,
        Arguments.INPUT_FILE,
        Arguments.TIMEOUT,
        Arguments.BATCH_SIZE,
    };

    public Task<int> ExecuteAsync(string[] args)
    {
        var inputFile = Arguments.INPUT_FILE.GetOrDefault(args);
        var connStrBuilder = Arguments.BuildConnectionString(args);
        using var conn = new SqlConnection(connStrBuilder.ConnectionString);
        conn.Open();

        var batchSize = Arguments.BATCH_SIZE.GetOrDefault(args);
        var numExecuted = 0;
        var scripts = new List<string>(batchSize);
        var execHandler = new BatchParser
        {
            Execute = (script, repeatCount, lineNumber, sqlCmdCommand) =>
            {
                scripts.Add(script);
                if (scripts.Count == batchSize)
                {
                    numExecuted += ExecuteScripts(conn, scripts);
                    Console.WriteLine("OK: executed {0} statements", numExecuted);
                    scripts.Clear();
                }
                return true;
            },
            ErrorMessage = (message, messageType) =>
            {
                Console.WriteLine("Error: {0}", message);
            },
        };

        using var reader = new StreamReader(inputFile!);
        using var parser = new Parser(execHandler, execHandler, reader, inputFile);

        var tstart = DateTime.UtcNow;
        parser.Parse();

        // execute any remaining scripts that didn't fit in a batch.
        numExecuted += ExecuteScripts(conn, scripts);

        var elapsed = DateTime.UtcNow - tstart;
        Console.WriteLine("OK: executed {0} statements in {1} seconds", numExecuted, (int)elapsed.TotalSeconds);

        return Task.FromResult(0);
    }

    static int ExecuteScripts(SqlConnection conn, IReadOnlyList<string> scripts)
    {
        if (scripts.Count == 0)
        {
            return 0;
        }

        using var trx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = conn.ConnectionTimeout;
        cmd.Transaction = trx;

        foreach (var script in scripts)
        {
            cmd.CommandText = script;
            cmd.ExecuteNonQuery();
        }

        trx.Commit();

        return scripts.Count;
    }
}
