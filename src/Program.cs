using System;
using System.Collections.Generic;
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
    static readonly Dictionary<string, CommandFn> _commands = new()
    {
        {"dump-schema", DumpSchemaAsync},
        {"dump-database", DumpDatabaseAsync},
    };

    static async Task Main(string[] args)
    {
        if (args.Length < 2)
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

    static void Usage()
    {
        Console.WriteLine("Usage:");
        foreach (var (name, _) in _commands)
        {
            Console.WriteLine($"  {name} [connection-string]");
        }
        Console.WriteLine("Examples:");
        Console.WriteLine("  dump-schema 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'");
        Console.WriteLine("  dump-database 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'");
    }

    static Task<int> DumpSchemaAsync(string[] args)
    {
        return RunScriptRequestAsync(args[0], "SchemaOnly");
    }

    static Task<int> DumpDatabaseAsync(string[] args)
    {
        return RunScriptRequestAsync(args[0], "SchemaAndData");
    }

    static async Task<int> RunScriptRequestAsync(string connectionString, string typeOfData)
    {
        var connStrBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            CommandTimeout = 10,
            ConnectTimeout = 10,
            Encrypt = false,
        };
        var dt = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Join(Directory.GetCurrentDirectory(), $"script-{dt}.sql");
        var opts = new ScriptingParams
        {
            ConnectionString = connStrBuilder.ConnectionString,
            ExcludeTypes = new List<string>()
            {
                // make sure we don't include any 'CREATE DATABASE' queries in the dump.
                "database",
            },
            FilePath = path,
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
                Console.WriteLine($"[OK] Script written to '{path}'");
                return 0;
            case ScriptStatus.Error:
                return 1;
            default:
                throw new InvalidOperationException();
        }
    }
}
