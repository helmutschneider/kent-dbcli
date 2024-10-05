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

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 1;
        }
        var name = args[0];
        var cmd = _commands.GetValueOrDefault(name);
        var needsHelp = args.Length == 0
            || (args.Length >= 1 && "help".Equals(args[0], StringComparison.OrdinalIgnoreCase))
            || (args.Length >= 1 && "--help".Equals(args[0], StringComparison.OrdinalIgnoreCase));
            
        if (cmd == null || needsHelp)
        {
            Usage();
            return 1;
        }
        var code = await cmd.ExecuteAsync(args.Skip(1).ToArray());
        return code;
    }

    static void Usage()
    {
        Console.WriteLine("Usage:");
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
