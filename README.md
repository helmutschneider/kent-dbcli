# Kent MSSQL database cli
This is a simple CLI utility to dump an MSSQL database to an SQL file. It mimics the 'Generate Script' tool in SSMS but has some sensible defaults, like including indexes. It calls into [sqltoolsservice](https://github.com/microsoft/sqltoolsservice) directly and the only hard dependency is .NET 6.0.

## Usage
```
dotnet run dump-schema 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'
dotnet run dump-database 'Data Source=localhost;Initial Catalog=dbname;User ID=sa;Password=password'
```
