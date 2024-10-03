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

public class Program
{
    // let's keep these argument names vaguely similar to 'sqlcmd'...
    // https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility

    static readonly Argument<string> ARGUMENT_SERVER = new("-S", "--server")
    {
        Description = "Database host.",
        Default = "localhost",
    };
    static readonly Argument<string> ARGUMENT_DATABASE = new("-d", "--database-name")
    {
        Description = "Database name.",
    };
    static readonly Argument<string> ARGUMENT_USER = new("-U", "--user-name")
    {
        Description = "Database username.",
        Default = string.Empty,
    };
    static readonly Argument<string> ARGUMENT_PASSWORD = new("-P", "--password")
    {
        Description = "Database password.",
        Default = string.Empty,
    };
    static readonly Argument<string> ARGUMENT_OUT_FILE = new("-o", "--output-file")
    {
        Description = "Path to write the output to.",
    };
    static readonly Argument<bool> ARGUMENT_VERBOSE = new("--verbose")
    {
        Description = "Print progress messages from SQL Tools Service.",
        Default = false,
    };
    static readonly Argument<string> ARGUMENT_EXCLUDE_TABLE = new("--exclude-table")
    {
        Description = "Exclude data from a table. May be specified multiple times.",
    };
    static readonly Argument<bool> ARGUMENT_SCHEMA_ONLY = new("--schema-only")
    {
        Description = "Only backup the database schema, eg. no data.",
    };
    static readonly IArgument[] ARGUMENTS = new IArgument[] {
        ARGUMENT_SERVER,
        ARGUMENT_DATABASE,
        ARGUMENT_USER,
        ARGUMENT_PASSWORD,
        ARGUMENT_OUT_FILE,
        ARGUMENT_VERBOSE,
        ARGUMENT_EXCLUDE_TABLE,
        ARGUMENT_SCHEMA_ONLY,
    };

    static readonly Dictionary<string, CommandFn> _commands = new()
    {
        {"backup", BackupAsync},

        // TODO: implement!
        // {"restore", RestoreAsync},
    };

    const string SCRIPT_DESTINATION_FILE = "ToSingleFile";
    const string SCRIPT_DESTINATION_EDITOR = "ToEditor";
    const string SCRIPT_SCHEMA = "SchemaOnly";
    const string SCRIPT_SCHEMA_AND_DATA = "SchemaAndData";
    const string SCRIPT_DATA = "DataOnly";

    static async Task Main(string[] args)
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

The 'backup' command assumes localhost, so this works too:
  backup -d dbname -U sa -P password

To backup a localdb instance:
  backup -S '(LocalDb)\\MSSQLLocalDB' -d dbname

Most arguments should behave exactly like their sqlcmd counterparts.

  https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility
        ");
    }

    static async Task<int> BackupAsync(string[] args)
    {
        // dump the schema first. then dump the data separately so we can control
        // which tables we want to exclude.

        var schemaOpts = CreateScriptingParams(args);
        if (schemaOpts == null)
        {
            Usage();
            return 1;
        }

        schemaOpts.ScriptOptions.TypeOfDataToScript = SCRIPT_SCHEMA;

        var verbose = GetNamedArgument(args, ARGUMENT_VERBOSE);
        var schemaOnly = GetNamedArgument(args, ARGUMENT_SCHEMA_ONLY);
        var status = await RunScriptRequestAsync(verbose, schemaOpts);

        if (status != ScriptStatus.Success)
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
            Usage();
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

        status = await RunScriptRequestAsync(verbose, dataOpts);

        if (status != ScriptStatus.Success)
        {
            return 1;
        }

        using (var input = File.OpenRead(dataOpts.FilePath))
        {
            using (var output = File.OpenWrite(schemaOpts.FilePath))
            {
                output.Seek(0, SeekOrigin.End);

                // both files are written as UTF16LE and both naturally contain
                // byte order marks. we don't want the BOM from the second file,
                // as it will be appended to the first.
                var bom = new byte[2];
                var read = input.Read(bom);

                Debug.Assert(read == 2);
                Debug.Assert(bom[0] == '\xFF');
                Debug.Assert(bom[1] == '\xFE');

                input.CopyTo(output);
            }
        }

        File.Delete(dataOpts.FilePath);
        Console.WriteLine($"[OK] Script written to '{schemaOpts.FilePath}'");

        return 0;
    }

    static ScriptingParams? CreateScriptingParams(string[] args)
    {
        var server = GetNamedArgument(args, ARGUMENT_SERVER);
        var isLocalDb = !string.IsNullOrEmpty(server)
            && server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);
        var connStrBuilder = new SqlConnectionStringBuilder
        {
            CommandTimeout = 10,
            ConnectTimeout = 10,
            DataSource = server,
            Encrypt = false,
            InitialCatalog = GetNamedArgument(args, ARGUMENT_DATABASE),
            IntegratedSecurity = isLocalDb,
            UserID = GetNamedArgument(args, ARGUMENT_USER),
            Password = GetNamedArgument(args, ARGUMENT_PASSWORD),
        };

        var outfile = GetNamedArgument(args, ARGUMENT_OUT_FILE);

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

    static async Task<ScriptStatus> RunScriptRequestAsync(bool verbose, ScriptingParams opts)
    {
        var ctx = new ScriptContext(verbose);

        using (var scripting = new ScriptingService())
        {
            await scripting.HandleScriptExecuteRequest(opts, ctx);
            while (ctx.Status == ScriptStatus.InProgress)
            {
                await Task.Delay(250);
            }
        }

        return ctx.Status;
    }
}
