using System;
using System.Collections.Generic;
using System.Linq;

namespace Kent.DbCli;

public class Argument<T> : IArgument
{
    public string[] Names { get; }
    public string Description { get; init; } = string.Empty;
    public bool Required { get; init; } = false;
    public T? Default { get; init; } = default(T);

    public Argument(string name, params string[] names)
    {
        this.Names = new string[1 + names.Length];
        this.Names[0] = name;

        Array.Copy(names, 0, this.Names, 1, names.Length);
    }

    public string? GetDefaultAsString()
        => this.Default?.ToString();

    public T? GetOrDefault(string[] args)
    {
        var parsed = GetArray(args);
        if (parsed.Count != 0)
        {
            return parsed[0];
        }
        return this.Default;
    }

    public IReadOnlyList<T> GetArray(string[] args)
    {
        var values = new List<T>();

        for (var i = 0; i < args.Length; ++i)
        {
            var maybeName = args[i].Trim();

            if (!this.Names.Contains(maybeName))
            {
                continue;
            }

            var next = (i < (args.Length - 1)) ? args[i + 1] : string.Empty;
            var parsed = Parse(next);

            if (parsed != null)
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    public bool TryGet(string[] args, out T? value)
    {
        if (!this.Required)
        {
            value = this.Default;
            return true;
        }

        var given = GetOrDefault(args);

        if (given == null || given.Equals(this.Default))
        {
            Console.WriteLine("[ERROR] argument '{0}' was not given", this.Names.Last());
            value = default(T);
            return false;
        }

        value = given;
        return true;
    }

    public T? Parse(string value)
    {
        var valueLooksLikeArgument = value.StartsWith("-");

        switch (this)
        {
            case Argument<bool>:
                {
                    if (valueLooksLikeArgument || string.IsNullOrEmpty(value))
                    {
                        return (T)(object)true;
                    }
                    if (bool.TryParse(value, out var parsed))
                    {
                        return (T)(object)parsed;
                    }
                    break;
                }
            case Argument<string>:
                if (!valueLooksLikeArgument && !string.IsNullOrEmpty(value))
                {
                    return (T)(object)value;
                }
                break;
            case Argument<int>:
                if (!valueLooksLikeArgument && int.TryParse(value, out var x))
                {
                    return (T)(object)x;
                }
                break;
        }

        return this.Default;
    }
}
