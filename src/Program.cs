using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlTools.ServiceLayer.Scripting;
using Microsoft.SqlTools.ServiceLayer.Scripting.Contracts;

namespace Kent.DbCli;

using CommandFn = Func<string[], Task<int>>;

class Program
{
    const string ARGUMENT_CONNECTION_STRING = "connection-string";
    const string ARGUMENT_OUT_FILE = "out-file";

    static readonly Dictionary<string, CommandFn> _commands = new()
    {
        {"dump-schema", DumpSchemaAsync},
        {"dump-database", DumpDatabaseAsync},
    };

    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            Environment.Exit(1);
            return;
        }
        var name = args[0];
        if (!_commands.TryGetValue(name, out var fn))
        {
            Usage();
            Environment.Exit(1);
            return;
        }
        var code = await fn(args.Skip(1).ToArray());
        Environment.Exit(code);
    }

    static string? GetNamedArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length; ++i)
        {
            var arg = args[i];
            var match = Regex.Match(arg, @"^--(\S+)");

            if (match.Success && match.Groups[1].Value == name)
            {
                return (i < (args.Length - 1)) ? args[i + 1] : string.Empty;
            }
        }
        return null;
    }

    static void Usage()
    {
        Console.WriteLine("Usage:");
        foreach (var (name, _) in _commands)
        {
            Console.WriteLine($"  {name} --connection-string connstr [--out-file path]");
        }
        Console.WriteLine(string.Empty);
        Console.WriteLine("Examples:");
        Console.WriteLine("  dump-schema \\\n    --connection-string 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'");
        Console.WriteLine("  dump-database \\\n    --connection-string 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'");
    }

    static Task<int> DumpSchemaAsync(string[] args)
    {
        return RunScriptRequestAsync(args, "SchemaOnly");
    }

    static Task<int> DumpDatabaseAsync(string[] args)
    {
        return RunScriptRequestAsync(args, "SchemaAndData");
    }

    static async Task<int> RunScriptRequestAsync(string[] args, string typeOfData)
    {
        var connectionString = GetNamedArgument(args, ARGUMENT_CONNECTION_STRING);

        if (string.IsNullOrEmpty(connectionString))
        {
            Usage();
            return 1;
        }

        var outfile = GetNamedArgument(args, ARGUMENT_OUT_FILE);

        if (string.IsNullOrEmpty(outfile))
        {
            var dt = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            outfile = $"script-{dt}.sql";
        }

        if (string.IsNullOrEmpty(Path.GetDirectoryName(outfile)))
        {
            outfile = Path.Join(Directory.GetCurrentDirectory(), outfile);
        }

        var connStrBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            CommandTimeout = 10,
            ConnectTimeout = 10,
            Encrypt = false,
        };
        var opts = new ScriptingParams
        {
            ConnectionString = connStrBuilder.ConnectionString,
            ExcludeTypes = new List<string>()
            {
                // make sure we don't include any 'CREATE DATABASE' queries in the dump.
                "database",
            },
            FilePath = outfile,
            Operation = ScriptingOperationType.Create,
            ScriptDestination = "ToSingleFile",
            ScriptOptions = new ScriptOptions
            {
                ContinueScriptingOnError = false,
                IncludeDescriptiveHeaders = false,
                ScriptFullTextIndexes = true,
                ScriptForeignKeys = true,
                ScriptIndexes = true,
                ScriptUseDatabase = false,
                TargetDatabaseEngineEdition = "SqlServerExpressEdition",
                TypeOfDataToScript = typeOfData,
                UniqueKeys = true,
            },
        };

        var ctx = new ScriptContext();

        using (var scripting = new ScriptingService())
        {
            await scripting.HandleScriptExecuteRequest(opts, ctx);
            while (ctx.Status == ScriptStatus.InProgress)
            {
                await Task.Delay(500);
            }
        }

        switch (ctx.Status)
        {
            case ScriptStatus.Success:
                Console.WriteLine($"[OK] Script written to '{opts.FilePath}'");
                return 0;
            case ScriptStatus.Error:
                return 1;
            default:
                throw new InvalidOperationException();
        }
    }
}
