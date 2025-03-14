﻿using BepInEx.Configuration;
using System;
using System.Collections.Generic;

namespace RaidForge
{
    public static class Raidschedule
    {
        public static ConfigEntry<string> OverrideMode;

        public static ConfigEntry<bool> GolemAutomationEnabled;
        public static ConfigEntry<string> GolemStartDateString;
        public static ConfigEntry<string> GolemDayToHealthMap;

     
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
            // Only do thionce
            if (OverrideMode != null) return;

            // ============= Basic Raid Schedule Config Entries =============
            OverrideMode = config.Bind(
                "RaidSchedule",
                "OverrideMode",
                "Normal",
                "ForceOn => always on, ForceOff => always off, Normal => day-of-week scheduling."
            );

            MondayStart  = config.Bind("RaidSchedule", "MondayStart", "20:00:00", "Monday start time");
            MondayEnd    = config.Bind("RaidSchedule", "MondayEnd", "22:00:00", "Monday end time");
            TuesdayStart = config.Bind("RaidSchedule", "TuesdayStart", "20:00:00", "Tuesday start time");
            TuesdayEnd   = config.Bind("RaidSchedule", "TuesdayEnd", "22:00:00", "Tuesday end time");
            WednesdayStart = config.Bind("RaidSchedule", "WednesdayStart", "20:00:00", "Wed start time");
            WednesdayEnd   = config.Bind("RaidSchedule", "WednesdayEnd", "22:00:00", "Wed end time");
            ThursdayStart = config.Bind("RaidSchedule", "ThursdayStart", "20:00:00", "Thu start time");
            ThursdayEnd   = config.Bind("RaidSchedule", "ThursdayEnd", "22:00:00", "Thu end time");
            FridayStart   = config.Bind("RaidSchedule", "FridayStart", "20:00:00", "Fri start time");
            FridayEnd     = config.Bind("RaidSchedule", "FridayEnd", "22:00:00", "Fri end time");
            SaturdayStart = config.Bind("RaidSchedule", "SaturdayStart", "20:00:00", "Sat start time");
            SaturdayEnd   = config.Bind("RaidSchedule", "SaturdayEnd", "22:00:00", "Sat end time");
            SundayStart   = config.Bind("RaidSchedule", "SundayStart", "20:00:00", "Sun start time");
            SundayEnd     = config.Bind("RaidSchedule", "SundayEnd", "22:00:00", "Sun end time");

          
            GolemAutomationEnabled = config.Bind(
                "GolemAutomation",
                "Enabled",
                false,
                "Whether Golem day-based HP automation is enabled (on/off)."
            );

           
            GolemStartDateString = config.Bind(
                "GolemAutomation",
                "StartDate",
                "",
                "Date/time for day-0 in 'yyyy-MM-dd HH:mm:ss' format. If empty, no date is set."
            );

            GolemDayToHealthMap = config.Bind(
                "GolemAutomation",
                "DayToHealthMap",
                "0=Low,1=Normal,2=High,3=VeryHigh",
                "Comma-separated list of day=SiegeWeaponHealth pairs."
            );

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
                if (start == TimeSpan.Zero && end == TimeSpan.Zero) return;

                // If end is "0", treat that as 24:00
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
