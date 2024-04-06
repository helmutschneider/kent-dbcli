# Kent SQL Server database cli
Kent.DbCli is a native .NET command line utility to dump an SQL Server database to a file. It
mimics the "Generate Scripts" tool in SSMS but has some sensible defaults, like including indexes.

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
  -h, --host               database host
  -d, --database           database name
  -u, --user               user
  -p, --password           password
  -c, --connection-string  raw connection string
  -o, --out-file           out file
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
