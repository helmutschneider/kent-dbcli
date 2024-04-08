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
    static readonly Argument<string> ARGUMENT_HOST = new("-h", "--host")
    {
        Description = "Database host."
    };
    static readonly Argument<string> ARGUMENT_DATABASE = new("-d", "--database")
    {
        Description = "Database name.",
    };
    static readonly Argument<string> ARGUMENT_USER = new("-u", "--user");
    static readonly Argument<string> ARGUMENT_PASSWORD = new("-p", "--password");
    static readonly Argument<string> ARGUMENT_CONNECTION_STRING = new("-c", "--connection-string")
    {
        Description = "Raw connection string. Overrides the other connection arguments."
    };
    static readonly Argument<string> ARGUMENT_OUT_FILE = new("-o", "--out-file");
    static readonly Argument<bool> ARGUMENT_VERBOSE = new("--verbose")
    {
        Description = "Print progress messages from SQL Tools Service.",
        Default = false,
    };
    static readonly Argument<bool> ARGUMENT_STDOUT = new("--stdout")
    {
        Description = "Write the backup to standard out instead of a file.",
        Default = false,
    };
    static readonly IArgument[] ARGUMENTS = new IArgument[] {
        ARGUMENT_HOST,
        ARGUMENT_DATABASE,
        ARGUMENT_USER,
        ARGUMENT_PASSWORD,
        ARGUMENT_CONNECTION_STRING,
        ARGUMENT_OUT_FILE,
        ARGUMENT_VERBOSE,
        ARGUMENT_STDOUT,
    };

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

    static T? GetNamedArgument<T>(string[] args, Argument<T> named)
    {
        for (var i = 0; i < args.Length; ++i)
        {
            var maybeName = args[i].Trim();

            if (!named.Names.Contains(maybeName))
            {
                continue;
            }

            var next = (i < (args.Length - 1)) ? args[i + 1] : string.Empty;
            var nextLooksLikeArgument = next.StartsWith("-");

            switch (named)
            {
                case Argument<bool>: {
                    if (nextLooksLikeArgument || string.IsNullOrEmpty(next))
                    {
                        return (T)(object)true;
                    }
                    if (bool.TryParse(next, out var parsed))
                    {
                        return (T)(object)parsed;
                    }
                    break;
                }
                case Argument<string>: {
                    if (!nextLooksLikeArgument && !string.IsNullOrEmpty(next))
                    {
                        return (T)(object)next;
                    }
                    break;
                }
            }
        }

        return named.Default;
    }

    static void Usage()
    {
        Console.WriteLine("Usage:");
        foreach (var (name, _) in _commands)
        {
            Console.WriteLine($"  {name}");
        }
        Console.WriteLine(string.Empty);
        Console.WriteLine("Arguments:");

        foreach (var arg in ARGUMENTS)
        {
            Console.WriteLine("  {0,-32} {1}", string.Join(", ", arg.Names), arg.Description);
        }

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

        var toStdout = GetNamedArgument(args, ARGUMENT_STDOUT);
        var verbose = GetNamedArgument(args, ARGUMENT_VERBOSE);

        var opts = new ScriptingParams
        {
            ConnectionString = connStr,
            ExcludeTypes = new List<string>()
            {
                // make sure we don't include any 'CREATE DATABASE' queries in the dump.
                "Database",
                "User,"
            },
            FilePath = outfile,
            Operation = ScriptingOperationType.Create,
            ScriptDestination = toStdout ? "ToEditor" : "ToSingleFile",
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

        var ctx = new ScriptContext(verbose);

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
                if (toStdout)
                {
                    Console.WriteLine(ctx.Output);
                }
                else
                {
                    Console.WriteLine($"[OK] Script written to '{opts.FilePath}'");
                }
                return 0;
            case ScriptStatus.Error:
                return 1;
            default:
                throw new InvalidOperationException();
        }
    }
}
