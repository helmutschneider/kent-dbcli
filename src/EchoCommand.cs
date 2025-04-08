using System;
using System.Threading.Tasks;

namespace Kent.DbCli;

public class EchoCommand(string value) : ICommand
{
    public IArgument[] AcceptsArguments { get; } = [];
    public Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine(value);
        return Task.FromResult(0);
    }
}
