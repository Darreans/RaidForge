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
        public static ConfigEntry<bool> ShowCurrentRaidingStatusInRaidStatus { get; private set; }

        public static ConfigEntry<string> RaidScheduleTimeZoneDisplayString { get; private set; }
        public static ConfigEntry<double> RaidScheduleDisplayOffsetHours { get; private set; }

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

            ShowCurrentRaidingStatusInRaidStatus = configFile.Bind(SECTION_GENERAL,
                "ShowCurrentRaidingStatusInRaidStatus",
                false,
                "If true, .raidstatus/.raids adds a second line per base showing whether that base is currently being raided/breached.");

            RaidScheduleTimeZoneDisplayString = configFile.Bind(SECTION_GENERAL,
                "RaidScheduleTimeZoneForDisplay",
                "Server Time",
                "Text label shown next to raid times in .raidtime/.raidt/.raiddays/.raidd. This label does not convert time; use RaidScheduleDisplayOffsetHours for a fixed adjustment.");

            RaidScheduleDisplayOffsetHours = configFile.Bind(SECTION_GENERAL,
                "RaidScheduleDisplayOffsetHours",
                0.0,
                "Fixed schedule offset, in hours, added to the dedicated server's local clock for raid checks and command output. The server clock remains authoritative. Positive values move RaidForge time later; negative values move it earlier. Example: server 08:00 with offset 2 is treated as 10:00. This does not adjust automatically for daylight saving time.");

            string defaultOffTime = "00:00";
            string defaultWeekendStartTime = "20:00";
            string defaultWeekendEndTime = "22:00";

            _dailyConfigs[DayOfWeek.Monday] = (
                BindDailyRaidTime(configFile, DayOfWeek.Monday, "StartTime", defaultOffTime),
                BindDailyRaidTime(configFile, DayOfWeek.Monday, "EndTime", defaultOffTime)
            );
            _dailyConfigs[DayOfWeek.Tuesday] = (BindDailyRaidTime(configFile, DayOfWeek.Tuesday, "StartTime", defaultOffTime), BindDailyRaidTime(configFile, DayOfWeek.Tuesday, "EndTime", defaultOffTime));
            _dailyConfigs[DayOfWeek.Wednesday] = (BindDailyRaidTime(configFile, DayOfWeek.Wednesday, "StartTime", defaultOffTime), BindDailyRaidTime(configFile, DayOfWeek.Wednesday, "EndTime", defaultOffTime));
            _dailyConfigs[DayOfWeek.Thursday] = (BindDailyRaidTime(configFile, DayOfWeek.Thursday, "StartTime", defaultOffTime), BindDailyRaidTime(configFile, DayOfWeek.Thursday, "EndTime", defaultOffTime));
            _dailyConfigs[DayOfWeek.Friday] = (BindDailyRaidTime(configFile, DayOfWeek.Friday, "StartTime", defaultWeekendStartTime), BindDailyRaidTime(configFile, DayOfWeek.Friday, "EndTime", defaultWeekendEndTime));
            _dailyConfigs[DayOfWeek.Saturday] = (BindDailyRaidTime(configFile, DayOfWeek.Saturday, "StartTime", defaultWeekendStartTime), BindDailyRaidTime(configFile, DayOfWeek.Saturday, "EndTime", defaultWeekendEndTime));
            _dailyConfigs[DayOfWeek.Sunday] = (BindDailyRaidTime(configFile, DayOfWeek.Sunday, "StartTime", defaultWeekendStartTime), BindDailyRaidTime(configFile, DayOfWeek.Sunday, "EndTime", defaultWeekendEndTime));

            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true && _logger != null) _logger.LogInfo("[RaidConfig] Initialized.");
        }

        public static DateTime GetRaidScheduleNow()
        {
            return DateTime.Now + GetRaidScheduleOffset();
        }

        public static TimeSpan GetRaidScheduleOffset()
        {
            double hours = RaidScheduleDisplayOffsetHours?.Value ?? 0.0;

            if (double.IsNaN(hours) || double.IsInfinity(hours))
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromHours(hours);
        }

        private static ConfigEntry<string> BindDailyRaidTime(ConfigFile configFile, DayOfWeek day, string suffix, string defaultValue)
        {
            string direction = suffix == "StartTime" ? "start" : "end";
            return configFile.Bind(
                SECTION_SCHEDULE,
                $"{day}{suffix}",
                defaultValue,
                $"Raid window {direction} time for {day} in HH:mm server-local time. Set both start and end to 00:00 for no raids. Use EndTime=24:00 to allow a full-day 00:00-24:00 raid window.");
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
                bool endTimeIsEndOfDay = string.Equals(endTimeStr, "24:00", StringComparison.Ordinal);
                bool treatEndTimeAsEndOfDay = endTimeIsEndOfDay || endTimeStr == "00:00" || string.IsNullOrEmpty(endTimeStr);

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

                bool spansMidnight = endTimeIsEndOfDay ||
                    (endTime < startTime && endTime != TimeSpan.Zero) ||
                    (startTime != TimeSpan.Zero && endTime == TimeSpan.Zero);


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
