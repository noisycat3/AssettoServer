﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Utils;
using Serilog;

namespace AssettoServer.Server;

public class ChecksumManager
{
    public IReadOnlyDictionary<string, byte[]> TrackChecksums { get; private set; } = null!;
    public IReadOnlyDictionary<string, List<byte[]>> CarChecksums { get; private set; } = null!;

    private readonly ACServerConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    
    public ChecksumManager(ACServerConfiguration configuration, EntryCarManager entryCarManager)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
    }
    
    public void Initialize()
    {
        TrackChecksums = CalculateTrackChecksums(_configuration.Server.Track, _configuration.Server.TrackConfig);
        Log.Information("Initialized {Count} track checksums", TrackChecksums.Count);

        //var carModels = _entryCarManager.EntryCars.Select(car => car.Model).Distinct().ToList();
        var carModels = _configuration.EntryList.Cars.Select(car => car.Model).Distinct();
        CarChecksums = CalculateCarChecksums(carModels, _configuration.Extra.EnableAlternativeCarChecksums);
        Log.Information("Initialized {Count} car checksums", CarChecksums.Select(car => car.Value.Count).Sum());

        var modelsWithoutChecksums = CarChecksums.Where(c => c.Value.Count == 0).Select(c => c.Key).ToList();
        if (modelsWithoutChecksums.Count > 0)
        {
            string models = string.Join(", ", modelsWithoutChecksums);

            if (_configuration.Extra.IgnoreConfigurationErrors.MissingCarChecksums)
            {
                Log.Warning("No data.acd found for {CarModels}. This will allow players to cheat using modified data. More info: https://assettoserver.org/docs/common-configuration-errors#missing-car-checksums", models);
            }
            else
            {
                throw new ConfigurationException($"No data.acd found for {models}. This will allow players to cheat using modified data. More info: https://assettoserver.org/docs/common-configuration-errors#missing-car-checksums")
                {
                    HelpLink = "https://assettoserver.org/docs/common-configuration-errors#missing-car-checksums"
                };
            }
        }
    }

    private static Dictionary<string, byte[]> CalculateTrackChecksums(string track, string trackConfig)
    {
        var dict = new Dictionary<string, byte[]>();
        
        AddChecksum(dict, "system/data/surfaces.ini");

        string trackPath = $"content/tracks/{track}";

        if (string.IsNullOrEmpty(trackConfig))
        {
            AddChecksum(dict, $"{trackPath}/data/surfaces.ini");
            AddChecksum(dict, $"{trackPath}/models.ini");
        }
        else
        {
            AddChecksum(dict, $"{trackPath}/{trackConfig}/data/surfaces.ini");
            AddChecksum(dict, $"{trackPath}/models_{trackConfig}.ini");
        }
        
        ChecksumDirectory(dict, trackPath);

        return dict;
    }

    private static Dictionary<string, List<byte[]>> CalculateCarChecksums(IEnumerable<string> cars, bool allowAlternatives)
    {
        var dict = new Dictionary<string, List<byte[]>>();

        foreach (string car in cars)
        {
            string carFolder = $"content/cars/{car}";
            var checksums = new List<byte[]>();
            if (allowAlternatives && Directory.Exists(carFolder))
            {
                foreach (string file in Directory.EnumerateFiles(carFolder, "data*.acd"))
                {
                    if (TryCreateChecksum(file, out byte[]? checksum))
                    {
                        checksums.Add(checksum);
                        Log.Debug("Added checksum for {Path}", file);
                    }
                }
            }
            else
            {
                if (TryCreateChecksum(Path.Join(carFolder, "data.acd"), out byte[]? checksum))
                {
                    checksums.Add(checksum);
                    Log.Debug("Added checksum for {Path}", car);
                }
            }

            dict.Add(car, checksums);
        }

        return dict;
    }

    private static bool TryCreateChecksum(string filePath, [MaybeNullWhen(false)] out byte[] checksum)
    {
        if (File.Exists(filePath))
        {
            using var md5 = MD5.Create();
            using var fileStream = File.OpenRead(filePath);
            checksum = md5.ComputeHash(fileStream);
            return true;
        }

        checksum = null;
        return false;
    }

    private static void AddChecksum(Dictionary<string, byte[]> dict, string filePath, string? name = null)
    {
        if (TryCreateChecksum(filePath, out byte[]? checksum))
        {
            dict.Add(name ?? filePath, checksum);
            Log.Debug("Added checksum for {Path}", name ?? filePath);
        }
    }
    
    private static void ChecksumDirectory(Dictionary<string, byte[]> dict, string directory)
    {
        if (!Directory.Exists(directory))
            return;
        
        string[] allFiles = Directory.GetFiles(directory);
        foreach (string file in allFiles)
        {
            string name = Path.GetFileName(file);

            if (name == "surfaces.ini" || name.EndsWith(".kn5"))
                AddChecksum(dict, file, file.Replace("\\", "/"));
        }
    }
}
