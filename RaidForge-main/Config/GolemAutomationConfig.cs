using BepInEx.Configuration;
using BepInEx.Logging;
using ProjectM;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidForge.Utils;

namespace RaidForge.Config
{
    public static class GolemAutomationConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableDayBasedAutomation { get; private set; }
        public static ConfigEntry<string> ServerStartDateForAutomation { get; private set; }
        public static ConfigEntry<string> ManualSiegeWeaponHealthOverride { get; private set; }

        private static Dictionary<SiegeWeaponHealth, (ConfigEntry<bool> Enabled, ConfigEntry<int> Day)> _healthLevelConfigs =
            new Dictionary<SiegeWeaponHealth, (ConfigEntry<bool> Enabled, ConfigEntry<int> Day)>();

        public static SortedDictionary<int, SiegeWeaponHealth> ParsedDayBasedSchedule { get; private set; } = new SortedDictionary<int, SiegeWeaponHealth>();
        public static DateTime? ParsedStartDate { get; private set; } = null;

        private static ManualLogSource _logger;

        public const string CONFIG_SECTION_MAIN_CONTROLS = "GolemMainControls"; 
        public const string CONFIG_SECTION_AUTOMATION_SCHEDULE = "GolemDayBasedAutomationSchedule";

        public static readonly Dictionary<SiegeWeaponHealth, int> GolemHpEstimates = new()
        {
            { SiegeWeaponHealth.VeryLow, 750 }, { SiegeWeaponHealth.Low, 1000 },
            { SiegeWeaponHealth.Normal, 1250 }, { SiegeWeaponHealth.High, 1750 },
            { SiegeWeaponHealth.VeryHigh, 2500 }, { SiegeWeaponHealth.MegaHigh, 3250 },
            { SiegeWeaponHealth.UltraHigh, 4000 }, { SiegeWeaponHealth.CrazyHigh, 5000 },
            { SiegeWeaponHealth.Max, 7500 },
        };

        public static void Initialize(ConfigFile configFile, ManualLogSource logger)
        {
            ConfigFileInstance = configFile;
            _logger = logger;

            EnableDayBasedAutomation = configFile.Bind(
                new ConfigDefinition(CONFIG_SECTION_MAIN_CONTROLS, "EnableDayBasedAutomation"),
                true,
                new ConfigDescription("Enable automatic adjustment of Siege Golem health based on server days passed. This is overridden if a ManualSiegeWeaponHealthOverride is set."));

            ServerStartDateForAutomation = configFile.Bind(
                new ConfigDefinition(CONFIG_SECTION_MAIN_CONTROLS, "ServerStartDateForAutomation"),
                string.Empty,
                new ConfigDescription("The date/time the server 'started' for day-based automation. Format: yyyy-MM-dd HH:mm:ss. Required if DayBasedAutomation is enabled."));

            ManualSiegeWeaponHealthOverride = configFile.Bind(
                new ConfigDefinition(CONFIG_SECTION_MAIN_CONTROLS, "ManualOverrideSiegeLevel"), 
                string.Empty,
                new ConfigDescription("Manually set a specific SiegeWeaponHealth level (e.g., 'Normal', 'High', 'Max'). If set to a valid level name, this overrides day-based automation. Set to empty string to disable this manual override and use day-based automation (if enabled)."));

            _healthLevelConfigs.Clear();
            foreach (SiegeWeaponHealth healthLevel in Enum.GetValues(typeof(SiegeWeaponHealth)))
            {
                int defaultDay = (healthLevel == SiegeWeaponHealth.Normal) ? 0 : -1;
                bool defaultEnabled = (healthLevel == SiegeWeaponHealth.Normal);

                ConfigDefinition enabledDef = new ConfigDefinition(CONFIG_SECTION_AUTOMATION_SCHEDULE, $"{healthLevel}_EnableInSchedule");
                ConfigDescription enabledDesc = new ConfigDescription($"Enable {healthLevel} Golem health for the day-based schedule.");
                var enabledEntry = configFile.Bind(enabledDef, defaultEnabled, enabledDesc);

                ConfigDefinition dayDef = new ConfigDefinition(CONFIG_SECTION_AUTOMATION_SCHEDULE, $"{healthLevel}_DayToActivateInSchedule");
                ConfigDescription dayDesc = new ConfigDescription($"Day number (from 0) for {healthLevel} health in the day-based schedule if enabled.");
                var dayEntry = configFile.Bind(dayDef, defaultDay, dayDesc);

                _healthLevelConfigs[healthLevel] = (enabledEntry, dayEntry);
            }

            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true && _logger != null)
                _logger.LogInfo("[GolemAutomationConfig] Initialized.");
        }

        public static bool SetServerStartDateAndSave(string dateString)
        {
            if (ServerStartDateForAutomation == null || ConfigFileInstance == null)
            {
                _logger?.LogError("[GolemAutomationConfig] Cannot set start date: Config not fully initialized.");
                return false;
            }
            ServerStartDateForAutomation.Value = dateString;
            ConfigFileInstance.Save();
            ReloadAndParseAll();
            LoggingHelper.Info($"[GolemAutomationConfig] Server start date for automation set to '{dateString}' and saved.");
            return true;
        }

        public static void SetManualSiegeWeaponHealthOverrideAndSave(SiegeWeaponHealth? level)
        {
            if (ManualSiegeWeaponHealthOverride == null || ConfigFileInstance == null)
            {
                _logger?.LogError("[GolemAutomationConfig] Cannot set manual level override: Config not fully initialized.");
                return;
            }
            ManualSiegeWeaponHealthOverride.Value = level?.ToString() ?? string.Empty;
            ConfigFileInstance.Save();
            LoggingHelper.Info($"[GolemAutomationConfig] Manual Golem SiegeWeaponHealth override set to '{ManualSiegeWeaponHealthOverride.Value}' and saved.");
        }

        public static void ClearManualSiegeWeaponHealthOverrideAndSave()
        {
            if (ManualSiegeWeaponHealthOverride == null || ConfigFileInstance == null)
            {
                _logger?.LogError("[GolemAutomationConfig] Cannot clear manual level override: Config not fully initialized.");
                return;
            }
            ManualSiegeWeaponHealthOverride.Value = string.Empty;
            ConfigFileInstance.Save();
            LoggingHelper.Info("[GolemAutomationConfig] Manual Golem SiegeWeaponHealth override cleared.");
        }

        public static void ReloadAndParseAll()
        {
            if (_logger == null) return;
            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                _logger.LogInfo("[GolemAutomationConfig] Reloading and parsing Golem settings...");
            ParseStartDate();
            ParseDayBasedSchedule();
        }

        private static void ParseStartDate()
        {
            ParsedStartDate = null;
            string startDateStr = ServerStartDateForAutomation.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(startDateStr)) return;
            if (DateTime.TryParseExact(startDateStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                ParsedStartDate = parsedDate;
                if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                    _logger?.LogInfo($"[GolemAutomationConfig] Parsed ServerStartDateForAutomation: {ParsedStartDate.Value:yyyy-MM-dd HH:mm:ss}");
            }
            else
                _logger?.LogError($"[GolemAutomationConfig] Invalid ServerStartDateForAutomation format: '{startDateStr}'.");
        }

        private static void ParseDayBasedSchedule()
        {
            ParsedDayBasedSchedule.Clear();
            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                _logger?.LogInfo("[GolemAutomationConfig] Parsing Golem Health Day-Based Schedule...");
            foreach (var kvp in _healthLevelConfigs)
            {
                if (kvp.Value.Enabled.Value && kvp.Value.Day.Value >= 0)
                {
                    if (ParsedDayBasedSchedule.ContainsKey(kvp.Value.Day.Value))
                        _logger?.LogWarning($"[GolemAutomationConfig] Day {kvp.Value.Day.Value} in schedule already has {ParsedDayBasedSchedule[kvp.Value.Day.Value]}, overwriting with {kvp.Key}.");
                    ParsedDayBasedSchedule[kvp.Value.Day.Value] = kvp.Key;
                }
            }
            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                _logger?.LogInfo($"[GolemAutomationConfig] Parsed {ParsedDayBasedSchedule.Count} day-based schedule entries.");
        }

        public static SiegeWeaponHealth? GetScheduledHealthForDay(int dayCount)
        {
            if (ParsedDayBasedSchedule.Count == 0) return null;
            var relevantEntry = ParsedDayBasedSchedule.Where(kvp => kvp.Key <= dayCount)
                                              .OrderByDescending(kvp => kvp.Key)
                                              .FirstOrDefault();
            if (ParsedDayBasedSchedule.Any(kvp => kvp.Key <= dayCount)) return relevantEntry.Value;
            return null;
        }
    }
}