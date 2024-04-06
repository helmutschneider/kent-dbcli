using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using Microsoft.SqlTools.ServiceLayer.Scripting;
using Microsoft.SqlTools.ServiceLayer.Scripting.Contracts;


namespace Kent.DbCli;

using CommandFn = Func<string[], Task<int>>;

class Program
{
    static readonly string[] ARGUMENT_HOST = new[] { "-h", "--host" };
    static readonly string[] ARGUMENT_DATABASE = new[] { "-d", "--database" };
    static readonly string[] ARGUMENT_USER = new[] { "-u", "--user" };
    static readonly string[] ARGUMENT_PASSWORD = new[] { "-p", "--password" };
    static readonly string[] ARGUMENT_CONNECTION_STRING = new[] { "-c", "--connection-string" };
    static readonly string[] ARGUMENT_OUT_FILE = new[] { "-o", "--out-file" };

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

    static string GetNamedArgument(string[] args, params string[] names)
    {
        Debug.Assert(names.Length != 0);

        for (var i = 0; i < args.Length; ++i)
        {
            var arg = args[i].Trim();
            if (names.Contains(arg))
            {
                return (i < (args.Length - 1)) ? args[i + 1] : string.Empty;
            }
        }

        return string.Empty;
    }

    static void Usage()
    {
        static void ArgumentUsage(string[] names, string description)
        {
            Console.WriteLine("  {0,-24} {1}", string.Join(", ", names), description);
        }

        Console.WriteLine("Usage:");
        foreach (var (name, _) in _commands)
        {
            Console.WriteLine($"  {name}");
        }
        Console.WriteLine(string.Empty);
        Console.WriteLine("Arguments:");
        ArgumentUsage(ARGUMENT_HOST, "database host");
        ArgumentUsage(ARGUMENT_DATABASE, "database name");
        ArgumentUsage(ARGUMENT_USER, "user");
        ArgumentUsage(ARGUMENT_PASSWORD, "password");
        ArgumentUsage(ARGUMENT_CONNECTION_STRING, "raw connection string");
        ArgumentUsage(ARGUMENT_OUT_FILE, "out file");
        Console.WriteLine(string.Empty);
        Console.WriteLine("Examples:");
        Console.WriteLine("  dump-schema   -h localhost -d dbname -u sa -p password");
        Console.WriteLine("  dump-database -h localhost -d dbname -u sa -p password");
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
        var outfile = GetNamedArgument(args, ARGUMENT_OUT_FILE);

        if (string.IsNullOrEmpty(outfile))
        {
            var dt = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            outfile = $"script-{dt}.sql";
        }

        // the 'FilePath' property must contain a directory when given to the scripting service.
        // if none is given we just default to the current directory.
        if (string.IsNullOrEmpty(Path.GetDirectoryName(outfile)))
        {
            outfile = Path.Join(Directory.GetCurrentDirectory(), outfile);
        }

        var connStr = GetNamedArgument(args, ARGUMENT_CONNECTION_STRING);

        if (string.IsNullOrEmpty(connStr))
        {
            var builder = new SqlConnectionStringBuilder
            {
                CommandTimeout = 10,
                ConnectTimeout = 10,
                DataSource = GetNamedArgument(args, ARGUMENT_HOST),
                Encrypt = false,
                InitialCatalog = GetNamedArgument(args, ARGUMENT_DATABASE),
                IntegratedSecurity = false,
                UserID = GetNamedArgument(args, ARGUMENT_USER),
                Password = GetNamedArgument(args, ARGUMENT_PASSWORD),
            };

            if (string.IsNullOrEmpty(builder.DataSource))
            {
                Usage();
                return 1;
            }

            connStr = builder.ConnectionString;
        }

        var opts = new ScriptingParams
        {
            ConnectionString = connStr,
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
                await Task.Delay(250);
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
