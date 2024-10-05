using System;
using System.Threading.Tasks;

namespace Kent.DbCli;

public interface ICommand
{
    IArgument[] AcceptsArguments { get; }
    Task<int> ExecuteAsync(string[] args);
}
