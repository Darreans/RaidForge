using BepInEx.Configuration;

namespace RaidForge.Config
{
    public static class OfflineRaidProtectionConfig
    {
        public static ConfigEntry<bool> EnableOfflineRaidProtection;
        public static ConfigEntry<float> GracePeriodDurationMinutes;
        public static ConfigEntry<bool> AnnounceOfflineRaidDuringGrace;
        public static ConfigEntry<bool> AnnounceDecayedBaseRaid;

        public static void Initialize(ConfigFile config)
        {
            EnableOfflineRaidProtection = config.Bind(
                "OfflineRaidProtection",
                "EnableOfflineProtection",
                true,
                "Enable or disable the offline raid protection system.");

            GracePeriodDurationMinutes = config.Bind(
                "OfflineRaidProtection",
                "GracePeriodMinutes",
                15.0f,
                "The duration (in minutes) of the grace period after all relevant defenders log off. " +
                "During this period, their base is still considered vulnerable, so they can be raided " +
                "Set to 0 to have no grace period, which means players can just log off and they will instantly have offline protection (not really recommended to have 0 set).");

            AnnounceOfflineRaidDuringGrace = config.Bind(
                "OfflineRaidProtection",
                "AnnounceOfflineRaidDuringGrace",
                true,
                "If true, a global announcement will be made when an offline base " +
                "is being raided. If you have offline protection and they are within their grace period, it will still announce.");

            AnnounceDecayedBaseRaid = config.Bind(
                "OfflineRaidProtection",
                "AnnounceDecayedBaseRaid",
                true,
                "If true, a global announcement will be made when a decayed base " +
                "is being damaged by a player");


        }
    }
}