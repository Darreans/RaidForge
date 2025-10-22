using Stunlock.Core;
using System.Collections.Generic;

namespace RaidForge.Data
{
    public static class PrefabData
    {
        public static readonly PrefabGUID SiegeGolemBuff = new PrefabGUID(914043867);

        public static readonly PrefabGUID TntExplosivePrefab1 = new PrefabGUID(-1021407417);
        public static readonly PrefabGUID TntExplosivePrefab2 = new PrefabGUID(1779299585);

        public static readonly PrefabGUID InterloperDebuff = new PrefabGUID(1382025211);

        public static readonly PrefabGUID ExternalInventoryPrefab = new PrefabGUID(1183666186);

        public static readonly PrefabGUID SolarusContainer = new PrefabGUID(-824445631);
        public static readonly PrefabGUID MonsterContainer = new PrefabGUID(-1996942061);
        public static readonly PrefabGUID ManticoreContainer = new PrefabGUID(653759442);
        public static readonly PrefabGUID DraculaContainer = new PrefabGUID(1495743889);
        public static readonly PrefabGUID MorganaContainer = new PrefabGUID(1724128982);

        public static readonly PrefabGUID SolarusShard = new PrefabGUID(-21943750);
        public static readonly PrefabGUID MorganaShard = new PrefabGUID(1286615355);
        public static readonly PrefabGUID DraculaShard = new PrefabGUID(666638454);
        public static readonly PrefabGUID MonsterShard = new PrefabGUID(1581189572);
        public static readonly PrefabGUID ManticoreShard = new PrefabGUID(-1260254082);

        public static readonly HashSet<PrefabGUID> SoulShardPrefabGUIDs = new()
        {
            SolarusShard,
            MorganaShard,
            DraculaShard,
            MonsterShard,
            ManticoreShard
        };

        public static readonly Dictionary<PrefabGUID, PrefabGUID> PedestalToExpectedShardMap = new Dictionary<PrefabGUID, PrefabGUID>
        {
            { SolarusContainer, SolarusShard },
            { MonsterContainer, MonsterShard },
            { ManticoreContainer, ManticoreShard },
            { DraculaContainer, DraculaShard },
            { MorganaContainer, MorganaShard }
        };

        public static readonly HashSet<int> ProtectedStructurePrefabHashes = new HashSet<int>
        {

        };
    }
}