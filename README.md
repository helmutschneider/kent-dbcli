# Kent SQL Server database cli
Kent.DbCli is a cross-platform command line utility for backing up an SQL Server database.
It outputs an SQL-file similar to `pg_dump` or `mysqldump`.

## Usage
```shell
Usage:
  backup
    -S, --server             = localhost      Database host
    -d, --database-name                       Database name
    -U, --user-name          = sa             Database username
    -P, --password                            Database password
    -o, --output-file                         Output script path
    -t, --query-timeout      = 10             Query timeout for individual statements
    --exclude-table                           Exclude data from a table. May be specified multiple times
    --schema-only            = False          Export the database schema without including table data

  restore
    -S, --server             = localhost      Database host
    -d, --database-name                       Database name
    -U, --user-name          = sa             Database username
    -P, --password                            Database password
    -i, --input-file                          Input script path
    -t, --query-timeout      = 10             Query timeout for individual statements
    --batch-size             = 100            Transaction batch size for query execution
```

## Background

Over the years I have gotten increasingly frustrated with the tooling-situation around
SQL Server. To make a complete backup you basically have three options:

1) Issue a manual `BACKUP DATABASE` query.
2) Pay money for a reliable 3rd-party tool.
3) Use the "Generate Scripts" tool in SSMS.

This CLI tool mimics the 3rd option, but is cross-platform, can be automated and has sensible
defaults (like including indices). The output is a nicely formatted SQL file that compresses well
with gzip, for example.

Communication with SQL Server is done using [sqltoolsservice](https://github.com/microsoft/sqltoolsservice),
version 4.5.0.15. Binaries for win-x64, osx-arm64 and linux-x64 are bundled. There are no external dependencies
besides .NET 6.0.

The following objects are included in the dump:
- schemas
- tables
- unique keys
- indexes
- foreign keys
- table data, see `dump-database`
