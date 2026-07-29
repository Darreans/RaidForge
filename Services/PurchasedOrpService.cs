using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Services
{
    public sealed class PurchasedOrpEntry
    {
        public string PersistentKey { get; set; }
        public string ContextualName { get; set; }
        public int RemainingRaidDays { get; set; }
        public DateTime LastProcessedLocalDate { get; set; }
        public DateTime LastPurchaseUtc { get; set; }
    }

    public static class PurchasedOrpService
    {
        private const string DataSubfolder = "Data";
        private const string SaveFileName = "RaidForge_PurchasedOrp.csv";
        private const string DateFormat = "yyyy-MM-dd";

        private static readonly Dictionary<string, PurchasedOrpEntry> _entriesByKey = new Dictionary<string, PurchasedOrpEntry>();
        private static readonly object _saveLock = new object();
        private static bool _dataLoaded;
        private static DateTime _lastConsumptionProcessedDate = DateTime.MinValue;

        private static string GetSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);
            return Path.Combine(dirPath, SaveFileName);
        }

        public static DateTime GetCurrentRaidDate()
        {
            return RaidConfig.GetRaidScheduleNow().Date;
        }

        public static void ResetRuntimeState()
        {
            lock (_saveLock)
            {
                _entriesByKey.Clear();
                _dataLoaded = false;
                _lastConsumptionProcessedDate = DateTime.MinValue;
            }
        }

        public static void ReloadStateFromDisk() => LoadStateFromDisk(true);

        public static void LoadStateFromDisk(bool forceReload = false)
        {
            if (_dataLoaded && !forceReload)
            {
                return;
            }

            lock (_saveLock)
            {
                if (_dataLoaded && !forceReload)
                {
                    return;
                }

                _entriesByKey.Clear();
                string filePath = GetSaveFilePath();

                if (!File.Exists(filePath))
                {
                    _dataLoaded = true;
                    return;
                }

                try
                {
                    string[] lines = SharedFileIO.ReadAllLinesShared(filePath);

                    foreach (string line in lines.Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        string[] parts = DelimitedText.ParseLine(line, ',');
                        if (parts.Length < 4)
                        {
                            continue;
                        }

                        string key = parts[0].Trim();
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        PurchasedOrpEntry entry = TryLoadCurrentFormat(parts);
                        if (entry == null)
                        {
                            entry = TryLoadLegacyDateFormat(parts);
                        }

                        if (entry != null && entry.RemainingRaidDays > 0)
                        {
                            entry.PersistentKey = key;
                            _entriesByKey[key] = entry;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error("[PurchasedORP] Failed to load purchased ORP state from disk.", ex);
                }

                _dataLoaded = true;
                ProcessDueRaidDayConsumptionInternal(GetCurrentRaidDate(), saveIfChanged: true, force: true);
            }
        }

        public static void ProcessDueRaidDayConsumption() => ProcessDueRaidDayConsumption(force: false);

        public static void ProcessDueRaidDayConsumption(bool force)
        {
            EnsureLoaded();

            lock (_saveLock)
            {
                ProcessDueRaidDayConsumptionInternal(GetCurrentRaidDate(), saveIfChanged: true, force: force);
            }
        }

        public static bool HasProtectionForToday(string persistentKey)
        {
            if (string.IsNullOrWhiteSpace(persistentKey) || !IsRaidDate(GetCurrentRaidDate()))
            {
                return false;
            }

            EnsureLoaded();

            lock (_saveLock)
            {
                ProcessDueRaidDayConsumptionInternal(GetCurrentRaidDate(), saveIfChanged: true);
                return _entriesByKey.TryGetValue(persistentKey, out PurchasedOrpEntry entry) &&
                    entry.RemainingRaidDays > 0;
            }
        }

        public static int GetRemainingRaidDays(string persistentKey)
        {
            if (string.IsNullOrWhiteSpace(persistentKey))
            {
                return 0;
            }

            EnsureLoaded();

            lock (_saveLock)
            {
                ProcessDueRaidDayConsumptionInternal(GetCurrentRaidDate(), saveIfChanged: true);
                return _entriesByKey.TryGetValue(persistentKey, out PurchasedOrpEntry entry)
                    ? Math.Max(0, entry.RemainingRaidDays)
                    : 0;
            }
        }

        public static bool TryGetEntry(string persistentKey, out PurchasedOrpEntry entry)
        {
            entry = null;

            if (string.IsNullOrWhiteSpace(persistentKey))
            {
                return false;
            }

            EnsureLoaded();

            lock (_saveLock)
            {
                ProcessDueRaidDayConsumptionInternal(GetCurrentRaidDate(), saveIfChanged: true);

                if (!_entriesByKey.TryGetValue(persistentKey, out PurchasedOrpEntry storedEntry))
                {
                    return false;
                }

                entry = new PurchasedOrpEntry
                {
                    PersistentKey = storedEntry.PersistentKey,
                    ContextualName = storedEntry.ContextualName,
                    RemainingRaidDays = storedEntry.RemainingRaidDays,
                    LastProcessedLocalDate = storedEntry.LastProcessedLocalDate,
                    LastPurchaseUtc = storedEntry.LastPurchaseUtc
                };

                return true;
            }
        }

        public static void AddRaidDays(string persistentKey, string contextualName, int raidDays)
        {
            if (string.IsNullOrWhiteSpace(persistentKey) || raidDays <= 0)
            {
                return;
            }

            EnsureLoaded();

            lock (_saveLock)
            {
                DateTime today = GetCurrentRaidDate();
                ProcessDueRaidDayConsumptionInternal(today, saveIfChanged: false);

                if (!_entriesByKey.TryGetValue(persistentKey, out PurchasedOrpEntry entry))
                {
                    entry = new PurchasedOrpEntry
                    {
                        PersistentKey = persistentKey,
                        LastProcessedLocalDate = today
                    };

                    _entriesByKey[persistentKey] = entry;
                }

                entry.ContextualName = contextualName;
                entry.RemainingRaidDays = checked(entry.RemainingRaidDays + raidDays);
                entry.LastPurchaseUtc = DateTime.UtcNow;

                if (entry.LastProcessedLocalDate == default)
                {
                    entry.LastProcessedLocalDate = today;
                }

                SaveStateToDiskInternal();
            }
        }

        public static int RemoveRaidDays(string persistentKey, int raidDays)
        {
            if (string.IsNullOrWhiteSpace(persistentKey) || raidDays <= 0)
            {
                return 0;
            }

            EnsureLoaded();

            lock (_saveLock)
            {
                DateTime today = GetCurrentRaidDate();
                ProcessDueRaidDayConsumptionInternal(today, saveIfChanged: false);

                if (!_entriesByKey.TryGetValue(persistentKey, out PurchasedOrpEntry entry))
                {
                    return 0;
                }

                int removed = Math.Min(raidDays, Math.Max(0, entry.RemainingRaidDays));
                entry.RemainingRaidDays -= removed;

                if (entry.RemainingRaidDays <= 0)
                {
                    _entriesByKey.Remove(persistentKey);
                }

                SaveStateToDiskInternal();
                return removed;
            }
        }

        public static bool IsRaidDate(DateTime date)
        {
            List<RaidScheduleEntry> schedule = RaidConfig.Schedule;
            if (schedule == null || schedule.Count == 0)
            {
                return false;
            }

            DayOfWeek day = date.Date.DayOfWeek;

            foreach (RaidScheduleEntry entry in schedule)
            {
                if (entry.Day == day)
                {
                    return true;
                }

                DayOfWeek nextDay = (DayOfWeek)(((int)entry.Day + 1) % 7);
                if (entry.SpansMidnight && entry.EndTime != TimeSpan.Zero && nextDay == day)
                {
                    return true;
                }
            }

            return false;
        }

        private static PurchasedOrpEntry TryLoadCurrentFormat(string[] parts)
        {
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int remainingRaidDays))
            {
                return null;
            }

            DateTime lastProcessedDate = GetCurrentRaidDate();
            if (parts.Length >= 4 &&
                DateTime.TryParseExact(
                    parts[3].Trim(),
                    DateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate))
            {
                lastProcessedDate = parsedDate.Date;
            }

            DateTime lastPurchaseUtc = DateTime.MinValue;
            if (parts.Length >= 5 && long.TryParse(parts[4], out long ticks))
            {
                lastPurchaseUtc = new DateTime(ticks, DateTimeKind.Utc);
            }

            return new PurchasedOrpEntry
            {
                ContextualName = parts[1].Trim(),
                RemainingRaidDays = Math.Max(0, remainingRaidDays),
                LastProcessedLocalDate = lastProcessedDate,
                LastPurchaseUtc = lastPurchaseUtc
            };
        }

        private static PurchasedOrpEntry TryLoadLegacyDateFormat(string[] parts)
        {
            int remainingRaidDays = 0;

            foreach (string dateText in parts[2].Split('|'))
            {
                if (DateTime.TryParseExact(
                    dateText.Trim(),
                    DateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate) &&
                    parsedDate.Date >= GetCurrentRaidDate())
                {
                    remainingRaidDays++;
                }
            }

            if (remainingRaidDays <= 0)
            {
                return null;
            }

            DateTime lastPurchaseUtc = DateTime.MinValue;
            if (parts.Length >= 4 && long.TryParse(parts[3], out long ticks))
            {
                lastPurchaseUtc = new DateTime(ticks, DateTimeKind.Utc);
            }

            return new PurchasedOrpEntry
            {
                ContextualName = parts[1].Trim(),
                RemainingRaidDays = remainingRaidDays,
                LastProcessedLocalDate = GetCurrentRaidDate(),
                LastPurchaseUtc = lastPurchaseUtc
            };
        }

        private static void EnsureLoaded()
        {
            if (!_dataLoaded)
            {
                LoadStateFromDisk();
            }
        }

        private static void ProcessDueRaidDayConsumptionInternal(DateTime today, bool saveIfChanged, bool force = false)
        {
            today = today.Date;
            if (!force && _lastConsumptionProcessedDate >= today)
            {
                return;
            }

            bool changed = false;
            DateTime lastDateToConsume = today.AddDays(-1);

            foreach (string key in _entriesByKey.Keys.ToList())
            {
                PurchasedOrpEntry entry = _entriesByKey[key];

                if (entry.LastProcessedLocalDate == default)
                {
                    entry.LastProcessedLocalDate = today;
                    changed = true;
                    continue;
                }

                DateTime cursor = entry.LastProcessedLocalDate.Date.AddDays(1);

                while (cursor <= lastDateToConsume && entry.RemainingRaidDays > 0)
                {
                    if (IsRaidDate(cursor))
                    {
                        entry.RemainingRaidDays--;
                        changed = true;
                    }

                    cursor = cursor.AddDays(1);
                }

                if (entry.LastProcessedLocalDate.Date < today)
                {
                    entry.LastProcessedLocalDate = today;
                    changed = true;
                }

                if (entry.RemainingRaidDays <= 0)
                {
                    _entriesByKey.Remove(key);
                    changed = true;
                }
            }

            if (changed && saveIfChanged)
            {
                SaveStateToDiskInternal();
            }

            _lastConsumptionProcessedDate = today;
        }

        private static void SaveStateToDiskInternal()
        {
            string filePath = GetSaveFilePath();

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("PersistentKey,ContextualName,RemainingRaidDays,LastProcessedLocalDate,LastPurchaseUtcTicks");

                foreach (PurchasedOrpEntry entry in _entriesByKey.Values.OrderBy(e => e.ContextualName, StringComparer.OrdinalIgnoreCase))
                {
                    if (entry.RemainingRaidDays <= 0)
                    {
                        continue;
                    }

                    sb.AppendLine(
                        $"{DelimitedText.Escape(entry.PersistentKey, ',')}," +
                        $"{DelimitedText.Escape(entry.ContextualName ?? "Unknown", ',')}," +
                        $"{entry.RemainingRaidDays.ToString(CultureInfo.InvariantCulture)}," +
                        $"{entry.LastProcessedLocalDate.Date.ToString(DateFormat, CultureInfo.InvariantCulture)}," +
                        $"{entry.LastPurchaseUtc.Ticks}");
                }

                SharedFileIO.QueueWriteAllTextWithRetry(filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[PurchasedORP] Failed to save purchased ORP state to disk.", ex);
            }
        }
    }
}
