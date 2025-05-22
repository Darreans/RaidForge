using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
    public static class OfflineRaidProtectionConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; } 

        public static ConfigEntry<bool> EnableOfflineRaidProtection { get; private set; }

        private const string SECTION_MAIN = "Offline Raid Protection";

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null) 
        {
            ConfigFileInstance = configFile;

            EnableOfflineRaidProtection = configFile.Bind(
                SECTION_MAIN,
                "EnableOfflineProtection",
                false,
                "If true, offline raid protection logic (including grace periods) will be active. If false, bases are raidable regardless of owner online status (respecting global raid windows).");

            logger?.LogInfo("[OfflineRaidProtectionConfig] Initialized.");
        }
    }
}