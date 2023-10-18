using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Generic;

namespace AssettoServer.Server;

internal abstract class EntryCarBase : IEntryCar
{
    // IEntry car interface
    public IACServer Server => _acServer;
    public byte SessionId { get; }
    public virtual string Model => _listEntry.Model;
    public virtual string Skin => _listEntry.Skin ?? string.Empty;
    public abstract bool IsAiCar { get; }
    public virtual IClient? Client => null;
    public abstract ushort Ping { get; }
    public abstract int TimeOffset { get; }
    public abstract string Name { get; }

    public abstract int InstanceCount { get; }
    public abstract int InstanceMax { get; }
    public abstract IEnumerable<ICarInstance> Instances { get; }

    public abstract ICarInstance CreateInstance();
    public abstract void DestroyInstance(ICarInstance instance);

    public abstract bool HasUpdateToSend { get; }

    // Slot configuration
    private readonly EntryList.Entry _listEntry;

    // Game references
    private readonly ACServer _acServer;
    public ACServer ACServer => _acServer;
    public ACServerConfiguration ServerConfiguration { get; }
    public SessionManager SessionManager { get; }

    // Common car entry properties
    public int Ballast { get; internal set; }
    public int Restrictor { get; internal set; }
    public DriverOptionsFlags DriverOptionsFlags { get; internal set; }

    // Logging utils
    public ILogger Logger { get; }

    private class EntryCarLogEventEnricher : ILogEventEnricher
    {
        private readonly EntryCarBase _entryCar;

        public EntryCarLogEventEnricher(EntryCarBase entryCar)
        {
            _entryCar = entryCar;
        }

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SessionId", _entryCar.SessionId));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CarModel", _entryCar.Model));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CarSkin", _entryCar.Skin));
        }
    }

    protected EntryCarBase(byte inSessionId, EntryList.Entry entry,
        ACServer acServer, ACServerConfiguration configuration, SessionManager sessionManager)
    {
        SessionId = inSessionId;
        _listEntry = entry;

        _acServer = acServer;

        ServerConfiguration = configuration;
        SessionManager = sessionManager;

        Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With(new EntryCarLogEventEnricher(this))
            .WriteTo.Logger(Log.Logger)
            .CreateLogger();
    }

    // Called to perform general update of each car
    public virtual void UpdateCar(long currentTime) { }

    // Called after car state has been sent
    public virtual void PostUpdateCar() { }

    // Called to retrieve the status for a particular car
    public virtual ICarInstance? GetBestInstanceFor(EntryCarClient clientCar) => null;
}
