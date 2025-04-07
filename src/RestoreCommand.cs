using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
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
        var scriptStr = new StringBuilder(batchSize!.Value);
        var numStatements = 0;
        var execHandler = new BatchParser
        {
            Execute = (script, repeatCount, lineNumber, sqlCmdCommand) =>
            {
                if (!string.IsNullOrWhiteSpace(script))
                {
                    scriptStr.Append(script);
                    numStatements += 1;
                }
                if (scriptStr.Length >= batchSize.Value)
                {
                    numExecuted += ExecuteScript(conn, scriptStr.ToString(), numStatements);
                    Console.WriteLine("OK: executed {0} statements", numExecuted);
                    scriptStr.Clear();
                    numStatements = 0;
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
        numExecuted += ExecuteScript(conn, scriptStr.ToString(), numStatements);

        var elapsed = DateTime.UtcNow - tstart;
        Console.WriteLine("OK: executed {0} statements in {1} seconds", numExecuted, (int)elapsed.TotalSeconds);

        return Task.FromResult(0);
    }

    static int ExecuteScript(SqlConnection conn, string script, int numStatements)
    {
        if (numStatements == 0)
        {
            return 0;
        }

        using var trx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = script;
        cmd.CommandTimeout = conn.ConnectionTimeout;
        cmd.Transaction = trx;
        cmd.ExecuteNonQuery();
        trx.Commit();

        return numStatements;
    }
}
