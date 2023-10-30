﻿using Galaxy_Swapper_v2.Workspace.Hashes;
using Galaxy_Swapper_v2.Workspace.Structs;
using Galaxy_Swapper_v2.Workspace.Swapping.Other;
using Galaxy_Swapper_v2.Workspace.Utilities;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Galaxy_Swapper_v2.Workspace.Plugins
{
    public static class Plugin
    {
        public static readonly string Path = $"{App.Config}\\Plugins";
        public enum Type
        {
            None = 0,
            Aes = 1,
            Oodle
        }
        public static void Import(FileInfo fileInfo, JObject parse)
        {
            var writer = new Writer(new byte[fileInfo.Length + 60000]);
            var type = Compression.Type.Aes;

            if (!parse["Compression"].KeyIsNullOrEmpty() && Enum.TryParse(typeof(Compression.Type), parse["Compression"].Value<string>(), true, out object newtype))
            {
                type = (Compression.Type)newtype;
            }

            writer.Write(type); //1 = encrypted in case I want to add other formats later on

            Compression.Compress(out byte[] compressed, out byte[] key, parse.ToString(Newtonsoft.Json.Formatting.None), type);

            //Import directory
            byte[] importpath = Encoding.ASCII.GetBytes(fileInfo.FullName);
            writer.Write(importpath.Length);
            writer.WriteBytes(importpath);

            //Encryption key
            if (type != Compression.Type.None)
            {
                writer.Write(CityHash.Hash(key));
                writer.Write(key.Length);
                writer.WriteBytes(key);
            }

            //Plugin buffer
            writer.Write(CityHash.Hash(compressed));
            writer.Write(compressed.Length);
            writer.WriteBytes(compressed);

            string output = $"{Path}\\{System.IO.Path.GetRandomFileName()}.plugin";
            File.WriteAllBytes(output, writer.ToByteArray(writer.Position));

            Log.Information($"Plugin wrote to: {output}");
        }

        public static PluginData Export(FileInfo fileInfo)
        {
            var reader = new Reader(File.ReadAllBytes(fileInfo.FullName));
            var type = (Compression.Type)reader.Read<int>();
            int importpathlength = reader.Read<int>();
            string importpath = reader.ReadStrings(importpathlength);

            byte[] key = null!;

            if (type != Compression.Type.None)
            {
                ulong keyhash = reader.Read<ulong>();
                int keylength = reader.Read<int>();
                key = reader.ReadBytes(keylength);

                if (CityHash.Hash(key) != keyhash)
                {
                    Log.Warning($"{fileInfo.Name} encryption key hash was not as expected plugin will be skipped");
                    return null!;
                }
            }

            ulong pluginhash = reader.Read<ulong>();
            int pluginlength = reader.Read<int>();
            byte[] pluginbuffer = reader.ReadBytes(pluginlength);

            if (CityHash.Hash(pluginbuffer) != pluginhash)
            {
                Log.Warning($"{fileInfo.Name} buffer hash was not as expected plugin will be skipped");
                return null!;
            }

            Compression.Decompress(pluginbuffer, key, out string decompressed, type);

            if (!decompressed.ValidJson())
            {
                Log.Warning($"{fileInfo.Name} is not in a valid json format and will be skipped");
                return null!;
            }

            var parse = JObject.Parse(decompressed);

            return new() { Import = importpath, Path = fileInfo.FullName, Content = decompressed, Parse = parse };
        }

        public static PluginData ExportOld(FileInfo fileInfo)
        {
            string content = File.ReadAllText(fileInfo.FullName);

            content = Encoding.ASCII.GetString(Utilities.Compression.Decompress(content));

            if (!content.ValidJson())
            {
                Log.Warning($"{fileInfo.Name} is not in a valid json format and will be skipped");
                return null!;
            }

            var parse = JObject.Parse(content);
            return new() { Import = null!, Path = fileInfo.FullName, Content = content, Parse = parse };
        }

        public static List<PluginData> GetPlugins()
        {
            var list = new List<PluginData>();

            foreach (var plugin in new DirectoryInfo(Path).GetFiles("*.*", SearchOption.AllDirectories))
            {
                PluginData plugindata = null!;

                switch (plugin.Extension)
                {
                    case ".encrypted":
                        plugindata = ExportOld(plugin);
                        break;
                    case ".plugin":
                        plugindata = Export(plugin);
                        break;
                }

                if (plugindata is not null)
                    list.Add(plugindata);
            }

            return list;
        }
    }
}
