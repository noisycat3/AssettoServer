using System;
using System.Reflection;
using System.Threading.Tasks;
using AssettoServer.Commands.TypeParsers;
using AssettoServer.Network.Rcon;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Network.Packets.Shared;
using Qmmands;
using Serilog;

namespace AssettoServer.Commands;

internal class ChatService
{
    private readonly Func<ACTcpClient, ACCommandContext> _contextFactory;
    private readonly Func<RconClient, int, ACCommandContext> _rconContextFactory;
    private readonly CommandService _commandService = new(new CommandServiceConfiguration
    {
        DefaultRunMode = RunMode.Parallel
    });

    public ChatService(ACPluginLoader loader, ACClientTypeParser acClientTypeParser, 
        Func<ACTcpClient, ACCommandContext> contextFactory,  
        Func<RconClient, int, ACCommandContext> rconContextFactory)
    {
        _contextFactory = contextFactory;
        _rconContextFactory = rconContextFactory;

        _commandService.AddModules(Assembly.GetEntryAssembly());
        _commandService.AddTypeParser(acClientTypeParser);
        _commandService.CommandExecutionFailed += OnCommandExecutionFailed;
        _commandService.CommandExecuted += OnCommandExecuted;

        foreach (var plugin in loader.LoadedPlugins)
        { 
            _commandService.AddModules(plugin.Assembly);
        }
    }

    private ValueTask OnCommandExecuted(object? sender, CommandExecutedEventArgs args)
    {
        if (args.Context is ACCommandContext context)
        {
            context.SendRconResponse();
        }

        return ValueTask.CompletedTask;
    }

    private async Task ProcessCommandAsync(ACTcpClient client, ChatMessage message)
    {
        ACCommandContext context = _contextFactory(client);
        IResult result = await _commandService.ExecuteAsync(message.Message, context);

        if (result is ChecksFailedResult checksFailedResult)
            context.Reply(checksFailedResult.FailedChecks[0].Result.FailureReason);
        else if (result is FailedResult failedResult)
            context.Reply(failedResult.FailureReason);
    }

    public async Task ProcessCommandAsync(RconClient client, int requestId, string command)
    {
        ACCommandContext context = _rconContextFactory(client, requestId);
        IResult result = await _commandService.ExecuteAsync(command, context);

        if (result is ChecksFailedResult checksFailedResult)
        {
            context.Reply(checksFailedResult.FailedChecks[0].Result.FailureReason);
            context.SendRconResponse();
        }
        else if (result is FailedResult failedResult)
        {
            context.Reply(failedResult.FailureReason);
            context.SendRconResponse();
        }
    }

    private ValueTask OnCommandExecutionFailed(object? sender, CommandExecutionFailedEventArgs e)
    {
        if (!e.Result.IsSuccessful)
        {
            (e.Context as ACCommandContext)?.Reply("An error occurred while executing this command.");
            Log.Error(e.Result.Exception, "Command execution failed: {Reason}", e.Result.FailureReason);
        }

        return ValueTask.CompletedTask;
    }
    
    public bool ProcessChatCommand(ACTcpClient sender, ChatMessage msg)
    {
        if (!CommandUtilities.HasPrefix(msg.Message, '/', out string commandStr))
            return false;

        msg.Message = commandStr;
        _ = ProcessCommandAsync(sender, msg);
        return true;
    }
}
