using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
    public static class TroubleshootingConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableVerboseLogging { get; private set; }

        private const string SECTION_LOGGING = "Logging";

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
        {
            ConfigFileInstance = configFile;

            EnableVerboseLogging = configFile.Bind(
                SECTION_LOGGING,
                "EnableVerboseLogging",
                false,
                "Set to true to enable detailed informational logs from RaidForge for debugging various features. This can be performance intensive.");

            logger?.LogInfo("[TroubleshootingConfig] Initialized.");
        }
    }
}