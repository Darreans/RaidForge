using BepInEx.Configuration;
using System;
using System.Collections.Generic;

namespace RaidForge.Config
{
    public static class OfflineRaidProtectionConfig
    {
        public static ConfigEntry<bool> EnableOfflineRaidProtection;
        public static ConfigEntry<bool> EnableOfflineProtectionDaySchedule;
        public static Dictionary<DayOfWeek, ConfigEntry<bool>> DayProtectionToggles { get; private set; }
        public static ConfigEntry<float> GracePeriodDurationMinutes;
        public static ConfigEntry<bool> AnnounceOfflineRaid;
        public static ConfigEntry<bool> AnnounceDecayedBaseRaid;

        public static void Initialize(ConfigFile config)
        {
            DayProtectionToggles = new Dictionary<DayOfWeek, ConfigEntry<bool>>();

            EnableOfflineRaidProtection = config.Bind(
                "OfflineRaidProtection",
                "EnableOfflineProtection",
                true,
                "Enable or disable the offline raid protection system.");

            EnableOfflineProtectionDaySchedule = config.Bind(
                "OfflineRaidProtection",
                "EnableOfflineProtectionDaySchedule",
                false,
                "If true, Offline Raid Protection only blocks damage on the enabled days below. Offline timestamps are still tracked on disabled days.");

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                DayProtectionToggles[day] = config.Bind(
                    "OfflineProtectionDays",
                    $"{day}EnableOfflineProtection",
                    true,
                    $"If EnableOfflineProtectionDaySchedule is true, this controls whether Offline Raid Protection blocks damage on {day}.");
            }

            GracePeriodDurationMinutes = config.Bind(
                "OfflineRaidProtection",
                "GracePeriodMinutes",
                15.0f,
                "The duration, in minutes, of the grace period after all relevant defenders log off. " +
                "During this period, their base is still considered vulnerable and can be raided. " +
                "Set to 0 for instant offline protection when players log off.");

            AnnounceOfflineRaid = config.Bind(
                "OfflineRaidProtection",
                "AnnounceOfflineRaid",
                true,
                "If true, a global announcement will be made when a base whose defenders are all offline is attacked with siege damage. " +
                "This can work even when Offline Raid Protection is disabled, allowing normal scheduled raiding servers to still announce offline raids.");

            AnnounceDecayedBaseRaid = config.Bind(
                "OfflineRaidProtection",
                "AnnounceDecayedBaseRaid",
                true,
                "If true, a global announcement will be made when a decayed base is being damaged by a player.");
        }

        public static bool IsOfflineProtectionAllowedToday()
        {
            return IsOfflineProtectionAllowedOn(DateTime.Now.DayOfWeek);
        }

        public static bool IsOfflineProtectionAllowedOn(DayOfWeek day)
        {
            if (!(EnableOfflineRaidProtection?.Value ?? false))
            {
                return false;
            }

            if (!(EnableOfflineProtectionDaySchedule?.Value ?? false))
            {
                return true;
            }

            return DayProtectionToggles != null &&
                DayProtectionToggles.TryGetValue(day, out var entry)
                    ? entry.Value
                    : true;
        }
    }
}
