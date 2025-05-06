using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RaidForge
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
        public static List<RaidScheduleEntry> Schedule { get; private set; }
        public static ConfigEntry<bool> EnableVerboseLogging { get; private set; }

        private static ManualLogSource _logger;
        private static ConfigEntry<string> _mondayStartTime, _mondayEndTime;
        private static ConfigEntry<string> _tuesdayStartTime, _tuesdayEndTime;
        private static ConfigEntry<string> _wednesdayStartTime, _wednesdayEndTime;
        private static ConfigEntry<string> _thursdayStartTime, _thursdayEndTime;
        private static ConfigEntry<string> _fridayStartTime, _fridayEndTime;
        private static ConfigEntry<string> _saturdayStartTime, _saturdayEndTime;
        private static ConfigEntry<string> _sundayStartTime, _sundayEndTime;
        private static Dictionary<DayOfWeek, (ConfigEntry<string> Start, ConfigEntry<string> End)> _dailyConfigs;

        private const string SECTION_SCHEDULE = "Daily Schedule";

        public static void Initialize(ConfigFile config, ManualLogSource logger)
        {
            _logger = logger;
            Schedule = new List<RaidScheduleEntry>();
            _dailyConfigs = new Dictionary<DayOfWeek, (ConfigEntry<string> Start, ConfigEntry<string> End)>();

            string defaultOffTime = "00:00";
            string defaultWeekendStartTime = "20:00";
            string defaultWeekendEndTime = "22:00";


            _mondayStartTime = config.Bind(SECTION_SCHEDULE, "MondayStartTime", defaultOffTime, "Raid Start Time for Monday. Format: HH:mm (24-hour). Use 00:00 to disable.");
            _mondayEndTime = config.Bind(SECTION_SCHEDULE, "MondayEndTime", defaultOffTime, "Raid End Time for Monday. Format: HH:mm (24-hour). Use 00:00 for midnight end.");
            _dailyConfigs[DayOfWeek.Monday] = (_mondayStartTime, _mondayEndTime);

            _tuesdayStartTime = config.Bind(SECTION_SCHEDULE, "TuesdayStartTime", defaultOffTime, "Raid Start Time for Tuesday.");
            _tuesdayEndTime = config.Bind(SECTION_SCHEDULE, "TuesdayEndTime", defaultOffTime, "Raid End Time for Tuesday.");
            _dailyConfigs[DayOfWeek.Tuesday] = (_tuesdayStartTime, _tuesdayEndTime);

            _wednesdayStartTime = config.Bind(SECTION_SCHEDULE, "WednesdayStartTime", defaultOffTime, "Raid Start Time for Wednesday.");
            _wednesdayEndTime = config.Bind(SECTION_SCHEDULE, "WednesdayEndTime", defaultOffTime, "Raid End Time for Wednesday.");
            _dailyConfigs[DayOfWeek.Wednesday] = (_wednesdayStartTime, _wednesdayEndTime);

            _thursdayStartTime = config.Bind(SECTION_SCHEDULE, "ThursdayStartTime", defaultOffTime, "Raid Start Time for Thursday.");
            _thursdayEndTime = config.Bind(SECTION_SCHEDULE, "ThursdayEndTime", defaultOffTime, "Raid End Time for Thursday.");
            _dailyConfigs[DayOfWeek.Thursday] = (_thursdayStartTime, _thursdayEndTime);

            _fridayStartTime = config.Bind(SECTION_SCHEDULE, "FridayStartTime", defaultOffTime, "Raid Start Time for Friday.");
            _fridayEndTime = config.Bind(SECTION_SCHEDULE, "FridayEndTime", defaultOffTime, "Raid End Time for Friday. Use 00:00 for midnight end (e.g., 02:00 means ends Sat 2 AM).");
            _dailyConfigs[DayOfWeek.Friday] = (_fridayStartTime, _fridayEndTime);

            _saturdayStartTime = config.Bind(SECTION_SCHEDULE, "SaturdayStartTime", defaultWeekendStartTime, "Raid Start Time for Saturday.");
            _saturdayEndTime = config.Bind(SECTION_SCHEDULE, "SaturdayEndTime", defaultWeekendEndTime, "Raid End Time for Saturday.");
            _dailyConfigs[DayOfWeek.Saturday] = (_saturdayStartTime, _saturdayEndTime);

            _sundayStartTime = config.Bind(SECTION_SCHEDULE, "SundayStartTime", defaultWeekendStartTime, "Raid Start Time for Sunday.");
            _sundayEndTime = config.Bind(SECTION_SCHEDULE, "SundayEndTime", defaultWeekendEndTime, "Raid End Time for Sunday.");
            _dailyConfigs[DayOfWeek.Sunday] = (_sundayStartTime, _sundayEndTime);

            EnableVerboseLogging = config.Bind(SECTION_SCHEDULE, "EnableVerboseLogging", false,
                "Set to true to enable detailed informational logs for debugging raid schedule checks.");

            if (EnableVerboseLogging.Value) _logger.LogInfo("Raid schedule configuration settings bound.");
        }

        public static void ParseSchedule()
        {
            if (_logger == null || _dailyConfigs == null)
            {
                Console.WriteLine("[RaidForge] CRITICAL: RaidConfig not initialized before parsing schedule.");
                Schedule = new List<RaidScheduleEntry>();
                return;
            }

            if (EnableVerboseLogging.Value) _logger.LogInfo("Parsing raid schedule from configuration...");
            var newSchedule = new List<RaidScheduleEntry>();
            var days = Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>();

            foreach (var day in days)
            {
                if (!_dailyConfigs.TryGetValue(day, out var configPair))
                {
                    _logger.LogError($"Configuration bindings not found for {day}. Skipping.");
                    continue;
                }

                var startTimeStr = configPair.Start.Value.Trim();
                var endTimeStr = configPair.End.Value.Trim();

                if (startTimeStr == "00:00")
                {
                    continue;
                }

                if (!TimeSpan.TryParseExact(startTimeStr, "h\\:mm", CultureInfo.InvariantCulture, out var startTime) &&
                    !TimeSpan.TryParseExact(startTimeStr, "hh\\:mm", CultureInfo.InvariantCulture, out startTime))
                {
                    _logger.LogError($"Invalid StartTime format for {day}: '{startTimeStr}'. Expected HH:mm. Skipping day.");
                    continue;
                }

                TimeSpan endTime;
                bool treatEndTimeAsMidnight = false;

                if (endTimeStr == "00:00")
                {
                    endTime = TimeSpan.Zero;
                    treatEndTimeAsMidnight = true;
                }
                else if (!TimeSpan.TryParseExact(endTimeStr, "h\\:mm", CultureInfo.InvariantCulture, out endTime) &&
                         !TimeSpan.TryParseExact(endTimeStr, "hh\\:mm", CultureInfo.InvariantCulture, out endTime))
                {
                    _logger.LogError($"Invalid EndTime format for {day}: '{endTimeStr}'. Expected HH:mm. Skipping day.");
                    continue;
                }

                bool spansMidnight = treatEndTimeAsMidnight || (endTime < startTime);

                newSchedule.Add(new RaidScheduleEntry { Day = day, StartTime = startTime, EndTime = endTime, SpansMidnight = spansMidnight });
                if (EnableVerboseLogging.Value) _logger.LogInfo($"Parsed schedule entry: {day} {startTime:hh\\:mm} - {endTime:hh\\:mm}{(spansMidnight ? " (spans midnight)" : "")}");
            }

            Schedule = newSchedule;
            if (EnableVerboseLogging.Value) _logger.LogInfo($"Total raid schedule entries parsed: {Schedule.Count}");
        }
    }
}
