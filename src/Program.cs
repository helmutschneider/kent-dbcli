using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.SqlTools.ServiceLayer.Scripting;
using Microsoft.SqlTools.ServiceLayer.Scripting.Contracts;
using Microsoft.SqlTools.ServiceLayer.BatchParser;
using Microsoft.SqlTools.ServiceLayer.BatchParser.ExecutionEngineCode;
using System.Threading;

namespace Kent.DbCli;

using CommandFn = Func<string[], Task<int>>;

public class Program
{
    // let's keep these argument names vaguely similar to 'sqlcmd'...
    // https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility

    static readonly Argument<string> ARGUMENT_SERVER = new("-S", "--server")
    {
        Description = "Database host",
        Default = "localhost",
    };
    static readonly Argument<string> ARGUMENT_DATABASE = new("-d", "--database-name")
    {
        Description = "Database name",
        Required = true,
    };
    static readonly Argument<string> ARGUMENT_USER = new("-U", "--user-name")
    {
        Description = "Database username",
        Default = "sa",
    };
    static readonly Argument<string> ARGUMENT_PASSWORD = new("-P", "--password")
    {
        Description = "Database password",
        Default = string.Empty,
    };
    static readonly Argument<bool> ARGUMENT_VERBOSE = new("--verbose")
    {
        Description = "Print progress messages from SQL Tools Service",
        Default = false,
    };
    static readonly Argument<string> ARGUMENT_INPUT_FILE = new("-i", "--input-file")
    {
        Description = "Input script path (restore)",
        Required = true,
    };
    static readonly Argument<string> ARGUMENT_OUTPUT_FILE = new("-o", "--output-file")
    {
        Description = "Output script path (backup)",
    };
    static readonly Argument<string> ARGUMENT_EXCLUDE_TABLE = new("--exclude-table")
    {
        Description = "Exclude data from a table. May be specified multiple times (backup)",
    };
    static readonly Argument<bool> ARGUMENT_SCHEMA_ONLY = new("--schema-only")
    {
        Description = "Export the database schema without including table data (backup)",
    };
    static readonly Argument<int> ARGUMENT_TIMEOUT = new("-t", "--query-timeout")
    {
        Description = "Query timeout for individual statements",
        Default = 10,
    };
    static readonly IArgument[] ARGUMENTS = new IArgument[] {
        ARGUMENT_SERVER,
        ARGUMENT_DATABASE,
        ARGUMENT_USER,
        ARGUMENT_PASSWORD,
        ARGUMENT_INPUT_FILE,
        ARGUMENT_OUTPUT_FILE,
        ARGUMENT_TIMEOUT,
        ARGUMENT_VERBOSE,
        ARGUMENT_EXCLUDE_TABLE,
        ARGUMENT_SCHEMA_ONLY,
    };

    static readonly Dictionary<string, CommandFn> _commands = new()
    {
        {"backup", BackupAsync},
        {"restore", RestoreAsync},
    };

    const string SCRIPT_DESTINATION_FILE = "ToSingleFile";
    const string SCRIPT_DESTINATION_EDITOR = "ToEditor";
    const string SCRIPT_SCHEMA = "SchemaOnly";
    const string SCRIPT_SCHEMA_AND_DATA = "SchemaAndData";
    const string SCRIPT_DATA = "DataOnly";

    public static async Task Main(string[] args)
    {
        var code = await InvokeAsync(args);
        Environment.Exit(code);
    }

    public static async Task<int> InvokeAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 1;
        }
        var name = args[0];
        if (!_commands.TryGetValue(name, out var fn))
        {
            Usage();
            return 1;
        }
        var code = await fn(args.Skip(1).ToArray());
        return code;
    }

    static T? GetNamedArgument<T>(string[] args, Argument<T> named)
    {
        var parsed = GetNamedArrayArgument(args, named);
        if (parsed.Count != 0)
        {
            return parsed[0];
        }
        return named.Default;
    }

    static IReadOnlyList<T> GetNamedArrayArgument<T>(string[] args, Argument<T> named)
    {
        var values = new List<T>();

        for (var i = 0; i < args.Length; ++i)
        {
            var maybeName = args[i].Trim();

            if (!named.Names.Contains(maybeName))
            {
                continue;
            }

            var next = (i < (args.Length - 1)) ? args[i + 1] : string.Empty;
            var parsed = named.Parse(next);

            if (parsed != null)
            {
                values.Add(parsed);
            }
        }

        return values;
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

        Console.WriteLine(@"
Examples:
  backup -S localhost -d dbname -U sa -P password
  restore -S localhost -d dbname -U sa -P password -i my-database-backup.sql

To backup a localdb instance:
  backup -S '(LocalDb)\\MSSQLLocalDB' -d dbname

Most arguments should behave exactly like their sqlcmd counterparts.

  https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility
        ");
    }

    static SqlConnectionStringBuilder? BuildConnectionString(string[] args)
    {
        var server = GetNamedArgument(args, ARGUMENT_SERVER);
        var isLocalDb = !string.IsNullOrEmpty(server)
            && server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);

        if (!EnsureArgumentGiven(args, ARGUMENT_DATABASE, out var dbName))
        {
            return null;
        }

        var timeout = GetNamedArgument(args, ARGUMENT_TIMEOUT);
        var connStrBuilder = new SqlConnectionStringBuilder
        {
            CommandTimeout = timeout,
            ConnectTimeout = timeout,
            DataSource = server,
            Encrypt = false,
            InitialCatalog = dbName,
            IntegratedSecurity = isLocalDb,
            UserID = GetNamedArgument(args, ARGUMENT_USER),
            Password = GetNamedArgument(args, ARGUMENT_PASSWORD),
        };

        return connStrBuilder;
    }

    static async Task<int> BackupAsync(string[] args)
    {
        // dump the schema first. then dump the data separately so we can control
        // which tables we want to exclude.

        var schemaOpts = CreateScriptingParams(args);
        if (schemaOpts == null)
        {
            return 1;
        }

        schemaOpts.ScriptOptions.TypeOfDataToScript = SCRIPT_SCHEMA;

        var verbose = GetNamedArgument(args, ARGUMENT_VERBOSE);
        var schemaOnly = GetNamedArgument(args, ARGUMENT_SCHEMA_ONLY);
        var ok = await RunScriptRequestAsync(verbose, schemaOpts);

        if (!ok)
        {
            return 1;
        }

        if (schemaOnly)
        {
            Console.WriteLine($"[OK] Script written to '{schemaOpts.FilePath}'");
            return 0;
        }

        var dataOpts = CreateScriptingParams(args);
        if (dataOpts == null)
        {
            return 1;
        }
        dataOpts.ScriptOptions.TypeOfDataToScript = SCRIPT_DATA;
        dataOpts.FilePath += ".temp";

        var excludeTables = GetNamedArrayArgument(args, ARGUMENT_EXCLUDE_TABLE);

        foreach (var table in excludeTables)
        {
            dataOpts.ExcludeObjectCriteria.Add(new ScriptingObject()
            {
                Type = "Table",
                Name = table,
            });
        }

        ok = await RunScriptRequestAsync(verbose, dataOpts);

        if (!ok)
        {
            return 1;
        }

        using (var input = File.OpenRead(dataOpts.FilePath))
        {
            using (var output = File.OpenWrite(schemaOpts.FilePath))
            {
                output.Seek(0, SeekOrigin.End);

                // the output from 'SqlTools.ServiceLayer' seems to be dumped
                // in-order with no regard for foreign keys between tables. let's
                // just disable any constraints while we import the data.
                output.Write(Encoding.Unicode.GetBytes("EXEC sp_MSForEachTable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'\r\nGO\r\n"));

                // both files are written as UTF16LE and both naturally contain
                // byte order marks. we don't want the BOM from the second file,
                // as it will be appended to the first.
                var bom = new byte[2];
                var read = input.Read(bom);

                Debug.Assert(read == 2);
                Debug.Assert(bom[0] == '\xFF');
                Debug.Assert(bom[1] == '\xFE');

                input.CopyTo(output);
                output.Write(Encoding.Unicode.GetBytes("EXEC sp_MSForEachTable 'ALTER TABLE ? CHECK CONSTRAINT ALL'\r\nGO\r\n"));
            }
        }

        File.Delete(dataOpts.FilePath);
        Console.WriteLine($"[OK] Script written to '{schemaOpts.FilePath}'");

        return 0;
    }

    static async Task<int> RestoreAsync(string[] args)
    {
        if (!EnsureArgumentGiven(args, ARGUMENT_INPUT_FILE, out var inputFile))
        {
            return 1;
        }

        if (!File.Exists(inputFile))
        {
            Console.WriteLine("[ERROR] Input file '{0}' does not exist", inputFile);
            return 1;
        }

        var connStrBuilder = BuildConnectionString(args);
        if (connStrBuilder == null)
        {
            return 1;
        }
        using var conn = new SqlConnection(connStrBuilder.ConnectionString);
        await conn.OpenAsync();
        using var engine = new ExecutionEngine();
        var timeout = GetNamedArgument(args, ARGUMENT_TIMEOUT);
        var verbose = GetNamedArgument(args, ARGUMENT_VERBOSE);
        var scriptArgs = new ScriptExecutionArgs(
            string.Empty, conn, timeout, new ExecutionEngineConditions(), new BatchEventHandler()
        );
        var numExecuted = 0;
        var tstart = DateTime.UtcNow;
        var batchHandler = new BatchParser
        {
            Execute = (batchScript, repeatCount, lineNumber, sqlCmdCommand) =>
            {
                scriptArgs.Script = batchScript;
                engine.ExecuteBatch(scriptArgs);

                numExecuted += 1;
                if (numExecuted % 100 == 0 && verbose)
                {
                    Console.WriteLine("[OK] Executed {0} statements", numExecuted);
                }

                return true;
            },
            ErrorMessage = (message, messageType) =>
            {
                Console.WriteLine("[ERROR] {0}", message);
            }
        };

        using var reader = new StreamReader(inputFile);
        using var parser = new Parser(
            batchHandler,
            batchHandler,
            reader,
            Path.GetFileName(inputFile)
        );

        parser.Parse();

        if (verbose)
        {
            var elapsed = DateTime.UtcNow - tstart;
            Console.WriteLine("[OK] Executed {0} statements in {1} seconds", numExecuted, (int)elapsed.TotalSeconds);
        }

        return 0;
    }

    static ScriptingParams? CreateScriptingParams(string[] args)
    {
        var connStrBuilder = BuildConnectionString(args);
        if (connStrBuilder == null)
        {
            return null;
        }

        var outfile = GetNamedArgument(args, ARGUMENT_OUTPUT_FILE);

        if (string.IsNullOrEmpty(outfile))
        {
            var dt = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
            outfile = $"{connStrBuilder.InitialCatalog}-{dt}.sql";
        }

        // the 'FilePath' property must contain a directory when given to the scripting service.
        // if none is given we just default to the current directory.
        if (string.IsNullOrEmpty(Path.GetDirectoryName(outfile)))
        {
            outfile = Path.Join(Directory.GetCurrentDirectory(), outfile);
        }

        var opts = new ScriptingParams
        {
            ConnectionString = connStrBuilder.ConnectionString,
            ExcludeTypes = new List<string>()
            {
                // make sure we don't include any 'CREATE DATABASE' queries in the dump.
                // https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.management.smo.databaseobjecttypes?view=sql-smo-160
                "Database",
                "User",
            },
            FilePath = outfile,
            Operation = ScriptingOperationType.Create,
            ScriptDestination = SCRIPT_DESTINATION_FILE,
            ScriptOptions = new ScriptOptions
            {
                ContinueScriptingOnError = false,
                IncludeDescriptiveHeaders = false,
                ScriptFullTextIndexes = true,
                ScriptForeignKeys = true,
                ScriptIndexes = true,
                ScriptUseDatabase = false,
                TargetDatabaseEngineEdition = "SqlServerExpressEdition",
                UniqueKeys = true,

                // 'TypeOfDataToScript' is set outside.
            },
            ExcludeObjectCriteria = new List<ScriptingObject>(),
        };

        return opts;
    }

    static bool EnsureArgumentGiven<T>(string[] args, Argument<T> argument, out T? value)
    {
        if (!argument.Required)
        {
            value = argument.Default;
            return true;
        }

        var given = GetNamedArgument(args, argument);

        if (given == null || given.Equals(argument.Default))
        {
            Console.WriteLine("[ERROR] argument '{0}' was not given", argument.Names[0]);
            value = default(T);
            return false;
        }

        value = given;
        return true;
    }

    static Task<bool> RunScriptRequestAsync(bool verbose, ScriptingParams opts)
    {
        var taskCompletion = new TaskCompletionSource<bool>(false);
        var operation = new ScriptingScriptOperation(opts, null);

        operation.ProgressNotification += (sender, args) =>
        {
            if (verbose)
            {
                Console.WriteLine("[{0}/{1}] {2}, {3}, {4}", args.CompletedCount, args.TotalCount, args.Status, args.ScriptingObject.Type, args.ScriptingObject);
            }
            if (!string.IsNullOrEmpty(args.ErrorMessage))
            {
                Console.WriteLine("{0}", args.ErrorMessage);
                Console.WriteLine("{0}", args.ErrorDetails);
            }
        };
        operation.CompleteNotification += (sender, args) =>
        {
            if (args.HasError)
            {
                Console.WriteLine("{0}", args.ErrorMessage);
                Console.WriteLine("{0}", args.ErrorDetails);
                taskCompletion.SetResult(false);
            }
            else
            {
                taskCompletion.SetResult(true);
            }
        };
        operation.Execute();

        return taskCompletion.Task;
    }

    class BatchEventHandler : IBatchEventsHandler
    {
        public SqlError? Error { get; private set; }

        public void OnBatchError(object sender, BatchErrorEventArgs args)
        {
            Console.WriteLine("{0}", args.Message);
            Error = args.Error;
        }

        public void OnBatchMessage(object sender, BatchMessageEventArgs args)
        {
            Console.WriteLine("{0}", args.Message);
        }

        public void OnBatchResultSetProcessing(object sender, BatchResultSetEventArgs args) { }
        public void OnBatchResultSetFinished(object sender, EventArgs args) { }
        public void OnBatchCancelling(object sender, EventArgs args) { }
    }
}
