using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Windows.Storage;

namespace SmoothTube.Services
{
    public static class PersistentCacheService
    {
        private const string CacheFolderName = "cache";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        public static T? Load<T>(string fileName)
            where T : class
        {
            try
            {
                string path = GetCacheFilePath(fileName);

                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public static void Save<T>(
            string fileName,
            T value)
            where T : class
        {
            try
            {
                string path = GetCacheFilePath(fileName);
                string folder = Path.GetDirectoryName(path) ??
                    ApplicationData.Current.LocalFolder.Path;

                Directory.CreateDirectory(folder);

                string json = JsonSerializer.Serialize(value, JsonOptions);

                File.WriteAllText(path, json);
            }
            catch
            {
                // Cache should never crash the app.
            }
        }

        public static void Clear(string fileName)
        {
            try
            {
                string path = GetCacheFilePath(fileName);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cache cleanup failures.
            }
        }

        private static string GetCacheFilePath(string fileName)
        {
            return Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                CacheFolderName,
                fileName);
        }
    }
}
