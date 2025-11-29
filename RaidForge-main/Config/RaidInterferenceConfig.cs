using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
    public static class RaidInterferenceConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableRaidInterference { get; private set; }

        private const string SECTION_MAIN = "Raid Interference";

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
        {
            ConfigFileInstance = configFile;

            EnableRaidInterference = configFile.Bind(
                SECTION_MAIN,
                "EnableRaidInterference",
                true,
                "If true, the system that applies debuffs to non-participating players ('interlopers') during an active siege will be active.");

            logger?.LogInfo("[RaidInterferenceConfig] Initialized.");
        }
    }
}