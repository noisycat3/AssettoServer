using System;
using AssettoServer.Network.Tcp;
using AssettoServer.Shared.Network.Packets.Outgoing;
using NodaTime;

namespace AssettoServer.Server.Weather.Implementation;

public class WeatherFxV1Implementation : IWeatherImplementation
{
    private readonly Lazy<ACServer> _acServer;

    public WeatherFxV1Implementation(Lazy<ACServer> acServer, CSPFeatureManager cspFeatureManager)
    {
        _acServer = acServer;
        cspFeatureManager.Add(new CSPFeature { Name = "WEATHERFX_V1", Mandatory = true });
    }

    public void SendWeather(WeatherData weather, ZonedDateTime dateTime, ACTcpClient? client = null)
    {
        if (!_acServer.IsValueCreated)
            return;

        var newWeather = new CSPWeatherUpdate
        {
            UnixTimestamp = (ulong) dateTime.ToInstant().ToUnixTimeSeconds(),
            WeatherType = (byte) weather.Type.WeatherFxType,
            UpcomingWeatherType = (byte) weather.UpcomingType.WeatherFxType,
            TransitionValue = weather.TransitionValue,
            TemperatureAmbient = (Half) weather.TemperatureAmbient,
            TemperatureRoad = (Half) weather.TemperatureRoad,
            TrackGrip = (Half) weather.TrackGrip,
            WindDirectionDeg = (Half) weather.WindDirection,
            WindSpeed = (Half) weather.WindSpeed,
            Humidity = (Half) weather.Humidity,
            Pressure = (Half) weather.Pressure,
            RainIntensity = (Half) weather.RainIntensity,
            RainWetness = (Half) weather.RainWetness,
            RainWater = (Half) weather.RainWater
        };

        if (client == null)
        {
            _acServer.Value.BroadcastPacketUdp(in newWeather);
        }
        else
        {
            client.SendPacketUdp(in newWeather);
        }
    }
}
