using System;
using System.Collections.Generic;
using System.Linq;
using Stunlock.Core;

namespace RaidForge.Data
{
    public readonly struct MapIconPrefabOption
    {
        public readonly string ShortName;
        public readonly PrefabRef Prefab;
        public string PrefabName => Prefab.Name;
        public int GuidHash => Prefab.GuidHash;

        public MapIconPrefabOption(string shortName, PrefabRef prefab)
        {
            ShortName = shortName;
            Prefab = prefab;
        }

        public MapIconPrefabOption(string shortName, string prefabName, int guidHash)
            : this(shortName, new PrefabRef(prefabName, guidHash))
        {
        }
    }

    public static class MapIconPrefabCatalog
    {
        // Each name/hash pair is defined once and reused by role-specific code and the selectable catalog.
        public static readonly PrefabRef DefaultRaidForgeMapIcon = new("MapIcon_CastleObject_Tailor", -1060998155);
        public static readonly PrefabRef MapIconProxyPrefab = new("MapIcon_ProxyObject_POI_Unknown", 636813227);
        public static readonly PrefabRef PassiveRaidStateMapIcon = new("MapIcon_Exit", -1527777594);
        public static readonly PrefabRef RecommendedTerritoryMapIcon = new("MapIcon_RecommendedTerritoryIcon", 906269163);
        public static readonly PrefabRef HistoricalRelicSpawnBaseMapIcon = new("MapIcon_Relic_Spawn_Base", -1305965860);
        public static readonly PrefabRef HistoricalRelicBaseMapIcon = new("MapIcon_Relic_Base", 638227411);
        public static readonly PrefabRef HistoricalCastleHeartMapIcon = new("MapIcon_CastleObject_CastleHeart", 652170644);

        private static readonly MapIconPrefabOption[] _options =
        {
            new("CastleObject_Anvil", "MapIcon_CastleObject_Anvil", 880725011),
            new("CastleObject_BloodAltar", "MapIcon_CastleObject_BloodAltar", -1639620581),
            new("CastleObject_CastleHeart", HistoricalCastleHeartMapIcon),
            new("CastleObject_CraftingBench", "MapIcon_CastleObject_CraftingBench", -2136859183),
            new("CastleObject_Default", "MapIcon_CastleObject_Default", -1511010205),
            new("CastleObject_JewelCrafting", "MapIcon_CastleObject_JewelCrafting", 268696952),
            new("CastleObject_ResearchStation", "MapIcon_CastleObject_ResearchStation", -623144025),
            new("CastleObject_Tailor", DefaultRaidForgeMapIcon),
            new("CastleWaypoint_Active", "MapIcon_CastleWaypoint_Active", 404272564),
            new("Cave_Entryway", "MapIcon_Cave_Entryway", 743889990),
            new("CharmedUnit", "MapIcon_CharmedUnit", -1491648886),
            new("Crypt", "MapIcon_Crypt", -2078904014),
            new("DeathContainer", "MapIcon_DeathContainer", -1597024018),
            new("DraculasCastle", "MapIcon_DraculasCastle", -2066471106),
            new("Exit", PassiveRaidStateMapIcon),
            new("LocalPlayer", "MapIcon_LocalPlayer", -1323817571),
            new("Mount", "MapIcon_Mount", 1495684919),
            new("Player", "MapIcon_Player", -892362184),
            new("PlayerCustomMarker", "MapIcon_PlayerCustomMarker", 1716771727),
            new("PlayerCustomMarkerPathfindDot", "MapIcon_PlayerCustomMarkerPathfindDot", -438821425),
            new("PlayerPathDot", "MapIcon_PlayerPathDot", 467133242),
            new("POI_Discover_BanditCamp", "MapIcon_POI_Discover_BanditCamp", 2043384709),
            new("POI_Discover_BanditEncampment", "MapIcon_POI_Discover_BanditEncampment", 1519700716),
            new("POI_Discover_BanditFortification", "MapIcon_POI_Discover_BanditFortification", 1092950571),
            new("POI_Discover_BanditHideout", "MapIcon_POI_Discover_BanditHideout", 1210453814),
            new("POI_Discover_BanditOutpost", "MapIcon_POI_Discover_BanditOutpost", 1957543975),
            new("POI_Discover_Boss_Bandit", "MapIcon_POI_Discover_Boss_Bandit", 1501929529),
            new("POI_Discover_Boss_GrizzlyBear", "MapIcon_POI_Discover_Boss_GrizzlyBear", 400428420),
            new("POI_Discover_Boss_Militia", "MapIcon_POI_Discover_Boss_Militia", 216024887),
            new("POI_Discover_Boss_SpiderQueen", "MapIcon_POI_Discover_Boss_SpiderQueen", -1798124394),
            new("POI_Discover_Boss_Undead", "MapIcon_POI_Discover_Boss_Undead", -1887784669),
            new("POI_Discover_ChurchFarmlands", "MapIcon_POI_Discover_ChurchFarmlands", -1893591910),
            new("POI_Discover_CottonPlantation", "MapIcon_POI_Discover_CottonPlantation", -768444788),
            new("POI_Discover_CryptVault", "MapIcon_POI_Discover_CryptVault", 603697502),
            new("POI_Discover_CultistSite", "MapIcon_POI_Discover_CultistSite", 931669506),
            new("POI_Discover_DunleyChurch", "MapIcon_POI_Discover_DunleyChurch", 632395128),
            new("POI_Discover_FarmlandsChurch", "MapIcon_POI_Discover_FarmlandsChurch", -1256336496),
            new("POI_Discover_FarmlandsFarm", "MapIcon_POI_Discover_FarmlandsFarm", 239387799),
            new("POI_Discover_FarmlandsGarlicFarm", "MapIcon_POI_Discover_FarmlandsGarlicFarm", 297452363),
            new("POI_Discover_FarmlandsHorseFarm", "MapIcon_POI_Discover_FarmlandsHorseFarm", -948983333),
            new("POI_Discover_FarmlandsTown", "MapIcon_POI_Discover_FarmlandsTown", -1426507977),
            new("POI_Discover_ForgottenCemetery", "MapIcon_POI_Discover_ForgottenCemetery", -2040967389),
            new("POI_Discover_GhostTown", "MapIcon_POI_Discover_GhostTown", -1276819284),
            new("POI_Discover_Graveyard", "MapIcon_POI_Discover_Graveyard", 698241041),
            new("POI_Discover_InfestedMines", "MapIcon_POI_Discover_InfestedMines", 1904546641),
            new("POI_Discover_IronMines", "MapIcon_POI_Discover_IronMines", 1903833803),
            new("POI_Discover_LumberjackCamp", "MapIcon_POI_Discover_LumberjackCamp", 1148857439),
            new("POI_Discover_Merchant", "MapIcon_POI_Discover_Merchant", -360752412),
            new("POI_Discover_MilitiaCommandPost", "MapIcon_POI_Discover_MilitiaCommandPost", 105682385),
            new("POI_Discover_MilitiaEncampment", "MapIcon_POI_Discover_MilitiaEncampment", -605145652),
            new("POI_Discover_MilitiaFortification", "MapIcon_POI_Discover_MilitiaFortification", 1115067066),
            new("POI_Discover_MilitiaWatchTower", "MapIcon_POI_Discover_MilitiaWatchTower", -1768623961),
            new("POI_Discover_SpiderForest", "MapIcon_POI_Discover_SpiderForest", -680026394),
            new("POI_Discover_TerokiMeadows", "MapIcon_POI_Discover_TerokiMeadows", -1379366510),
            new("POI_Discover_TownFarmlands", "MapIcon_POI_Discover_TownFarmlands", 563819869),
            new("POI_Discover_Unknown", "MapIcon_POI_Discover_Unknown", -1443504104),
            new("POI_Discover_WolfDen", "MapIcon_POI_Discover_WolfDen", -1443808553),
            new("POI_Knowledge_Church", "MapIcon_POI_Knowledge_Church", -1571811509),
            new("POI_Knowledge_LumberjackCamp", "MapIcon_POI_Knowledge_LumberjackCamp", 351553164),
            new("POI_Knowledge_Settlement", "MapIcon_POI_Knowledge_Settlement", 138663934),
            new("POI_Knowledge_Town", "MapIcon_POI_Knowledge_Town", -221292597),
            new("POI_Resource_CoalMine", "MapIcon_POI_Resource_CoalMine", 1992891574),
            new("POI_Resource_CopperQuarry", "MapIcon_POI_Resource_CopperQuarry", 419555810),
            new("POI_Resource_CottonPlantation", "MapIcon_POI_Resource_CottonPlantation", 468675810),
            new("POI_Resource_DuskbarkForest", "MapIcon_POI_Resource_DuskbarkForest", -1989680779),
            new("POI_Resource_ForgottenCemetery", "MapIcon_POI_Resource_ForgottenCemetery", -1405787520),
            new("POI_Resource_GrizzlyBearDen", "MapIcon_POI_Resource_GrizzlyBearDen", -107944115),
            new("POI_Resource_IronMine", "MapIcon_POI_Resource_IronMine", -1725105796),
            new("POI_Resource_IronVein", "MapIcon_POI_Resource_IronVein", -1132008653),
            new("POI_Resource_Permanent_CopperQuarry", "MapIcon_POI_Resource_Permanent_CopperQuarry", 701513285),
            new("POI_Resource_QuartzMine", "MapIcon_POI_Resource_QuartzMine", -853022254),
            new("POI_Resource_QuartzQuarry", "MapIcon_POI_Resource_QuartzQuarry", -426591728),
            new("POI_Resource_SaintLilyCemetery", "MapIcon_POI_Resource_SaintLilyCemetery", -1766751566),
            new("POI_Resource_SnowBlossomForest", "MapIcon_POI_Resource_SnowBlossomForest", 643012707),
            new("POI_Spawn_CoffinSelect", "MapIcon_POI_Spawn_CoffinSelect", 1454830975),
            new("POI_Spawn_CryptSelect", "MapIcon_POI_Spawn_CryptSelect", -1938049417),
            new("POI_Spawn_WaypointSelect", "MapIcon_POI_Spawn_WaypointSelect", -2098609826),
            new("POI_VBloodSource", "MapIcon_POI_VBloodSource", 139106998),
            new("RecommendedTerritoryIcon", RecommendedTerritoryMapIcon),
            new("Relic_Base", HistoricalRelicBaseMapIcon),
            new("Relic_Spawn_Base", HistoricalRelicSpawnBaseMapIcon),
            new("Relic_Spawn_Dracula", "MapIcon_Relic_Spawn_Dracula", -834249437),
            new("Relic_Spawn_Morgana", "MapIcon_Relic_Spawn_Morgana", 1621891122),
            new("Relic_Spawn_Solarus", "MapIcon_Relic_Spawn_Solarus", -52304021),
            new("Relic_Spawn_TheMonster", "MapIcon_Relic_Spawn_TheMonster", 1570562186),
            new("Relic_Spawn_WingedHorror", "MapIcon_Relic_Spawn_WingedHorror", -199298876),
            new("Relic_Standard_Dracula", "MapIcon_Relic_Standard_Dracula", 1082059027),
            new("Relic_Standard_Morgana", "MapIcon_Relic_Standard_Morgana", 1172401273),
            new("Relic_Standard_Solarus", "MapIcon_Relic_Standard_Solarus", 2133172828),
            new("Relic_Standard_TheMonster", "MapIcon_Relic_Standard_TheMonster", 1204693597),
            new("Relic_Standard_WingedHorror", "MapIcon_Relic_Standard_WingedHorror", 622223699),
            new("Siege_Summon_T02", "MapIcon_Siege_Summon_T02", -1769480952),
            new("Siege_Summon_T02_Complete", "MapIcon_Siege_Summon_T02_Complete", 1358914922),
            new("StartGraveyardExit", "MapIcon_StartGraveyardExit", -37003475),
            new("StoneCoffin", "MapIcon_StoneCoffin", 985620050),
            new("UnclaimedCastle", "MapIcon_UnclaimedCastle", 1556395508),
            new("WoodenCoffin", "MapIcon_WoodenCoffin", 1636226671),
            new("WorldWaypoint_Active", "MapIcon_WorldWaypoint_Active", -1510127174),
        };

        private static readonly PrefabRef[] _knownPrefabs = BuildKnownPrefabs();
        private static readonly Dictionary<string, MapIconPrefabOption> _aliases = BuildAliases();
        private static readonly Dictionary<int, string> _namesByHash = _knownPrefabs
            .GroupBy(prefab => prefab.GuidHash)
            .ToDictionary(group => group.Key, group => group.First().Name);

        public static IReadOnlyList<MapIconPrefabOption> Options => _options;
        public static IReadOnlyList<PrefabRef> KnownPrefabs => _knownPrefabs;

        public static bool TryResolve(string value, out PrefabGUID prefabGuid, out string prefabName)
        {
            prefabGuid = default;
            prefabName = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!_aliases.TryGetValue(value.Trim(), out var option))
            {
                return false;
            }

            prefabGuid = new PrefabGUID(option.GuidHash);
            prefabName = option.PrefabName;
            return true;
        }

        public static bool TryGetName(int guidHash, out string prefabName)
        {
            return _namesByHash.TryGetValue(guidHash, out prefabName);
        }

        public static bool TryGetByHash(int guidHash, out PrefabRef prefab)
        {
            foreach (PrefabRef candidate in _knownPrefabs)
            {
                if (candidate.GuidHash == guidHash)
                {
                    prefab = candidate;
                    return true;
                }
            }

            prefab = default;
            return false;
        }

        public static string GetShortNameExamples()
        {
            return "Examples: CastleObject_Tailor, CastleObject_CastleHeart, Exit, Relic_Base, Siege_Summon_T02.";
        }

        private static Dictionary<string, MapIconPrefabOption> BuildAliases()
        {
            var aliases = new Dictionary<string, MapIconPrefabOption>(StringComparer.OrdinalIgnoreCase);

            foreach (var option in _options)
            {
                aliases[option.ShortName] = option;
                aliases[option.PrefabName] = option;

                if (option.PrefabName.StartsWith("MapIcon_", StringComparison.OrdinalIgnoreCase))
                {
                    aliases[option.PrefabName.Substring("MapIcon_".Length)] = option;
                }
            }

            aliases["Relic_Spawn_Ssolarus"] = aliases["Relic_Spawn_Solarus"];
            aliases["MapIcon_Relic_Spawn_Ssolarus"] = aliases["Relic_Spawn_Solarus"];

            return aliases;
        }

        private static PrefabRef[] BuildKnownPrefabs()
        {
            return _options
                .Select(option => option.Prefab)
                .Append(MapIconProxyPrefab)
                .GroupBy(prefab => prefab.GuidHash)
                .Select(group => group.First())
                .ToArray();
        }
    }
}
