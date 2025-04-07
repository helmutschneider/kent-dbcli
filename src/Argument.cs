using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kent.DbCli;

public class Argument<T> : IArgument
{
    public string[] Names { get; }
    public string Description { get; init; } = string.Empty;
    public bool IsRequired { get; init; } = false;
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
            case Argument<Bytes>:
                if (!valueLooksLikeArgument && TryParseBytes(value, out var b))
                {
                    return (T)(object)b;
                }
                break;
        }

        return this.Default;
    }

    public static bool TryParseBytes(string value, out Bytes parsed)
    {
        var match = Regex.Match(value, @"(\d+)\s*([a-z])?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            parsed = null!;
            return false;
        }

        var num = int.Parse(match.Groups[1].Value);
        var multipliter = 1;

        if (match.Groups.Count == 3)
        {
            multipliter = match.Groups[2].Value.ToLower() switch
            {
                "g" => 1_000_000_000,
                "m" => 1_000_000,
                "k" => 1_000,
                "b" => 1,
                _ => 1,
            };
        }

        parsed = new Bytes(value, num * multipliter);

        return true;
    }
}
