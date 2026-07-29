using BepInEx.Configuration;

using RaidForge.Utils;

namespace RaidForge.Config
{
    public static class OptInRaidingConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }

        public static ConfigEntry<bool> EnableOptInRaiding { get; private set; }
        public static ConfigEntry<bool> DefaultEveryoneOptedOut { get; private set; }
        public static ConfigEntry<bool> DefaultEveryoneOptedIn { get; private set; }
        public static ConfigEntry<int> OptInLockDurationHours { get; private set; }
        public static ConfigEntry<bool> BlockOptInChangesDuringRaidHours { get; private set; }

        public static ConfigEntry<bool> AutoOptOutAfterCooldown { get; private set; }
        public static ConfigEntry<bool> AutoOptInShardHolders { get; private set; }

        private const string SECTION_MAIN = "Opt-In Raiding";

        public static bool UseDefaultOptInMode =>
            DefaultEveryoneOptedIn?.Value == true &&
            DefaultEveryoneOptedOut?.Value != true;

        public static void Initialize(ConfigFile configFile)
        {
            ConfigFileInstance = configFile;

            EnableOptInRaiding = configFile.Bind(SECTION_MAIN, "EnableOptInRaiding", false,
                "If true, this system is active only when Offline Raid Protection is disabled. By default, all bases are protected unless players/clans use the .raidoptin command.\n" +
                "IMPORTANT: Offline Raid Protection takes priority if both systems are enabled.");

            DefaultEveryoneOptedOut = configFile.Bind(SECTION_MAIN, "DefaultEveryoneOptedOut", true,
                "If true, everyone starts opted-out/protected and only .raidoptin entries are saved. This is the normal/default mode.");

            DefaultEveryoneOptedIn = configFile.Bind(SECTION_MAIN, "DefaultEveryoneOptedIn", false,
                "If true and DefaultEveryoneOptedOut is false, everyone starts opted-in/raidable and only .raidoptout entries are saved.");

            OptInLockDurationHours = configFile.Bind(SECTION_MAIN, "OptInLockDurationHours", 24,
                "The number of hours a player/clan must wait before switching back after choosing a manual raid status.");

            BlockOptInChangesDuringRaidHours = configFile.Bind(SECTION_MAIN, "BlockOptInChangesDuringRaidHours", true,
                "If true, players/clans cannot use .raidoptin or .raidoptout while a raid window is active.");

            AutoOptOutAfterCooldown = configFile.Bind(SECTION_MAIN, "AutoOptOutAfterCooldown", false,
                "If true, players/clans are automatically opted-out the moment their lock duration expires.");

            AutoOptInShardHolders = configFile.Bind(SECTION_MAIN, "AutoOptInShardHolders", true,
                "If true, holding a soul shard automatically treats you as opted-in during raid windows.");

            NormalizeDefaultMode();
        }

        private static void NormalizeDefaultMode()
        {
            if (DefaultEveryoneOptedOut.Value && DefaultEveryoneOptedIn.Value)
            {
                DefaultEveryoneOptedIn.Value = false;
                ConfigFileInstance.Save();
                LoggingHelper.Warning("[OptInRaidingConfig] DefaultEveryoneOptedOut and DefaultEveryoneOptedIn were both enabled. DefaultEveryoneOptedOut wins, so DefaultEveryoneOptedIn was disabled.");
            }
        }
    }
}
