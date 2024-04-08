using System;

namespace Kent.DbCli;

public interface IArgument
{
    public string[] Names { get; }
    public string Description { get; }
}