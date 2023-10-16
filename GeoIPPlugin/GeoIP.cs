using System.Net;
using AssettoServer.Shared.Model;
using MaxMind.GeoIP2;

namespace GeoIPPlugin;

public class GeoIP
{
    private readonly DatabaseReader _database;

    public GeoIP(IACServer server, GeoIPConfiguration configuration)
    {
        _database = new DatabaseReader(configuration.DatabasePath);
        server.ClientConnected += OnClientConnected;
    }

    private void OnClientConnected(IACServer server, ClientConnectionEventArgs args)
    {
        IClient sender = args.Client;

        try
        {
            if(sender.RemoteAddress is IPEndPoint endpoint && _database.TryCity(endpoint.Address, out var response))
            {
                sender.Logger.Information("GeoIP results for {ClientName}: {Country} ({CountryCode}) [{Lat},{Lon}]", sender.Name, response!.Country.Name, response.Country.IsoCode, response.Location.Latitude, response.Location.Longitude);
            }
            else
            {
                sender.Logger.Warning("Could not get GeoIP lookup result for {ClientName}", sender.Name);
            }
        }
        catch (Exception ex)
        {
            sender.Logger.Error(ex, "Error during GeoIP lookup for {ClientName}", sender.Name);
        }
    }
}
