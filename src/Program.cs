using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kent.DbCli;

public class Program
{
    static readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backup"] = new BackupCommand(),
        ["restore"] = new RestoreCommand(),
    };
    static readonly IReadOnlyList<string> _usageAliases = new[] { "usage", "help", "--help" };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 1;
        }
        var name = args[0];
        var cmd = _commands.GetValueOrDefault(name);
        var needsHelp = args.Length >= 1
            && _usageAliases.Any((x) => x.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            
        if (cmd == null || needsHelp)
        {
            Usage();
            return 1;
        }
        var cmdArgs = args.Skip(1).ToArray();
        var hasRequiredArguments = true;

        foreach (var arg in cmd.AcceptsArguments)
        {
            if (arg.IsRequired && !Arguments.Exists(arg, cmdArgs))
            {
                Console.WriteLine("Error: argument '{0}' was not given", arg.Names.Last());
                hasRequiredArguments = false;
            }
        }

        if (!hasRequiredArguments)
        {
            Console.WriteLine(string.Empty);
            Usage();
            return 1;
        }

        var code = await cmd.ExecuteAsync(cmdArgs);
        return code;
    }

    static void Usage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(string.Empty);
        foreach (var (name, cmd) in _commands)
        {
            Console.WriteLine("  {0}", name);
            foreach (var arg in cmd.AcceptsArguments)
            {
                var def = arg.GetDefaultAsString() is string s && !string.IsNullOrEmpty(s) ? $"= {s}" : string.Empty;
                Console.WriteLine("    {0,-24} {1,-16} {2}", string.Join(", ", arg.Names), def, arg.Description);
            }
            Console.WriteLine(string.Empty);
        }
    }
}
