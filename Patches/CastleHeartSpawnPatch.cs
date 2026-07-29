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
            if (__instance._MainQuery.IsEmptyIgnoreFilter) return;

            NativeArray<Entity> newHearts = __instance._MainQuery.ToEntityArray(Allocator.Temp);

            try
            {
                EntityManager em = __instance.EntityManager;
                foreach (Entity heartEntity in newHearts)
                {
                    OwnershipCacheService.AddHeartToCache(heartEntity, em);
                }
            }
            finally
            {
                if (newHearts.IsCreated) newHearts.Dispose();
            }
        }
    }
}
