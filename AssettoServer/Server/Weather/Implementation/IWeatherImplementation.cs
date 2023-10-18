using AssettoServer.Shared.Model;
using NodaTime;

namespace AssettoServer.Server.Weather.Implementation;

public interface IWeatherImplementation
{
    public void SendWeather(WeatherData weather, ZonedDateTime dateTime, IClient client = null);
}
