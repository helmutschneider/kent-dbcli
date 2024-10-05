using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace Kent.DbCli;

public static class Arguments
{
    // let's keep these argument names vaguely similar to 'sqlcmd'...
    // https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility

    public static readonly Argument<string> SERVER = new("-S", "--server")
    {
        Description = "Database host",
        Default = "localhost",
    };

    public static readonly Argument<string> DATABASE = new("-d", "--database-name")
    {
        Description = "Database name",
        Required = true,
    };

    public static readonly Argument<string> USER = new("-U", "--user-name")
    {
        Description = "Database username",
        Default = "sa",
    };

    public static readonly Argument<string> PASSWORD = new("-P", "--password")
    {
        Description = "Database password",
        Default = string.Empty,
    };

    public static readonly Argument<string> INPUT_FILE = new("-i", "--input-file")
    {
        Description = "Input script path",
        Required = true,
    };

    public static readonly Argument<string> OUTPUT_FILE = new("-o", "--output-file")
    {
        Description = "Output script path",
    };

    public static readonly Argument<string> EXCLUDE_TABLE = new("--exclude-table")
    {
        Description = "Exclude data from a table. May be specified multiple times",
    };

    public static readonly Argument<bool> SCHEMA_ONLY = new("--schema-only")
    {
        Description = "Export the database schema without including table data",
    };
    
    public static readonly Argument<int> TIMEOUT = new("-t", "--query-timeout")
    {
        Description = "Query timeout for individual statements",
        Default = 10,
    };
    
    public static readonly Argument<int> BATCH_SIZE = new("--batch-size")
    {
        Description = "Transaction batch size for query execution",
        Default = 100,
    };

    public static SqlConnectionStringBuilder? BuildConnectionString(string[] args)
    {
        var server = SERVER.GetOrDefault(args);
        if (!DATABASE.TryGet(args, out var dbName))
        {
            return null;
        }

        var isLocalDb = !string.IsNullOrEmpty(server)
            && server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);

        var timeout = TIMEOUT.GetOrDefault(args);
        var connStrBuilder = new SqlConnectionStringBuilder
        {
            CommandTimeout = timeout,
            ConnectTimeout = timeout,
            DataSource = server,
            Encrypt = false,
            InitialCatalog = dbName,
            IntegratedSecurity = isLocalDb,
            UserID = USER.GetOrDefault(args),
            Password = PASSWORD.GetOrDefault(args),
        };

        return connStrBuilder;
    }
}
