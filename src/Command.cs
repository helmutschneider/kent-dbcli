using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kent.DbCli;

public class Command
{
    public string Name { get; init; } = string.Empty;
    public CommandFn Action { get; init; } = (args) => Task.FromResult(0);
    public List<IArgument> Arguments { get; } = new();

    public delegate Task<int> CommandFn(string[] args);
}
