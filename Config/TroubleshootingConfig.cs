using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
    public static class TroubleshootingConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableTntDamageLogging { get; private set; }
        public static ConfigEntry<bool> EnableVerboseLogging { get; private set; }

        private const string SECTION_LOGGING = "Logging";

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
        {
            ConfigFileInstance = configFile;

            EnableTntDamageLogging = configFile.Bind(
                SECTION_LOGGING,
                "EnableTntDamageLogging",
                false,
                "Temporarily logs throttled castle-structure DealDamage events at Info level, including damage modifiers and the source ownership graph. Use this to identify TNT damage sources, then turn it off.");

            EnableVerboseLogging = configFile.Bind(
                SECTION_LOGGING,
                "EnableVerboseLogging",
                false,
                "Set to true to enable detailed informational logs from RaidForge for debugging various features. This can be performance intensive and should be off, unless you are troubleshooting something.");

            logger?.LogInfo("[TroubleshootingConfig] Initialized.");
        }
    }
}
