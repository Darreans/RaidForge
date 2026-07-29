using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Services
{
    public static class OptInRaidService
    {
        private static Dictionary<string, DateTime> _manualStateTimes = new Dictionary<string, DateTime>();
        private static Dictionary<string, string> _manualStateNames = new Dictionary<string, string>();

        private static readonly string DataSubfolder = "Data";
        private static readonly string OptInSaveFileName = "RaidForge_OptInState.csv";
        private static readonly string OptOutSaveFileName = "RaidForge_OptOutState.csv";
        private static readonly object _saveLock = new();
        private static bool _dataLoaded = false;
        private static bool _loadedDefaultOptInMode = false;

        private static bool DefaultOptInMode => OptInRaidingConfig.UseDefaultOptInMode;

        private static string GetSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);
            return Path.Combine(dirPath, DefaultOptInMode ? OptOutSaveFileName : OptInSaveFileName);
        }

        public static void ResetRuntimeState()
        {
            lock (_saveLock)
            {
                _manualStateTimes.Clear();
                _manualStateNames.Clear();
                _dataLoaded = false;
                _loadedDefaultOptInMode = false;
            }
        }

        public static void ReloadStateFromDisk() => LoadStateFromDisk(true);

        public static void LoadStateFromDisk(bool forceReload = false)
        {
            bool currentMode = DefaultOptInMode;
            if (_dataLoaded && !forceReload && _loadedDefaultOptInMode == currentMode) return;

            lock (_saveLock)
            {
                currentMode = DefaultOptInMode;
                if (_dataLoaded && !forceReload && _loadedDefaultOptInMode == currentMode) return;

                string filePath = GetSaveFilePath();
                _manualStateTimes.Clear();
                _manualStateNames.Clear();
                _loadedDefaultOptInMode = currentMode;

                if (!File.Exists(filePath))
                {
                    _dataLoaded = true;
                    return;
                }

                try
                {
                    string[] lines = SharedFileIO.ReadAllLinesShared(filePath);
                    foreach (var line in lines.Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string[] parts = DelimitedText.ParseLine(line, ',');
                        if (parts.Length >= 3)
                        {
                            string key = parts[0];
                            if (long.TryParse(parts[1], out long ticks))
                            {
                                string name = parts[2];
                                if (!string.IsNullOrEmpty(key))
                                {
                                    _manualStateTimes[key] = new DateTime(ticks, DateTimeKind.Utc);
                                    _manualStateNames[key] = name;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error("[OptIn] Failed to load opt-in/opt-out state from disk.", ex);
                }

                _dataLoaded = true;
            }
        }

        public static void ProcessAutoOptOut(int lockDurationHours)
        {
            EnsureLoadedForCurrentMode();

            if (DefaultOptInMode)
            {
                return;
            }

            List<string> toRemove = new List<string>();
            DateTime now = DateTime.UtcNow;
            TimeSpan lockDuration = TimeSpan.FromHours(lockDurationHours);

            lock (_saveLock)
            {
                if (_manualStateTimes.Count == 0) return;

                foreach (var kvp in _manualStateTimes)
                {
                    if (now - kvp.Value >= lockDuration)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                if (toRemove.Count > 0)
                {
                    foreach (var key in toRemove)
                    {
                        _manualStateTimes.Remove(key);
                        string name = _manualStateNames.TryGetValue(key, out var n) ? n : key;
                        _manualStateNames.Remove(key);
                        LoggingHelper.Info($"[OptIn] Auto-opted out '{name}' because their {lockDurationHours}h cooldown expired.");
                    }

                    RaidMapIconService.MarkPersistentStateIconsDirty();
                    SaveStateToDisk();
                }
            }
        }

        public static void OptIn(string persistentKey, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey)) return;
            EnsureLoadedForCurrentMode();

            lock (_saveLock)
            {
                if (DefaultOptInMode)
                {
                    _manualStateTimes.Remove(persistentKey);
                    _manualStateNames.Remove(persistentKey);
                }
                else
                {
                    _manualStateTimes[persistentKey] = DateTime.UtcNow;
                    _manualStateNames[persistentKey] = contextualName;
                }

                _dataLoaded = true;
                _loadedDefaultOptInMode = DefaultOptInMode;
            }

            RaidMapIconService.MarkPersistentStateIconsDirty();
            SaveStateToDisk();
        }

        public static void OptOut(string persistentKey, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey)) return;
            EnsureLoadedForCurrentMode();

            lock (_saveLock)
            {
                if (DefaultOptInMode)
                {
                    _manualStateTimes[persistentKey] = DateTime.UtcNow;
                    _manualStateNames[persistentKey] = contextualName;
                }
                else
                {
                    _manualStateTimes.Remove(persistentKey);
                    _manualStateNames.Remove(persistentKey);
                }

                _dataLoaded = true;
                _loadedDefaultOptInMode = DefaultOptInMode;
            }

            RaidMapIconService.MarkPersistentStateIconsDirty();
            SaveStateToDisk();
        }

        public static bool IsOptedIn(string persistentKey)
        {
            EnsureLoadedForCurrentMode();
            if (string.IsNullOrEmpty(persistentKey)) return false;

            lock (_saveLock)
            {
                bool hasManualState = _manualStateTimes.ContainsKey(persistentKey);
                return DefaultOptInMode ? !hasManualState : hasManualState;
            }
        }

        public static bool IsManuallyOptedOut(string persistentKey)
        {
            EnsureLoadedForCurrentMode();
            if (string.IsNullOrEmpty(persistentKey) || !DefaultOptInMode) return false;

            lock (_saveLock)
            {
                return _manualStateTimes.ContainsKey(persistentKey);
            }
        }

        public static bool TryGetOptInTime(string persistentKey, out DateTime optInTime)
        {
            optInTime = default;
            EnsureLoadedForCurrentMode();
            if (string.IsNullOrEmpty(persistentKey)) return false;

            lock (_saveLock)
            {
                if (DefaultOptInMode)
                {
                    bool isManuallyOptedOut = _manualStateTimes.ContainsKey(persistentKey);
                    if (isManuallyOptedOut)
                    {
                        return false;
                    }

                    optInTime = DateTime.MinValue;
                    return true;
                }

                return _manualStateTimes.TryGetValue(persistentKey, out optInTime);
            }
        }

        public static bool TryGetManualStateTime(string persistentKey, out DateTime stateTime)
        {
            stateTime = default;
            EnsureLoadedForCurrentMode();
            if (string.IsNullOrEmpty(persistentKey)) return false;

            lock (_saveLock)
            {
                return _manualStateTimes.TryGetValue(persistentKey, out stateTime);
            }
        }

        private static void EnsureLoadedForCurrentMode()
        {
            if (!_dataLoaded || _loadedDefaultOptInMode != DefaultOptInMode)
            {
                LoadStateFromDisk(_loadedDefaultOptInMode != DefaultOptInMode);
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
                    sb.AppendLine(DefaultOptInMode
                        ? "PersistentKey,OptOutTimestampTicks,ContextualName"
                        : "PersistentKey,OptInTimestampTicks,ContextualName");

                    foreach (var kvp in _manualStateTimes)
                    {
                        string key = kvp.Key;
                        long ticks = kvp.Value.Ticks;
                        string name = _manualStateNames.TryGetValue(key, out var n) ? n : "Unknown";
                        sb.AppendLine($"{DelimitedText.Escape(key, ',')},{ticks},{DelimitedText.Escape(name, ',')}");
                    }

                    SharedFileIO.QueueWriteAllTextWithRetry(filePath, sb.ToString());
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error("[OptIn] Failed to save opt-in/opt-out state to disk.", ex);
                }
            }
        }
    }
}
