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
            // No longer need, but will leave for now.
            /*
            728150320,   // TM_Castle_Wall_Tier02_Stone_Entrance
            996421136,   // TM_Castle_Wall_Tier02_Stone_EntranceWide
            1960255814,  // TM_Castle_Wall_Tier02_Stone_EntranceWide_Left
            -2018964383, // TM_Castle_Wall_Tier02_Stone_EntranceWide_Right
            1506730526,  // TM_Castle_Wall_Tier02_Stone_Pillar
            -764944520,  // TM_Castle_Wall_Tier02_Stone_Window
            572652285,   // TM_Castle_Fortification_Stone_Wall01
            1451022870,  // TM_Castle_Wall_Entrance_Center_Stone01
            1909288154,  // TM_Castle_Wall_Tier02_Stone
            -251630856,  // TM_Castle_Wall_Tier02_Stone_EntranceCrown
            809177531,   // TM_Castle_Wall_Door_Metal_Wide_Tier02_ServantLock
            -1378161357, // TM_Castle_Wall_Door_Metal_Wide_T02_Standard
            -2120778385, // TM_Castle_Wall_Door_Tier02_PrisonStyle01_ServantLock
            -1615530056, // TM_Castle_Wall_Door_Tier02_PrisonStyle01_Standard
            1031154600,  // TM_Castle_Wall_Door_Tier02_PrisonStyle02_ServantLock
            -29749970,   // TM_Castle_Wall_Door_Tier02_PrisonStyle02_Standard
            -1431484465, // TM_Castle_Wall_DoorPlug_Tier02_PrisonStyle01_Standard
            -335563583,   // TM_Castle_Wall_DoorPlug_Tier02_PrisonStyle02_Standard
            -1293632412, // TM_Castle_Wall_Tier01_Wood_Entrance
             1639432090, // TM_Castle_Wall_Tier01_Wood_Pillar
             792289367,  // TM_Castle_Wall_Tier01_Wood 
            */
        };
    }
}