using Newtonsoft.Json;
using System;
using System.IO;

namespace kuboPix.Helpers
{
    /// <summary>
    /// Small, thread-safe, durable JSON key/value store under %APPDATA%\kuboPix\.
    /// Used by the printed-order ledger (T-5.1) and the pending status-update queue (T-5.3)
    /// so they survive an app restart / power loss. Tolerant of missing or corrupt files.
    /// </summary>
    public static class LocalStore
    {
        private static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kuboPix");

        private static readonly object _lock = new object();

        public static void Save<T>(string key, T value)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(BaseDir);
                    string json = JsonConvert.SerializeObject(value, Formatting.Indented);
                    File.WriteAllText(PathFor(key), json);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LocalStore.Save error for '{key}': {ex.Message}");
            }
        }

        public static T? Load<T>(string key)
        {
            try
            {
                lock (_lock)
                {
                    string path = PathFor(key);
                    if (!File.Exists(path))
                        return default;

                    string json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json))
                        return default;

                    return JsonConvert.DeserializeObject<T>(json);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LocalStore.Load error for '{key}': {ex.Message}");
                return default;
            }
        }

        private static string PathFor(string key)
            => Path.Combine(BaseDir, key + ".json");
    }
}
