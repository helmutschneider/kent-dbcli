using System;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using Microsoft.SqlTools.Hosting.Protocol;
using Microsoft.SqlTools.Hosting.Protocol.Contracts;
using Microsoft.SqlTools.Hosting.Protocol.Serializers;
using Microsoft.SqlTools.ServiceLayer.Scripting.Contracts;

namespace Kent.DbCli;

public class ScriptContext : RequestContext<ScriptingResult>
{
    public ScriptStatus Status { get; private set; } = ScriptStatus.InProgress;
    public string Output { get; private set; } = string.Empty;

    readonly bool _verbose;

    public ScriptContext(bool verbose) : base(_message, new MessageWriter(Stream.Null, new JsonRpcMessageSerializer()))
    {
        _verbose = verbose;
    }

    // if we use 'Message.Unknown()' the JSON serializer throws an error due to the null 'Id'. 
    static readonly Message _message = new Message
    {
        MessageType = MessageType.Unknown,
        Id = string.Empty,
        Method = string.Empty,
    };

    public override Task SendEvent<TParams>(EventType<TParams> eventType, TParams eventParams)
    {
        switch (eventParams)
        {
            case ScriptingProgressNotificationParams progress:
                if (_verbose)
                {
                    WriteMessage("[{0}/{1}] {2}, {3}, {4}", progress.CompletedCount, progress.TotalCount, progress.Status, progress.ScriptingObject.Type, progress.ScriptingObject);
                }
                break;
            case ScriptingCompleteParams complete:
                if (complete.Success)
                {
                    Status = ScriptStatus.Success;
                }
                else
                {
                    Status = ScriptStatus.Error;
                    WriteMessage(string.Join("\n", complete.ErrorMessage, complete.ErrorDetails));
                }
                break;
        }
        return base.SendEvent(eventType, eventParams);
    }

    public override Task SendError(string errorMessage, int errorCode = 0, string? data = null)
    {
        Status = ScriptStatus.Error;
        WriteMessage($"{errorMessage} {errorCode} {data}");
        return base.SendError(errorMessage, errorCode, data);
    }

    public override Task SendResult(ScriptingResult resultDetails)
    {
        /// <see cref="Program.ARGUMENT_STDOUT"/>
        Output = resultDetails.Script ?? string.Empty;

        return base.SendResult(resultDetails);
    }

    public virtual void WriteMessage(string message, params object?[] args)
    {
        Console.WriteLine(message, args);
    }
}
