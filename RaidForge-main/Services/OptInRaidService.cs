using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;

namespace RaidForge.Services
{
    public static class OptInRaidService
    {
        private static Dictionary<string, DateTime> _optedInStates = new Dictionary<string, DateTime>();
        private static Dictionary<string, string> _optedInNames = new Dictionary<string, string>();

        private static readonly string DataSubfolder = "Data";
        private static readonly string SaveFileName = "RaidForge_OptInState.csv";
        private static readonly object _saveLock = new();
        private static bool _dataLoaded = false;

        private static string GetSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);
            return Path.Combine(dirPath, SaveFileName);
        }

        public static void LoadStateFromDisk()
        {
            if (_dataLoaded) return;
            lock (_saveLock)
            {
                string filePath = GetSaveFilePath();
                _optedInStates.Clear();
                _optedInNames.Clear();

                if (!File.Exists(filePath))
                {
                    _dataLoaded = true;
                    return;
                }

                try
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string[] parts = line.Split(',');
                        if (parts.Length >= 3)
                        {
                            string key = parts[0];
                            if (long.TryParse(parts[1], out long ticks))
                            {
                                string name = parts[2];
                                if (!string.IsNullOrEmpty(key))
                                {
                                    _optedInStates[key] = new DateTime(ticks, DateTimeKind.Utc);
                                    _optedInNames[key] = name;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
                _dataLoaded = true;
            }
        }

        private static void SaveStateToDisk()
        {
            lock (_saveLock)
            {
                string filePath = GetSaveFilePath();
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("PersistentKey,OptInTimestampTicks,ContextualName");

                    foreach (var kvp in _optedInStates)
                    {
                        string key = kvp.Key;
                        long ticks = kvp.Value.Ticks;
                        string name = _optedInNames.TryGetValue(key, out var n) ? n : "Unknown";
                        sb.AppendLine($"{key},{ticks},{name}");
                    }

                    File.WriteAllText(filePath, sb.ToString());
                }
                catch (Exception)
                {
                }
            }
        }

        public static void OptIn(string persistentKey, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey)) return;
            _optedInStates[persistentKey] = DateTime.UtcNow;
            _optedInNames[persistentKey] = contextualName;
            SaveStateToDisk();
        }

        public static void OptOut(string persistentKey, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey)) return;
            _optedInStates.Remove(persistentKey);
            _optedInNames.Remove(persistentKey);
            SaveStateToDisk();
        }

        public static bool IsOptedIn(string persistentKey)
        {
            return !string.IsNullOrEmpty(persistentKey) && _optedInStates.ContainsKey(persistentKey);
        }

        public static bool TryGetOptInTime(string persistentKey, out DateTime optInTime)
        {
            optInTime = default;
            return !string.IsNullOrEmpty(persistentKey) && _optedInStates.TryGetValue(persistentKey, out optInTime);
        }
    }
}