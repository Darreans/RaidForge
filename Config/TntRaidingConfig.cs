using BepInEx.Configuration;
using BepInEx.Logging;
using RaidForge.Data;

namespace RaidForge.Config
{
    public static class TntRaidingConfig
    {
        private const string RaidingSection = "01 - TNT Castle Raiding";
        private const string NormalDamageSection = "02 - Normal TNT Damage";
        private const string CastleDamageSection = "03 - Castle Wall Damage";

        public static ConfigFile ConfigFileInstance { get; private set; }
        public static ConfigEntry<bool> EnableTntRaiding { get; private set; }
        public static ConfigEntry<float> T01NormalDamagePercent { get; private set; }
        public static ConfigEntry<float> T02NormalDamagePercent { get; private set; }
        public static ConfigEntry<float> T01CastleWallDamagePercent { get; private set; }
        public static ConfigEntry<float> T02CastleWallDamagePercent { get; private set; }
        public static ConfigEntry<bool> UseNormalTntDamageAfterBreach { get; private set; }

        public static void Initialize(ConfigFile configFile, ManualLogSource logger = null)
        {
            ConfigFileInstance = configFile;

            EnableTntRaiding = configFile.Bind(
                RaidingSection,
                "EnableTntRaiding",
                false,
                "If true, player-placed T01/T02 explosives can damage connected stone castle structures. Raid schedules, ORP, purchased protection, and opt-in rules still apply.");

            T01NormalDamagePercent = BindPercent(
                configFile,
                NormalDamageSection,
                "T01NormalDamagePercent",
                "Scales all ordinary T01 explosive damage. 100 = native damage, 10 = 10% of native damage, and 110 = 10% above native damage.");

            T02NormalDamagePercent = BindPercent(
                configFile,
                NormalDamageSection,
                "T02NormalDamagePercent",
                "Scales all ordinary T02 explosive damage. 100 = native damage, 10 = 10% of native damage, and 110 = 10% above native damage.");

            T01CastleWallDamagePercent = BindPercent(
                configFile,
                CastleDamageSection,
                "T01CastleWallDamagePercent",
                "T01 damage against non-breached stone castle structures when TNT raiding is enabled. This is a percentage of native T01 explosive damage and does not multiply the normal-damage percentage.");

            T02CastleWallDamagePercent = BindPercent(
                configFile,
                CastleDamageSection,
                "T02CastleWallDamagePercent",
                "T02 damage against non-breached stone castle structures when TNT raiding is enabled. This is a percentage of native T02 explosive damage and does not multiply the normal-damage percentage.");

            UseNormalTntDamageAfterBreach = configFile.Bind(
                CastleDamageSection,
                "UseNormalTntDamageAfterBreach",
                true,
                "If true, breached castle structures use that TNT tier's NormalDamagePercent. If false, they continue using that tier's CastleWallDamagePercent.");

            logger?.LogInfo("[TntRaidingConfig] TNT normal-damage and castle-raiding settings initialized.");
        }

        public static float GetNormalDamagePercent(TntTier tier)
        {
            return tier == TntTier.T01
                ? T01NormalDamagePercent?.Value ?? 100f
                : T02NormalDamagePercent?.Value ?? 100f;
        }

        public static float GetCastleWallDamagePercent(TntTier tier)
        {
            return tier == TntTier.T01
                ? T01CastleWallDamagePercent?.Value ?? 100f
                : T02CastleWallDamagePercent?.Value ?? 100f;
        }

        public static void MigrateLegacySettingsIfNeeded(
            ConfigFile legacyWeaponConfig,
            bool tntConfigExistedBeforeLoad,
            ManualLogSource logger = null)
        {
            if (legacyWeaponConfig == null || tntConfigExistedBeforeLoad)
            {
                return;
            }

            var legacyEnabledDefinition = new ConfigDefinition("Weapon Raiding", "EnableTntRaiding");
            var legacyPercentDefinition = new ConfigDefinition("Weapon Raiding", "TntPreBreachDamagePercent");

            ConfigEntry<bool> legacyEnabled = legacyWeaponConfig.Bind(
                legacyEnabledDefinition,
                false,
                new ConfigDescription(
                    "Legacy RaidForge TNT setting. Migrated automatically to TntDamageAndRaiding.cfg."));

            ConfigEntry<float> legacyPercent = legacyWeaponConfig.Bind(
                legacyPercentDefinition,
                100f,
                new ConfigDescription(
                    "Legacy RaidForge TNT setting. Migrated automatically to TntDamageAndRaiding.cfg."));

            EnableTntRaiding.Value = legacyEnabled.Value;
            T01CastleWallDamagePercent.Value = legacyPercent.Value;
            T02CastleWallDamagePercent.Value = legacyPercent.Value;

            legacyWeaponConfig.Remove(legacyEnabledDefinition);
            legacyWeaponConfig.Remove(legacyPercentDefinition);
            legacyWeaponConfig.Save();
            ConfigFileInstance.Save();

            logger?.LogInfo(
                $"[TntRaidingConfig] Migrated legacy TNT settings: enabled={EnableTntRaiding.Value}, " +
                $"T01/T02 castle damage={legacyPercent.Value}%.");
        }

        private static ConfigEntry<float> BindPercent(
            ConfigFile configFile,
            string section,
            string key,
            string description)
        {
            return configFile.Bind(
                section,
                key,
                100f,
                new ConfigDescription(
                    description,
                    new AcceptableValueRange<float>(0f, 1000f)));
        }
    }
}
