using BepInEx.Configuration;
using RaidForge.Data;
using System.Globalization;

namespace RaidForge.Config
{
    public static class MapIconsConfig
    {
        public static ConfigEntry<bool> EnableOfflineRaidMapIcon;
        public static ConfigEntry<string> OfflineRaidMapIconPrefab;

        public static ConfigEntry<bool> EnableDecayRaidMapIcon;
        public static ConfigEntry<string> DecayRaidMapIconPrefab;

        public static ConfigEntry<bool> EnableOptInRaidMapIcon;
        public static ConfigEntry<bool> EnableOptOutRaidMapIcon;

        public static ConfigEntry<int> RaidMapIconTimeoutSeconds;

        public static void Initialize(ConfigFile config)
        {
            EnableOfflineRaidMapIcon = config.Bind("MapIcons", "EnableOfflineRaidMapIcon", true, "Display a map icon on the map when an offline base is being raided.");
            OfflineRaidMapIconPrefab = config.Bind(
                "MapIcons",
                "OfflineRaidMapIconPrefab",
                GetLegacyMapIconPrefabValue(config, "OfflineRaidMapIconPrefabGuid", MapIconPrefabCatalog.DefaultRaidForgeMapIcon.Name, MapIconPrefabCatalog.DefaultRaidForgeMapIcon.GuidHash),
                $"Prefab name, short map icon name, or GUID hash for the map icon to display for offline raids. {MapIconPrefabCatalog.GetShortNameExamples()} Default: MapIcon_CastleObject_Tailor.");

            EnableDecayRaidMapIcon = config.Bind("MapIcons", "EnableDecayRaidMapIcon", true, "Display a map icon on the map when a decayed base is being raided.");
            DecayRaidMapIconPrefab = config.Bind(
                "MapIcons",
                "DecayRaidMapIconPrefab",
                GetLegacyMapIconPrefabValue(config, "DecayRaidMapIconPrefabGuid", MapIconPrefabCatalog.DefaultRaidForgeMapIcon.Name, MapIconPrefabCatalog.DefaultRaidForgeMapIcon.GuidHash),
                $"Prefab name, short map icon name, or GUID hash for the map icon to display for decay raids. {MapIconPrefabCatalog.GetShortNameExamples()} Default: MapIcon_CastleObject_Tailor.");

            EnableOptInRaidMapIcon = config.Bind("MapIcons", "EnableOptInRaidMapIcon", true, "Display a map icon on opted-in bases while raids are enabled.");

            EnableOptOutRaidMapIcon = config.Bind("MapIcons", "EnableOptOutRaidMapIcon", true, "Display a map icon on opted-out bases while DefaultEveryoneOptedIn is enabled, DefaultEveryoneOptedOut is disabled, and raids are enabled.");

            RaidMapIconTimeoutSeconds = config.Bind("MapIcons", "RaidMapIconTimeoutSeconds", 300, "How many seconds (default 300 = 5 mins) the map icon remains after the last hit.");

            RemoveLegacyPassiveIconEntries(config);
        }

        private static string GetLegacyMapIconPrefabValue(ConfigFile config, string legacyKey, string defaultPrefabName, int defaultGuidHash)
        {
            if (!config.TryGetEntry("MapIcons", legacyKey, out ConfigEntry<int> legacyEntry))
            {
                return defaultPrefabName;
            }

            return legacyEntry.Value == defaultGuidHash
                ? defaultPrefabName
                : legacyEntry.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static void RemoveLegacyPassiveIconEntries(ConfigFile config)
        {
            bool removed =
                config.Remove(new ConfigDefinition("MapIcons", "EnableOfflineProtectIcons")) |
                config.Remove(new ConfigDefinition("MapIcons", "RefreshClientMapDataAfterIconSpawn")) |
                config.Remove(new ConfigDefinition("MapIcons", "OfflineProtectIconPrefabGuid")) |
                config.Remove(new ConfigDefinition("MapIcons", "OfflineRaidMapIconPrefabGuid")) |
                config.Remove(new ConfigDefinition("MapIcons", "DecayRaidMapIconPrefabGuid")) |
                config.Remove(new ConfigDefinition("MapIcons", "OptInRaidMapIconPrefabGuid")) |
                config.Remove(new ConfigDefinition("MapIcons", "OptOutRaidMapIconPrefabGuid"));

            if (removed)
            {
                config.Save();
            }
        }
    }
}
