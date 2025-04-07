using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlTools.ServiceLayer.Scripting;
using Microsoft.SqlTools.SqlCore.Scripting;
using Microsoft.SqlTools.SqlCore.Scripting.Contracts;

namespace Kent.DbCli;

public class BackupCommand : ICommand
{
    const string SCRIPT_DESTINATION_FILE = "ToSingleFile";
    const string SCRIPT_DESTINATION_EDITOR = "ToEditor";
    const string SCRIPT_SCHEMA = "SchemaOnly";
    const string SCRIPT_SCHEMA_AND_DATA = "SchemaAndData";
    const string SCRIPT_DATA = "DataOnly";

    public IArgument[] AcceptsArguments { get; } = new IArgument[]
    {
        Arguments.SERVER,
        Arguments.DATABASE,
        Arguments.USER,
        Arguments.PASSWORD,
        Arguments.OUTPUT_FILE,
        Arguments.TIMEOUT,
        Arguments.EXCLUDE_TABLE,
        Arguments.SCHEMA_ONLY,
    };

    public async Task<int> ExecuteAsync(string[] args)
    {
        // dump the schema first. then dump the data separately so we can control
        // which tables we want to exclude.

        var schemaOpts = CreateScriptingParams(args);
        if (schemaOpts == null)
        {
            return 1;
        }

        schemaOpts.ScriptOptions.TypeOfDataToScript = SCRIPT_SCHEMA;
        var schemaOnly = Arguments.SCHEMA_ONLY.GetOrDefault(args);
        var ok = await RunScriptRequestAsync(schemaOpts);

        if (!ok)
        {
            return 1;
        }

        if (schemaOnly)
        {
            Console.WriteLine($"OK: script written to '{schemaOpts.FilePath}'");
            return 0;
        }

        var dataOpts = CreateScriptingParams(args);
        if (dataOpts == null)
        {
            return 1;
        }
        dataOpts.ScriptOptions.TypeOfDataToScript = SCRIPT_DATA;
        dataOpts.FilePath += ".temp";

        var excludeTables = Arguments.EXCLUDE_TABLE.GetArray(args);

        foreach (var table in excludeTables)
        {
            dataOpts.ExcludeObjectCriteria.Add(new ScriptingObject()
            {
                Type = "Table",
                Name = table,
            });
        }

        ok = await RunScriptRequestAsync(dataOpts);

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
        Console.WriteLine($"OK: script written to '{schemaOpts.FilePath}'");

        return 0;
    }

    static ScriptingParams? CreateScriptingParams(string[] args)
    {
        var connStrBuilder = Arguments.BuildConnectionString(args);
        if (connStrBuilder == null)
        {
            return null;
        }

        var outfile = Arguments.OUTPUT_FILE.GetOrDefault(args);

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

    static Task<bool> RunScriptRequestAsync(ScriptingParams opts)
    {
        var taskCompletion = new TaskCompletionSource<bool>(false);
        var operation = new ScriptingScriptOperation(opts, null);

        operation.ProgressNotification += (sender, args) =>
        {
            Console.WriteLine("{0}/{1}: {2}, {3}, {4}", args.CompletedCount, args.TotalCount, args.Status, args.ScriptingObject.Type, args.ScriptingObject);
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
}
