using BepInEx.Configuration;
using BepInEx.Logging;
using ProjectM;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RaidForge
{
    public static class GolemAutomationConfig
    {
        public static ConfigEntry<bool> EnableGolemAutomation { get; private set; }
        public static ConfigEntry<string> ServerStartDate { get; private set; }

        private static Dictionary<SiegeWeaponHealth, (ConfigEntry<bool> Enabled, ConfigEntry<int> Day)> _healthLevelConfigs =
            new Dictionary<SiegeWeaponHealth, (ConfigEntry<bool> Enabled, ConfigEntry<int> Day)>();

        public static SortedDictionary<int, SiegeWeaponHealth> ParsedSchedule { get; private set; } = new SortedDictionary<int, SiegeWeaponHealth>();
        public static DateTime? ParsedStartDate { get; private set; } = null;

        private static ManualLogSource _logger;
        private const string CONFIG_SECTION = "GolemAutomation";

        public static void Initialize(ConfigFile config, ManualLogSource logger)
        {
            _logger = logger;

            EnableGolemAutomation = config.Bind(CONFIG_SECTION, "EnableGolemAutomation", false,
                "Enable automatic adjustment of Siege Golem health based on server days passed.");

            ServerStartDate = config.Bind(CONFIG_SECTION, "ServerStartDate", "",
                "The date and time the server 'started' for day counting. Format: yyyy-MM-dd HH:mm:ss (e.g., 2025-05-01 18:00:00). Day 0 starts at this time.");

            foreach (SiegeWeaponHealth healthLevel in Enum.GetValues(typeof(SiegeWeaponHealth)))
            {
                int defaultDay = (healthLevel == SiegeWeaponHealth.Normal) ? 0 : -1;
                bool defaultEnabled = (healthLevel == SiegeWeaponHealth.Normal);

                var enabledEntry = config.Bind($"{CONFIG_SECTION}.Levels", $"{healthLevel}_Enable", defaultEnabled,
                    $"Enable {healthLevel} Golem health level in the automation schedule.");

                var dayEntry = config.Bind($"{CONFIG_SECTION}.Levels", $"{healthLevel}_Day", defaultDay,
                    $"Day number (starting from 0) when {healthLevel} health should become active, if enabled. Use -1 to disable this specific level even if enabled toggle is true.");

                _healthLevelConfigs[healthLevel] = (enabledEntry, dayEntry);
            }

            ParseStartDate();
            ParseHealthSchedule();

            if (RaidConfig.EnableVerboseLogging.Value) _logger?.LogInfo("Golem Automation Config Initialized with granular level settings.");
        }

        public static void ReloadAndParseAll()
        {
            if (_logger == null) return;
            if (RaidConfig.EnableVerboseLogging.Value) _logger.LogInfo("Reloading and parsing Golem Automation config...");
            ParseStartDate();
            ParseHealthSchedule();
        }

        private static void ParseStartDate()
        {
            ParsedStartDate = null;
            string startDateStr = ServerStartDate.Value?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(startDateStr))
            {
                _logger?.LogInfo("GolemAutomation.ServerStartDate is not set. Automation requires this.");
                return;
            }

            if (DateTime.TryParseExact(startDateStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                ParsedStartDate = parsedDate;
                if (RaidConfig.EnableVerboseLogging.Value) _logger?.LogInfo($"Parsed GolemAutomation.ServerStartDate: {ParsedStartDate.Value:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                _logger?.LogError($"!!! Invalid GolemAutomation.ServerStartDate format: '{startDateStr}'. Expected 'yyyy-MM-dd HH:mm:ss'. Automation disabled until fixed.");
            }
        }

        private static void ParseHealthSchedule()
        {
            ParsedSchedule.Clear();
            if (RaidConfig.EnableVerboseLogging.Value) _logger?.LogInfo("Parsing Golem Health Schedule from individual level settings...");

            foreach (var kvp in _healthLevelConfigs)
            {
                SiegeWeaponHealth healthLevel = kvp.Key;
                bool isEnabled = kvp.Value.Enabled.Value;
                int dayNum = kvp.Value.Day.Value;

                if (isEnabled && dayNum >= 0)
                {
                    ParsedSchedule[dayNum] = healthLevel;
                    if (RaidConfig.EnableVerboseLogging.Value) _logger?.LogInfo($"Parsed Golem Schedule Entry: Day {dayNum} -> {healthLevel} (Enabled)");
                }
            }

            if (ParsedSchedule.Count > 0) { if (RaidConfig.EnableVerboseLogging.Value) _logger?.LogInfo($"Successfully parsed {ParsedSchedule.Count} ENABLED golem health schedule entries."); }
            else
            {
                _logger?.LogWarning("No Golem health levels are enabled with a valid day number (>= 0) in the configuration.");
            }
        }

        public static SiegeWeaponHealth? GetTargetHealthForDay(int dayCount)
        {
            if (ParsedSchedule.Count == 0) return null;

            var relevantEntry = ParsedSchedule.Where(kvp => kvp.Key <= dayCount)
                                              .OrderByDescending(kvp => kvp.Key)
                                              .FirstOrDefault();

            if (ParsedSchedule.Any(kvp => kvp.Key <= dayCount))
            {
                return relevantEntry.Value;
            }

            return null;
        }
    }
}
