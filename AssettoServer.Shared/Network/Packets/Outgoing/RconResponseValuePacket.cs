﻿using System.Text;
using AssettoServer.Shared.Model;

namespace AssettoServer.Shared.Network.Packets.Outgoing;

public class RconResponseValuePacket : IOutgoingNetworkPacket
{
    public int RequestId { get; init; }
    public string Body { get; init; } = "";
    
    public void ToWriter(ref PacketWriter writer)
    {
        writer.Write(RequestId);
        writer.Write(RconProtocolOut.ResponseValue);
        if (string.IsNullOrEmpty(Body))
            writer.Write<byte>(0);
        else
        {
           writer.WriteStringFixed(Body, Encoding.ASCII, 1000, false);
           writer.Write<byte>(0);
        }
    }
}
