using IniParser.Model;
using IniParser;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;

namespace AssettoServer.Server;

// Based on https://github.com/MrElectrify/AssettoCorsaTools/blob/master/AssettoCorsaToolFramework/src/Framework/Files/FileManager.cpp
public class CarDataArchive
{
    public CarDataArchive(string fileName, string directory)
    {
        _fileName = fileName;
        _key = CalculateKey(directory);
        _fileMap = new Dictionary<string, byte[]?>();
    }

    public bool Load()
    {
        using FileStream fs = File.Open(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader sr = new BinaryReader(fs);

        while (fs.Position < fs.Length)
        {
            int nameSize = sr.ReadInt32();
            if (nameSize == -1111)
            {
                fs.Seek(8, SeekOrigin.Begin);
                continue;
            }

            if (nameSize <= 0)
                continue;

            string fileName = Encoding.ASCII.GetString(sr.ReadBytes(nameSize));
            int fileSize = sr.ReadInt32();
            if (fileSize <= 0)
                continue;

            int[] encrypted = new int[fileSize];
            for (int i = 0; i < fileSize; i++)
                encrypted[i] = sr.ReadInt32();

            byte[] fileContents = new byte[encrypted.Length];
            for (int i = 0; i < fileSize; ++i)
                fileContents[i] = (byte)(encrypted[i] - (byte)_key[i % _key.Length]);

            // Register file
            _fileMap.Add(fileName, fileContents);
        }

        return true;
    }

    public IEnumerable<string> GetFiles()
    {
        return _fileMap.Keys;
    }

    public byte[]? GetContent(string fileName)
    {
        if (_fileMap.TryGetValue(fileName, out byte[]? bytes))
            return bytes;
        return null;
    }

    public T? GetIni<T>(string fileName) where T : class, new()
    {
        byte[]? content = GetContent(fileName);
        if (content == null)
            return null;

        FileIniDataParser parser = new FileIniDataParser();
        IniData data = parser.ReadData(new StreamReader(new MemoryStream(content, false)));
        return data.DeserializeObject<T>();
    }

    private readonly string _fileName;
    private readonly string _key;
    private readonly Dictionary<string, byte[]?> _fileMap;

    private static string CalculateKey(string directory)
    {
        /*
         *	This mimics the behavior of Assetto Corsa's ksSecurity::keyFromString
         */
        byte factor0 = 0;

        // add all chars, as in AC
        foreach (char c in directory)
            factor0 += (byte)c;

        // another strange sum
        int factor1 = 0;
        for (int i = 0; i < directory.Length - 1; i += 2)
        {
            int tmp = directory[i] * factor1;
            factor1 = tmp - directory[i + 1];
        }

        // another strange sum
        int factor2 = 0;
        for (int i = 1; i < directory.Length - 3; i += 3)
        {
            int tmp0 = directory[i] * factor2;
            int tmp1 = tmp0 / (directory[i + 1] + 27);
            factor2 = -27 - directory[i - 1] + tmp1;
        }

        // yet another strange sum
        byte factor3 = unchecked((byte)-125);
        for (int i = 1; i < directory.Length; ++i)
        {
            factor3 = (byte)(factor3 - directory[i]);
        }

        // of course, another strange sum
        int factor4 = 66;
        for (int i = 1; i < directory.Length - 4; i += 4)
        {
            var tmp = (directory[i] + 15) * factor4;
            factor4 = (directory[i - 1] + 15) * tmp + 22;
        }

        // yup, you guessed it
        byte factor5 = 101;
        for (int i = 0; i < directory.Length - 2; i += 2)
        {
            factor5 = (byte)(factor5 - (byte)directory[i]);
        }

        // not even a purpose in commenting these anymore
        int factor6 = 171;
        for (int i = 0; i < directory.Length - 2; i += 2)
        {
            factor6 %= directory[i];
        }

        // last one, finally
        int factor7 = 171;
        for (int i = 0; i < directory.Length - 1;)
        {
            var tmp = factor7 / directory[i];
            factor7 = directory[++i] + tmp;
        }

        return $"{factor0}-{(byte)factor1}-{(byte)factor2}-{factor3}-" +
               $"{(byte)factor4}-{factor5}-{(byte)factor6}-{(byte)factor7}";
    }
}
