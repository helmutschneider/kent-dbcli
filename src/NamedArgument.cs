using System;

namespace Kent.DbCli;

public class NamedArgument
{
    public string[] Names { get; }
    public string Description { get; init; } = string.Empty;
    public string Default { get; init; } = string.Empty;

    public NamedArgument(string name, params string[] names)
    {
        this.Names = new string[1 + names.Length];
        this.Names[0] = name;
        
        Array.Copy(names, 0, this.Names, 1, names.Length);
    }
}
