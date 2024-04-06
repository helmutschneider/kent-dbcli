using System;
using System.Threading.Tasks;
using Microsoft.SqlTools.Hosting.Protocol;
using Microsoft.SqlTools.Hosting.Protocol.Contracts;
using Microsoft.SqlTools.ServiceLayer.Scripting.Contracts;

namespace Kent.DbCli;

class ScriptContext : RequestContext<ScriptingResult>
{
    public ScriptStatus Status { get; private set; } = ScriptStatus.Working;
    public string Output { get; private set; } = string.Empty;

    public ScriptContext() : base()
    {
    }

    public override Task SendEvent<TParams>(EventType<TParams> eventType, TParams eventParams)
    {
        if (eventParams is ScriptingProgressNotificationParams p && p.Status == "Completed")
        {
            Console.WriteLine($"[{p.CompletedCount}/{p.TotalCount}] {p.ScriptingObject}");
        }
        return Task.CompletedTask;
    }

    public override Task SendError(Exception e)
    {
        Status = ScriptStatus.Error;
        Output = e.ToString();
        return Task.CompletedTask;
    }

    public override Task SendError(string errorMessage, int errorCode = 0, string? data = null)
    {
        Status = ScriptStatus.Error;
        Output = $"{errorMessage} {errorCode} {data}";
        return Task.CompletedTask;
    }

    public override Task SendResult(ScriptingResult resultDetails)
    {
        Status = ScriptStatus.Ok;
        Output = resultDetails.Script;
        return Task.CompletedTask;
    }
}