using BepInEx.Configuration;

namespace RaidForge.Config
{
    public static class OptInRaidingConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableOptInRaiding { get; private set; }
        public static ConfigEntry<int> OptInLockDurationHours { get; private set; }

        private const string SECTION_MAIN = "Opt-In Raiding";

        public static void Initialize(ConfigFile configFile)
        {
            ConfigFileInstance = configFile;

            EnableOptInRaiding = configFile.Bind(
                SECTION_MAIN,
                "EnableOptInRaiding",
                false,
                "If true, this system is active. By default, all bases are protected unless players/clans use the .raidoptin command.\n" +
                "IMPORTANT: If this is true, the main OfflineRaidProtection system will be IGNORED.");

            OptInLockDurationHours = configFile.Bind(
                SECTION_MAIN,
                "OptInLockDurationHours",
                24,
                "The number of hours a player/clan must remain opted-in to raiding after using the .raidoptin command. They cannot opt-out during this time.");
        }
    }
}