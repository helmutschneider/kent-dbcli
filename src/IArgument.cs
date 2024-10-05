using System;

namespace Kent.DbCli;

public interface IArgument
{
    string[] Names { get; }
    string Description { get; }
    string? GetDefaultAsString();
}
