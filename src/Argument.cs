using System;

namespace Kent.DbCli;

public class Argument<T> : IArgument
{
    public string[] Names { get; }
    public string Description { get; init; } = string.Empty;
    public T? Default { get; init; } = default(T);

    public Argument(string name, params string[] names)
    {
        this.Names = new string[1 + names.Length];
        this.Names[0] = name;
        
        Array.Copy(names, 0, this.Names, 1, names.Length);
    }
}
