using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using RaidForge.Services;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(SpawnCastleTeamSystem), nameof(SpawnCastleTeamSystem.OnUpdate))]
    public static class CastleHeartSpawnPatch
    {
        public static void Prefix(SpawnCastleTeamSystem __instance)
        {
            NativeArray<Entity> newHearts = __instance._MainQuery.ToEntityArray(Allocator.Temp);

            try
            {
                if (newHearts.Length > 0)
                {
                    EntityManager em = __instance.EntityManager;
                    foreach (Entity heartEntity in newHearts)
                    {
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