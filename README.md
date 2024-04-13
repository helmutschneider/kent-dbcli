# Kent SQL Server database cli
Kent.DbCli is a cross-platform command line utility for backing up an SQL Server database.
It outputs an SQL-file similar to `pg_dump` or `mysqldump`.

## Usage
```shell
Usage:
  dump-schema
  dump-database

Arguments:
  -h, --host                       Database host.
  -d, --database                   Database name.
  -u, --user
  -p, --password
  -c, --connection-string          Raw connection string. Overrides the other connection arguments.
  -o, --output                     Path to write the output to.
  --verbose                        Print progress messages from SQL Tools Service.
  --exclude-table                  Exclude data from a table. Only applies to 'dump-database'. May be specified multiple times.
```

To dump the schema only:
```shell
./Kent.DbCli dump-schema -h localhost -d dbname -u sa -p password
```

To dump the schema and data:
```shell
./Kent.DbCli dump-database -h localhost -d dbname -u sa -p password
```

To dump the schema and data, excluding the data from a table:
```shell
./Kent.DbCli dump-database -h localhost -d dbname -u sa -p password --exclude-table TableName
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
