using BepInEx.Configuration;

namespace RaidForge.Config
{
    public static class OfflineRaidProtectionConfig
    {
        public static ConfigEntry<bool> EnableOfflineRaidProtection;

        public static ConfigEntry<float> GracePeriodDurationMinutes;

        public static void Initialize(ConfigFile config)
        {
            EnableOfflineRaidProtection = config.Bind(
                "OfflineRaidProtection", 
                "Enabled",               
                true,                    
                "Enable or disable the offline raid protection system against Siege Golems.");

            GracePeriodDurationMinutes = config.Bind(
                "OfflineRaidProtection", 
                "GracePeriodMinutes",    
                15.0f,                   
                "The duration (in minutes) of the grace period after all members of a clan log off if in clan or solo players not in clan. " +
                "During this period, their base is still considered 'online' or 'recently online' for the purpose of Siege Golem offline protection. " +
                "Set to 0 to have no grace period (protection activates immediately once the service confirms they are offline). This is not recommended as abuse can occur");

        }
    }
}