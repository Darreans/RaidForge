using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RaidForge.Config
{
    public struct RaidScheduleEntry
    {
        public DayOfWeek Day;
        public TimeSpan StartTime;
        public TimeSpan EndTime;
        public bool SpansMidnight;
    }

    public static class RaidConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static List<RaidScheduleEntry> Schedule { get; private set; }
        public static ConfigEntry<bool> AllowWaygateTeleports { get; private set; }

        public static ConfigEntry<string> RaidScheduleTimeZoneDisplayString { get; private set; }

        private static ManualLogSource _logger;
        private static Dictionary<DayOfWeek, (ConfigEntry<string> Start, ConfigEntry<string> End)> _dailyConfigs;

        public const string SECTION_GENERAL = "General";
        public const string SECTION_SCHEDULE = "DailyRaidSchedule";

        public static void Initialize(ConfigFile configFile, ManualLogSource logger)
        {
            ConfigFileInstance = configFile;
            _logger = logger;
            Schedule = new List<RaidScheduleEntry>();
            _dailyConfigs = new Dictionary<DayOfWeek, (ConfigEntry<string> Start, ConfigEntry<string> End)>();

            AllowWaygateTeleports = configFile.Bind(SECTION_GENERAL, "AllowWaygateTeleportsDuringRaid", true,
                "Allow teleportation via Waygates during an active raid window (if global raids are ON).");

            RaidScheduleTimeZoneDisplayString = configFile.Bind(SECTION_GENERAL,
                "RaidScheduleTimeZoneForDisplay", 
                "Server Time", 
                "The timezone string (e.g., EST, PST, UTC, Server Time) to display next to raid times in the .raiddays command. Leave empty if no timezone should be shown.");

            string defaultOffTime = "00:00";
            string defaultWeekendStartTime = "20:00";
            string defaultWeekendEndTime = "22:00";

            _dailyConfigs[DayOfWeek.Monday] = (
                configFile.Bind(SECTION_SCHEDULE, "MondayStartTime", defaultOffTime),
                configFile.Bind(SECTION_SCHEDULE, "MondayEndTime", defaultOffTime)
            );
            _dailyConfigs[DayOfWeek.Tuesday] = (configFile.Bind(SECTION_SCHEDULE, "TuesdayStartTime", defaultOffTime), configFile.Bind(SECTION_SCHEDULE, "TuesdayEndTime", defaultOffTime));
            _dailyConfigs[DayOfWeek.Wednesday] = (configFile.Bind(SECTION_SCHEDULE, "WednesdayStartTime", defaultOffTime), configFile.Bind(SECTION_SCHEDULE, "WednesdayEndTime", defaultOffTime));
            _dailyConfigs[DayOfWeek.Thursday] = (configFile.Bind(SECTION_SCHEDULE, "ThursdayStartTime", defaultOffTime), configFile.Bind(SECTION_SCHEDULE, "ThursdayEndTime", defaultOffTime));
            _dailyConfigs[DayOfWeek.Friday] = (configFile.Bind(SECTION_SCHEDULE, "FridayStartTime", defaultWeekendStartTime), configFile.Bind(SECTION_SCHEDULE, "FridayEndTime", defaultWeekendEndTime));
            _dailyConfigs[DayOfWeek.Saturday] = (configFile.Bind(SECTION_SCHEDULE, "SaturdayStartTime", defaultWeekendStartTime), configFile.Bind(SECTION_SCHEDULE, "SaturdayEndTime", defaultWeekendEndTime));
            _dailyConfigs[DayOfWeek.Sunday] = (configFile.Bind(SECTION_SCHEDULE, "SundayStartTime", defaultWeekendStartTime), configFile.Bind(SECTION_SCHEDULE, "SundayEndTime", defaultWeekendEndTime));

            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true && _logger != null) _logger.LogInfo("[RaidConfig] Initialized.");
        }

        public static void ParseSchedule()
        {
            if (_logger == null || _dailyConfigs == null)
            {
                Console.WriteLine("[RaidConfig] CRITICAL: RaidConfig not properly initialized before parsing schedule.");
                Schedule = new List<RaidScheduleEntry>();
                return;
            }
            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true) _logger.LogInfo("[RaidConfig] Parsing raid schedule from configuration...");

            var newSchedule = new List<RaidScheduleEntry>();
            var days = Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>();
            foreach (var day in days)
            {
                if (!_dailyConfigs.TryGetValue(day, out var configPair)) { continue; }
                var startTimeStr = configPair.Start.Value?.Trim() ?? "00:00";
                var endTimeStr = configPair.End.Value?.Trim() ?? "00:00";

             
                if (startTimeStr == "00:00" && (endTimeStr == "00:00" || string.IsNullOrEmpty(endTimeStr)))
                {
                    continue;
                }

                if (!TimeSpan.TryParseExact(startTimeStr, "h\\:mm", CultureInfo.InvariantCulture, out var startTime) &&
                    !TimeSpan.TryParseExact(startTimeStr, "hh\\:mm", CultureInfo.InvariantCulture, out startTime))
                {
                    if (TroubleshootingConfig.EnableVerboseLogging?.Value == true) _logger.LogWarning($"[RaidConfig] Could not parse start time '{startTimeStr}' for {day}. Skipping entry.");
                    continue;
                }

                TimeSpan endTime;
                bool treatEndTimeAsEndOfDay = (endTimeStr == "00:00" || string.IsNullOrEmpty(endTimeStr));

                if (treatEndTimeAsEndOfDay)
                {
                    endTime = TimeSpan.Zero;
                }
                else if (!TimeSpan.TryParseExact(endTimeStr, "h\\:mm", CultureInfo.InvariantCulture, out endTime) &&
                         !TimeSpan.TryParseExact(endTimeStr, "hh\\:mm", CultureInfo.InvariantCulture, out endTime))
                {
                    if (TroubleshootingConfig.EnableVerboseLogging?.Value == true) _logger.LogWarning($"[RaidConfig] Could not parse end time '{endTimeStr}' for {day}. Skipping entry.");
                    continue;
                }

                bool spansMidnight = (endTime < startTime && endTime != TimeSpan.Zero) || (startTime != TimeSpan.Zero && endTime == TimeSpan.Zero);


                newSchedule.Add(new RaidScheduleEntry { Day = day, StartTime = startTime, EndTime = endTime, SpansMidnight = spansMidnight });

                if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                {
                    string endDisplay = (endTime == TimeSpan.Zero && spansMidnight) ? "Midnight" : endTime.ToString("hh\\:mm");
                    _logger.LogInfo($"[RaidConfig] Parsed schedule entry: {day} {startTime:hh\\:mm} - {endDisplay}{(spansMidnight ? " (spans midnight)" : "")}");
                }
            }
            Schedule = newSchedule;
            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true) _logger.LogInfo($"[RaidConfig] Total raid schedule entries parsed: {Schedule.Count}");
        }
    }
}