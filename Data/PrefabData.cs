using System;
using System.Collections.Generic;
using Stunlock.Core;

namespace RaidForge.Data
{
    public enum TntTier
    {
        None,
        T01,
        T02
    }

    public static class PrefabData
    {
        public static readonly PrefabRef SiegeGolemBuff = new("AB_Shapeshift_Golem_T02_Buff", 914043867);
        public static readonly PrefabRef TntItemT01 = new("Item_Building_Explosives_T01", 1779299585);
        public static readonly PrefabRef TntItemT02 = new("Item_Building_Explosives_T02", -1021407417);
        public static readonly PrefabRef TntPlaceableT01 = new("TM_EH_Mines_ExplosiveBarrel_Placeable_T01", -1401715476);
        public static readonly PrefabRef TntPlaceableT02 = new("TM_EH_Mines_ExplosiveBarrel_Placeable_T02", 2036168659);
        public static readonly PrefabRef InterloperDebuff = new("AB_Lucie_CripplingGoo_PoisonDebuff", 1382025211);
        public static readonly PrefabRef BearBuff = new("AB_Shapeshift_Bear_Buff", -1569370346);
        public static readonly PrefabRef BearSkinBuff = new("AB_Shapeshift_Bear_Skin01_Buff", -858273386);
        public static readonly PrefabRef ExternalInventoryPrefab = new("External_Inventory", 1183666186);

        public static readonly PrefabRef SolarusContainer = new("TM_Castle_Container_Specialized_Soulshards_Solarus", -824445631);
        public static readonly PrefabRef MonsterContainer = new("TM_Castle_Container_Specialized_Soulshards_Monster", -1996942061);
        public static readonly PrefabRef ManticoreContainer = new("TM_Castle_Container_Specialized_Soulshards_Manticore", 653759442);
        public static readonly PrefabRef DraculaContainer = new("TM_Castle_Container_Specialized_Soulshards_Dracula", 1495743889);
        public static readonly PrefabRef MorganaContainer = new("TM_Castle_Container_Specialized_Soulshards_Morgana", 1724128982);

        public static readonly PrefabRef SolarusShard = new("Item_MagicSource_SoulShard_Solarus", -21943750);
        public static readonly PrefabRef MorganaShard = new("Item_MagicSource_SoulShard_Morgana", 1286615355);
        public static readonly PrefabRef DraculaShard = new("Item_MagicSource_SoulShard_Dracula", 666638454);
        public static readonly PrefabRef MonsterShard = new("Item_MagicSource_SoulShard_Monster", -1581189572);
        public static readonly PrefabRef ManticoreShard = new("Item_MagicSource_SoulShard_Manticore", -1260254082);

        private static readonly PrefabRef[] _all =
        {
            SiegeGolemBuff,
            TntItemT01,
            TntItemT02,
            TntPlaceableT01,
            TntPlaceableT02,
            InterloperDebuff,
            BearBuff,
            BearSkinBuff,
            ExternalInventoryPrefab,
            SolarusContainer,
            MonsterContainer,
            ManticoreContainer,
            DraculaContainer,
            MorganaContainer,
            SolarusShard,
            MorganaShard,
            DraculaShard,
            MonsterShard,
            ManticoreShard,
        };

        public static IReadOnlyList<PrefabRef> All => _all;

        public static readonly HashSet<PrefabGUID> SoulShardPrefabGUIDs = new()
        {
            SolarusShard.Guid,
            MorganaShard.Guid,
            DraculaShard.Guid,
            MonsterShard.Guid,
            ManticoreShard.Guid
        };

        public static readonly HashSet<PrefabGUID> TntItemPrefabGUIDs = new()
        {
            TntItemT01.Guid,
            TntItemT02.Guid
        };

        public static readonly HashSet<PrefabGUID> TntPlaceablePrefabGUIDs = new()
        {
            TntPlaceableT01.Guid,
            TntPlaceableT02.Guid
        };

        public static readonly HashSet<PrefabGUID> SoulShardPedestalPrefabGUIDs = new()
        {
            SolarusContainer.Guid,
            MonsterContainer.Guid,
            ManticoreContainer.Guid,
            DraculaContainer.Guid,
            MorganaContainer.Guid
        };

        public static bool TryGetByName(string name, out PrefabRef prefab)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string trimmedName = name.Trim();

                foreach (PrefabRef candidate in _all)
                {
                    if (string.Equals(candidate.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
                    {
                        prefab = candidate;
                        return true;
                    }
                }
            }

            prefab = default;
            return false;
        }

        public static bool TryGetByHash(int guidHash, out PrefabRef prefab)
        {
            foreach (PrefabRef candidate in _all)
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

        public static bool IsTntPrefab(PrefabGUID prefabGuid)
        {
            return IsTntItemPrefab(prefabGuid) || IsTntPlaceablePrefab(prefabGuid);
        }

        public static bool IsTntItemPrefab(PrefabGUID prefabGuid)
        {
            return TntItemPrefabGUIDs.Contains(prefabGuid);
        }

        public static bool IsTntPlaceablePrefab(PrefabGUID prefabGuid)
        {
            return TntPlaceablePrefabGUIDs.Contains(prefabGuid);
        }

        public static TntTier GetTntTier(PrefabGUID prefabGuid)
        {
            if (prefabGuid == TntItemT01.Guid || prefabGuid == TntPlaceableT01.Guid)
            {
                return TntTier.T01;
            }

            if (prefabGuid == TntItemT02.Guid || prefabGuid == TntPlaceableT02.Guid)
            {
                return TntTier.T02;
            }

            return TntTier.None;
        }
    }
}
