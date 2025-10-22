using BepInEx.Configuration;
using System;
using System.Collections.Generic;

namespace RaidForge.Config
{
    public static class OptInScheduleConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableOptInSchedule { get; private set; }

        public static Dictionary<DayOfWeek, ConfigEntry<bool>> DayToggles { get; private set; }

        private const string SECTION_MAIN = "Opt-In Schedule";

        public static void Initialize(ConfigFile configFile)
        {
            ConfigFileInstance = configFile;
            DayToggles = new Dictionary<DayOfWeek, ConfigEntry<bool>>();

            EnableOptInSchedule = configFile.Bind(
                SECTION_MAIN,
                "EnableOptInSchedule",
                false,
                "If true, the daily schedule below is used. If false, the standard opt-in system is used.");

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                bool defaultValue = (day >= DayOfWeek.Monday && day <= DayOfWeek.Friday);

                DayToggles[day] = configFile.Bind(
                    SECTION_MAIN,
                    $"{day}AllowOptInSystem",
                    defaultValue,
                    $"If true, the opt-in/opt-out system works normally on {day}. If false, everyone is raidable on {day}.");
            }
        }

        public static bool IsOptInSystemAllowedToday()
        {
            DayOfWeek today = DateTime.Now.DayOfWeek;
            if (DayToggles.TryGetValue(today, out var configEntry))
            {
                return configEntry.Value;
            }

            return true;
        }
    }
}