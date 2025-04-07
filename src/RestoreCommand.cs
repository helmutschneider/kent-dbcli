using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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
        var batch = new StringBuilder(batchSize!.Value);
        var numStatements = 0;

        var execHandler = new BatchParser
        {
            Execute = (script, repeatCount, lineNumber, sqlCmdCommand) =>
            {
                script = script.Trim();

                if (string.IsNullOrEmpty(script))
                {
                    return true;
                }

                if (MustExecuteAlone(script))
                {
                    numExecuted += ExecuteBatch(conn, batch.ToString(), numStatements);
                    batch.Clear();
                    numStatements = 0;
                    numExecuted += ExecuteBatch(conn, script, 1);
                    Console.WriteLine("OK: executed {0} statements", numExecuted);
                    return true;
                }

                batch.Append(script);
                batch.Append(";\n");
                numStatements += 1;

                if (batch.Length >= batchSize.Value)
                {
                    numExecuted += ExecuteBatch(conn, batch.ToString(), numStatements);
                    batch.Clear();
                    numStatements = 0;
                    Console.WriteLine("OK: executed {0} statements", numExecuted);
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

        var ts = Stopwatch.StartNew();
        parser.Parse();

        // execute any remaining scripts that didn't fit in a batch.
        numExecuted += ExecuteBatch(conn, batch.ToString(), numStatements);

        ts.Stop();
        Console.WriteLine("OK: executed {0} statements in {1} seconds", numExecuted, (int)ts.Elapsed.TotalSeconds);

        return Task.FromResult(0);
    }

    static int ExecuteBatch(SqlConnection conn, string batch, int numStatements)
    {
        if (numStatements == 0)
        {
            return 0;
        }

        using var trx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = batch;
        cmd.CommandTimeout = conn.ConnectionTimeout;
        cmd.Transaction = trx;
        cmd.ExecuteNonQuery();
        trx.Commit();

        return numStatements;
    }

    static bool MustExecuteAlone(string script)
    {
        return script.StartsWith("create database", StringComparison.OrdinalIgnoreCase)
            || script.StartsWith("create schema", StringComparison.OrdinalIgnoreCase);
    }
}
