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
                "Enable or disable the offline raid protection system against all damage types when fully active (after grace).");

            GracePeriodDurationMinutes = config.Bind(
                "OfflineRaidProtection",
                "GracePeriodMinutes",
                15.0f,
                "The duration (in minutes) of the grace period after all relevant defenders log off. " +
                "During this period, their base is still considered 'recently online' and vulnerable. " +
                "Set to 0 to have no grace period (full ORP protection may activate immediately if defenders are offline).");

            AnnounceOfflineRaidDuringGrace = config.Bind(
                "OfflineRaidProtection",
                "AnnounceOfflineRaidDuringGrace",
                true,
                "If true, a global announcement will be made when an offline base (within its grace period) " +
                "is being raided (30 second cool down).");

            AnnounceDecayedBaseRaid = config.Bind(
                "OfflineRaidProtection",
                "AnnounceDecayedBaseRaid",
                true,
                "If true, a global announcement will be made when a decayed base " +
                "is being damaged by a player (5 minute cool down)");
        }
    }
}