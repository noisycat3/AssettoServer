﻿using System.Drawing;
using AssettoServer.Commands;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Discord;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Network.Packets.Shared;
using CSharpDiscordWebhook.NET.Discord;
using Serilog;

namespace DiscordAuditPlugin;

public class Discord
{
    private readonly string _serverNameSanitized;

    private readonly DiscordConfiguration _configuration;

    private DiscordWebhook? AuditHook { get; }
    private DiscordWebhook? ChatHook { get; }

    public Discord(DiscordConfiguration configuration, IACServer server, ACServerConfiguration serverConfiguration, ChatService chatService)
    {
        _serverNameSanitized = DiscordUtils.SanitizeUsername(serverConfiguration.Server.Name);
        _configuration = configuration;

        if (!string.IsNullOrEmpty(_configuration.AuditUrl))
        {
            AuditHook = new DiscordWebhook
            {
                Uri = new Uri(_configuration.AuditUrl)
            };

            server.ClientKicked += OnClientKicked;
            server.ClientBanned += OnClientBanned;
        }
        
        if (!string.IsNullOrEmpty(_configuration.ChatUrl))
        {
            ChatHook = new DiscordWebhook
            {
                Uri = new Uri(_configuration.ChatUrl)
            };

            server.ClientConnected += (_, args) =>
            {
                args.Client.ChatMessageEvent += OnChatMessageReceived;
            };

            server.ClientDisconnected += (_, args) =>
            {
                args.Client.ChatMessageEvent -= OnChatMessageReceived;
            };
        }
    }

    private void OnClientBanned(IACServer server, ClientAuditEventArgs args)
    {
        Task.Run(async () =>
        {
            try
            {
                IClient client = args.Client;
                await AuditHook!.SendAsync(PrepareAuditMessage(
                    ":hammer: Ban alert",
                    _serverNameSanitized,
                    client.Guid, 
                    client.Name,
                    args.ReasonStr,
                    Color.Red,
                    args.Admin?.Name
                ));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in Discord webhook");
            }
        });
    }

    private void OnClientKicked(IACServer server, ClientAuditEventArgs args)
    {
        if (args.Reason != KickReason.ChecksumFailed)
        {
            IClient client = args.Client;
            Task.Run(async () =>
            {
                try
                {
                    await AuditHook!.SendAsync(PrepareAuditMessage(
                        ":boot: Kick alert",
                        _serverNameSanitized,
                        client.Guid, 
                        client.Name,
                        args.ReasonStr,
                        Color.Yellow,
                        args.Admin?.Name
                    ));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in Discord webhook");
                }
            });
        }
    }

    private void OnChatMessageReceived(IClient sender, ChatMessageEventArgs argsM)
    {
        ChatMessage args = argsM.ChatMessage;
        if (args.Message.StartsWith("\t\t\t\t$CSP0:")
            || string.IsNullOrWhiteSpace(args.Message)
            || _configuration.ChatIgnoreGuids.Contains(sender.Guid)) 
            return;
        
        string username;
        string content;

        if (_configuration.ChatMessageIncludeServerName)
        {
            username = _serverNameSanitized;
            content = $"**{sender.Name}:** {DiscordUtils.Sanitize(args.Message)}";
        }
        else
        {
            username = DiscordUtils.SanitizeUsername(sender.Name) ?? throw new InvalidOperationException("ACTcpClient has no name set");
            content = DiscordUtils.Sanitize(args.Message);
        }

        DiscordMessage msg = new DiscordMessage
        {
            AvatarUrl = _configuration.PictureUrl,
            Username = username,
            Content = content,
            AllowedMentions = new AllowedMentions()
        };

        ChatHook!.SendAsync(msg)
            .ContinueWith(t => Log.Error(t.Exception, "Error in Discord webhook"), TaskContinuationOptions.OnlyOnFaulted);
    }

    private DiscordMessage PrepareAuditMessage(
        string title,
        string serverName,
        ulong clientGuid,
        string? clientName,
        string? reason,
        Color color,
        string? adminName
    )
    {
        string userSteamUrl = "https://steamcommunity.com/profiles/" + clientGuid;
        DiscordMessage message = new DiscordMessage
        {
            Username = DiscordUtils.SanitizeUsername(serverName),
            AvatarUrl = _configuration.PictureUrl,
            Embeds = new List<DiscordEmbed>
            {
                new()
                {
                    Title = title,
                    Color = color,
                    Fields = new List<EmbedField>
                    {
                        new() { Name = "Name", Value = DiscordUtils.Sanitize(clientName), InLine = true },
                        new() { Name = "Steam-GUID", Value = clientGuid + " ([link](" + userSteamUrl + "))", InLine = true }
                    }
                }
            },
            AllowedMentions = new AllowedMentions()
        };

        if (adminName != null)
            message.Embeds[0].Fields.Add(new EmbedField { Name = "By Admin", Value = DiscordUtils.Sanitize(adminName), InLine = true });

        if (reason != null)
            message.Embeds[0].Fields.Add(new EmbedField { Name = "Message", Value = DiscordUtils.Sanitize(reason) });

        return message;
    }
}
