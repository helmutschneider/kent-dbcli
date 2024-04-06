# Kent SQL Server database cli
This is a native .NET cli utility to dump an SQL Server database to a file. It mimics the 'Generate Script'
tool in SSMS but has some sensible defaults, like including indexes.

Communication with SQL Server is done using [sqltoolsservice](https://github.com/microsoft/sqltoolsservice),
version 4.5.0.15. Binaries for win-x64 and osx-arm64 are bundled. There are no external dependencies besides .NET 6.0.

The following objects are included in the dump:
- schemas
- tables
- unique keys
- indexes
- foreign keys
- table data, see `dump-database`

To dump the schema only:
```shell
dotnet run --project src/Kent.DbCli.csproj \
  dump-schema 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'
```

To dump the schema and data:
```shell
dotnet run --project src/Kent.DbCli.csproj \
  dump-database 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'
```
