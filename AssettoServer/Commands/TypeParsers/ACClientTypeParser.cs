using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Qmmands;
using System;
using System.Threading.Tasks;

namespace AssettoServer.Commands.TypeParsers;

public class ACClientTypeParser : TypeParser<ACTcpClient>
{
    private readonly Lazy<EntryCarManager> _entryCarManager;
    private EntryCarManager CarManager => _entryCarManager.Value;

    public ACClientTypeParser(Lazy<EntryCarManager> entryCarManager)
    {
        _entryCarManager = entryCarManager;
    }

    public override ValueTask<TypeParserResult<ACTcpClient>> ParseAsync(Parameter parameter, string value, CommandContext context)
    {
        if (context is ACCommandContext acContext)
        {
            //if (int.TryParse(value, out int result) && _entryCarManager.ConnectedCars.TryGetValue(result, out EntryCar? car) && car.Client != null)
            if (int.TryParse(value, out int result) && CarManager.EntryCars[result] is { Client: ACTcpClient clientById })
                return TypeParserResult<ACTcpClient>.Successful(clientById);

            ACTcpClient? exactMatch = null;
            ACTcpClient? ignoreCaseMatch = null;
            ACTcpClient? containsMatch = null;
            ACTcpClient? ignoreCaseContainsMatch = null;

            if (value.StartsWith("@"))
                value = value.Substring(1);

            foreach (EntryCarClient clientCar in CarManager.ClientCars)
            {
                // Client has a name and specific type
                if (clientCar.Client is ACTcpClient { Name: { } } client)
                {
                    if (client.Name == value)
                    {
                        exactMatch = client;
                        break;
                    }

                    if (client.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                        ignoreCaseMatch = client;
                    else if (client.Name.Contains(value) && (containsMatch == null || containsMatch.Name?.Length > client.Name.Length))
                        containsMatch = client;
                    else if (client.Name.Contains(value, StringComparison.OrdinalIgnoreCase) && (ignoreCaseContainsMatch == null || ignoreCaseContainsMatch.Name?.Length > client.Name.Length))
                        ignoreCaseContainsMatch = client;
                }
            }

            ACTcpClient? bestMatch = null;
            if (exactMatch != null)
                bestMatch = exactMatch;
            else if (ignoreCaseMatch != null)
                bestMatch = ignoreCaseMatch;
            else if (containsMatch != null)
                bestMatch = containsMatch;
            else if (ignoreCaseContainsMatch != null)
                bestMatch = ignoreCaseContainsMatch;

            if (bestMatch != null)
                return TypeParserResult<ACTcpClient>.Successful(bestMatch);
        }

        return ValueTask.FromResult(TypeParserResult<ACTcpClient>.Failed("This player is not connected."));
    }
}
