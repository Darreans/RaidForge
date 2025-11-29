using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaidForge.Config
{
    public static class WeaponRaidingConfig
    {
        public static ConfigFile ConfigFileInstance { get; private set; }
        public static ConfigEntry<bool> EnableWeaponRaiding { get; private set; }
        public static ConfigEntry<float> WeaponDamageVsStoneMultiplier { get; private set; }

        private const string SECTION_MAIN = "Weapon Raiding";

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
        {
            ConfigFileInstance = configFile;

            EnableWeaponRaiding = configFile.Bind(
                SECTION_MAIN,
                "EnableWeaponRaiding",
                false, 
                "If true, players can damage castle walls/structures with regular weapons and explosives without a Siege Golem.");

            WeaponDamageVsStoneMultiplier = configFile.Bind(
                SECTION_MAIN,
                "WeaponDamageVsStoneMultiplier",
                0.5f,
                "The damage multiplier against Stone Structures (Walls, Doors). \n" +
                "0.0 = No Damage (Vanilla).\n" +
                "1.0 = Full Weapon Damage (Like hitting a tree).\n" +
                "0.5 = Half Damage (Recommended to keep Golems relevant).");

            if (logger != null) logger.LogInfo("[WeaponRaidingConfig] Initialized.");
        }
    }
}