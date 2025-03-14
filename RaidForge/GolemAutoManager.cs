using System;
using System.Collections.Generic;
using System.Globalization; 
using BepInEx.Logging;

namespace RaidForge
{
    public static class GolemAutoManager
    {
        private static ManualLogSource Logger => RaidForgePlugin.Logger;

        private static int _lastDayUpdated = -1;

        private static Dictionary<int, ProjectM.SiegeWeaponHealth> _dayToHealthMap
            = new Dictionary<int, ProjectM.SiegeWeaponHealth>();

      
        public static void LoadConfig()
        {
            _dayToHealthMap.Clear();

            var mapStr = Raidschedule.GolemDayToHealthMap.Value; // e.g. "0=Low,1=Normal,2=High"
            var pairs = mapStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var trimmed = pair.Trim();
                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex < 0) continue;

                var dayPart = trimmed.Substring(0, eqIndex).Trim();
                var valPart = trimmed.Substring(eqIndex + 1).Trim();

                if (!int.TryParse(dayPart, out int dayIndex))
                {
                    Logger.LogWarning($"[GolemAuto] Invalid day index: '{dayPart}' in map: {trimmed}");
                    continue;
                }

                if (!Enum.TryParse(valPart, true, out ProjectM.SiegeWeaponHealth health))
                {
                    Logger.LogWarning($"[GolemAuto] Invalid SiegeWeaponHealth '{valPart}' in map: {trimmed}");
                    continue;
                }

                _dayToHealthMap[dayIndex] = health;
            }

            Logger.LogInfo($"[GolemAuto] Parsed {_dayToHealthMap.Count} day->health pairs from config.");
            _lastDayUpdated = -1; 
        }

        
        public static void UpdateIfNeeded()
        {
            if (!Raidschedule.GolemAutomationEnabled.Value) return;

            if (string.IsNullOrWhiteSpace(Raidschedule.GolemStartDateString.Value)) return;

            if (!DateTime.TryParseExact(
                    Raidschedule.GolemStartDateString.Value,
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var startDate))
            {
                Logger.LogWarning("[GolemAuto] Cannot parse GolemStartDateString => "
                                  + Raidschedule.GolemStartDateString.Value);
                return;
            }

            var now = DateTime.Now;
            var dayCount = (int)Math.Floor((now - startDate).TotalDays);
            if (dayCount < 0) dayCount = 0;

            if (dayCount == _lastDayUpdated) return;

            if (!_dayToHealthMap.TryGetValue(dayCount, out var chosenHealth))
            {
                int maxDay = -1;
                foreach (var kv in _dayToHealthMap.Keys)
                {
                    if (kv > maxDay) maxDay = kv;
                }
                if (dayCount > maxDay && maxDay >= 0)
                {
                    chosenHealth = _dayToHealthMap[maxDay];
                }
                else
                {
                    Logger.LogInfo($"[GolemAuto] Day {dayCount} not found in map, no HP change.");
                    _lastDayUpdated = dayCount;
                    return;
                }
            }

            bool ok = SiegeWeaponManager.SetSiegeWeaponHealth(chosenHealth, Logger);
            if (ok)
            {
                Logger.LogInfo($"[GolemAuto] Day {dayCount}: HP set => {chosenHealth}");
            }
            _lastDayUpdated = dayCount;
        }

        
        public static int GetCurrentDay()
        {
            if (!Raidschedule.GolemAutomationEnabled.Value) return -1;

            var startStr = Raidschedule.GolemStartDateString.Value;
            if (string.IsNullOrWhiteSpace(startStr)) return -1;

            if (!DateTime.TryParseExact(
                    startStr,
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var startDate))
            {
                return -1;
            }

            var dayCount = (int)Math.Floor((DateTime.Now - startDate).TotalDays);
            return (dayCount < 0) ? 0 : dayCount;
        }

        
        public static void SetStartDate(DateTime dt)
        {
            Raidschedule.GolemStartDateString.Value
                = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            RaidForgePlugin.Instance.Config.Save();

            Raidschedule.LoadFromConfig();

            _lastDayUpdated = -1;
            Logger.LogInfo($"[GolemAuto] StartDate set => {dt}");
        }

        /// <summary>
        /// Called by ".golemauto clear" to remove the start date from the config
        /// and immediately save so the file updates.
        /// </summary>
        public static void ClearStartDate()
        {
            Raidschedule.GolemStartDateString.Value = "";
            RaidForgePlugin.Instance.Config.Save();

            Raidschedule.LoadFromConfig();
            _lastDayUpdated = -1;

            Logger.LogInfo("[GolemAuto] Cleared start date.");
        }
    }
}
