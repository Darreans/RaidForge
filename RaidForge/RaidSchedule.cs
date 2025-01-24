using BepInEx.Configuration;
using System;
using System.Collections.Generic;

namespace RaidForge
{
    public static class Raidschedule
    {
        // "ForceOn" => always on, "ForceOff" => always off, "Normal" => day-of-week scheduling
        public static ConfigEntry<string> OverrideMode;

        // How often we do reflection checks (in seconds)
        public static ConfigEntry<int> RaidCheckInterval;

        // Day-of-week times
        public static ConfigEntry<string> MondayStart;
        public static ConfigEntry<string> MondayEnd;
        public static ConfigEntry<string> TuesdayStart;
        public static ConfigEntry<string> TuesdayEnd;
        public static ConfigEntry<string> WednesdayStart;
        public static ConfigEntry<string> WednesdayEnd;
        public static ConfigEntry<string> ThursdayStart;
        public static ConfigEntry<string> ThursdayEnd;
        public static ConfigEntry<string> FridayStart;
        public static ConfigEntry<string> FridayEnd;
        public static ConfigEntry<string> SaturdayStart;
        public static ConfigEntry<string> SaturdayEnd;
        public static ConfigEntry<string> SundayStart;
        public static ConfigEntry<string> SundayEnd;

        public static Dictionary<DayOfWeek, Raidwindow> Windows { get; private set; } = new();

        public static void Initialize(ConfigFile config)
        {
            // If already set, skip
            if (OverrideMode != null) return;

            OverrideMode = config.Bind(
                "RaidSchedule",
                "OverrideMode",
                "Normal",
                "ForceOn => always on, ForceOff => always off, Normal => day-of-week scheduling."
            );

            RaidCheckInterval = config.Bind(
                "RaidSchedule",
                "RaidCheckInterval",
                5,
                "How often (in seconds) we do reflection checks. If you want near-instant toggles, set to 1. You may not want to change this"
            );

            MondayStart  = config.Bind("RaidSchedule", "MondayStart", "20:00:00", "Monday start time");
            MondayEnd    = config.Bind("RaidSchedule", "MondayEnd", "22:00:00", "Monday end time");
            TuesdayStart = config.Bind("RaidSchedule", "TuesdayStart", "20:00:00", "Tuesday start time");
            TuesdayEnd   = config.Bind("RaidSchedule", "TuesdayEnd", "22:00:00", "Tuesday end time");
            WednesdayStart = config.Bind("RaidSchedule", "WednesdayStart", "20:00:00", "Wed start time");
            WednesdayEnd   = config.Bind("RaidSchedule", "WednesdayEnd", "22:00:00", "Wed end time");
            ThursdayStart = config.Bind("RaidSchedule", "ThursdayStart", "20:00:00", "Thu start time");
            ThursdayEnd   = config.Bind("RaidSchedule", "ThursdayEnd", "22:00:00", "Thu end time");
            FridayStart = config.Bind("RaidSchedule", "FridayStart", "20:00:00", "Fri start time");
            FridayEnd   = config.Bind("RaidSchedule", "FridayEnd", "22:00:00", "Fri end time");
            SaturdayStart = config.Bind("RaidSchedule", "SaturdayStart", "20:00:00", "Sat start time");
            SaturdayEnd   = config.Bind("RaidSchedule", "SaturdayEnd", "22:00:00", "Sat end time");
            SundayStart = config.Bind("RaidSchedule", "SundayStart", "20:00:00", "Sun start time");
            SundayEnd   = config.Bind("RaidSchedule", "SundayEnd", "22:00:00", "Sun end time");

            LoadFromConfig();
        }

        public static void LoadFromConfig()
        {
            Windows.Clear();

            ParseDay(DayOfWeek.Monday, MondayStart.Value, MondayEnd.Value);
            ParseDay(DayOfWeek.Tuesday, TuesdayStart.Value, TuesdayEnd.Value);
            ParseDay(DayOfWeek.Wednesday, WednesdayStart.Value, WednesdayEnd.Value);
            ParseDay(DayOfWeek.Thursday, ThursdayStart.Value, ThursdayEnd.Value);
            ParseDay(DayOfWeek.Friday, FridayStart.Value, FridayEnd.Value);
            ParseDay(DayOfWeek.Saturday, SaturdayStart.Value, SaturdayEnd.Value);
            ParseDay(DayOfWeek.Sunday, SundayStart.Value, SundayEnd.Value);
        }

        private static void ParseDay(DayOfWeek day, string startStr, string endStr)
        {
            if (TimeSpan.TryParse(startStr, out var start) &&
                TimeSpan.TryParse(endStr, out var end))
            {
                // If both 0 => no window for that day
                if (start == TimeSpan.Zero && end == TimeSpan.Zero) return;

                // If end=0 but start!=0 => treat as 24h
                if (end == TimeSpan.Zero && start != TimeSpan.Zero)
                {
                    end = new TimeSpan(24, 0, 0);
                }

                Windows[day] = new Raidwindow
                {
                    Day = day,
                    Start = start,
                    End = end
                };
            }
            else
            {
                RaidForgePlugin.Logger.LogWarning(
                    $"[Raidschedule] Invalid time for {day}: '{startStr}' '{endStr}'"
                );
            }
        }
    }
}
