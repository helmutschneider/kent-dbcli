# Kent SQL Server database cli
Kent.DbCli is a native .NET command line utility for backing up an SQL Server database.

Overthe years I have gotten increasingly frustrated with the tooling-situation around
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
  -o, --out-file
```

To dump the schema only:
```shell
dotnet run --project src/Kent.DbCli.csproj -- \
  dump-schema -h localhost -d dbname -u sa -p password
```

To dump the schema and data:
```shell
dotnet run --project src/Kent.DbCli.csproj -- \
  dump-database -h localhost -d dbname -u sa -p password
```
