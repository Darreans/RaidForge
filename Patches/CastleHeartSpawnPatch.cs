using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using RaidForge.Services;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches
{
    // This system runs whenever a Castle Heart is initialized by the engine.
    // CRITICAL: This is what detects bases loading from the Save File on restart.
    [HarmonyPatch(typeof(SpawnCastleTeamSystem), nameof(SpawnCastleTeamSystem.OnUpdate))]
    public static class CastleHeartSpawnPatch
    {
        public static void Prefix(SpawnCastleTeamSystem __instance)
        {
            // Get all hearts currently being initialized by the game
            NativeArray<Entity> newHearts = __instance._MainQuery.ToEntityArray(Allocator.Temp);

            try
            {
                if (newHearts.Length > 0)
                {
                    EntityManager em = __instance.EntityManager;
                    foreach (Entity heartEntity in newHearts)
                    {
                        // Add them to our cache immediately
                        OwnershipCacheService.AddHeartToCache(heartEntity, em);
                    }
                }
            }
            finally
            {
                if (newHearts.IsCreated) newHearts.Dispose();
            }
        }
    }
}