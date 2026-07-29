using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using Unity.Collections;
using Unity.Entities;
using RaidForge.Services;
using RaidForge.Config;
using RaidForge.Utils;
using System;

namespace RaidForge.Patches
{
    [HarmonyPatch]
    public static class RaidEventDetectorPatch
    {
        private static Entity GetCastleHeartFromBreachedStructure(Entity breachedStructureEntity, EntityManager em)
        {
            if (em.HasComponent<CastleHeartConnection>(breachedStructureEntity))
            {
                NetworkedEntity heartNetEntity = em.GetComponentData<CastleHeartConnection>(breachedStructureEntity).CastleHeartEntity;
                Entity chEntity = heartNetEntity._Entity;
                return chEntity;
            }
            if (em.HasComponent<CastleHeart>(breachedStructureEntity))
            {
                return breachedStructureEntity;
            }
            return Entity.Null;
        }

        [HarmonyPatch(typeof(DeathEventListenerSystem), nameof(DeathEventListenerSystem.OnUpdate))]
        [HarmonyPostfix]
        static void OnUpdatePostfix(DeathEventListenerSystem __instance)
        {
            if (!RaidInterferenceConfig.EnableRaidInterference.Value) return;

            NativeArray<DeathEvent> deathEvents = default;
            try
            {
                if (__instance._DeathEventQuery.IsEmptyIgnoreFilter) return;

                deathEvents = __instance._DeathEventQuery.ToComponentDataArray<DeathEvent>(Allocator.TempJob);
                if (deathEvents.Length == 0) return;

                EntityManager currentEntityManager = __instance.EntityManager;

                foreach (DeathEvent deathEvent in deathEvents)
                {
                    if (!currentEntityManager.Exists(deathEvent.Died) || !currentEntityManager.Exists(deathEvent.Killer)) continue;

                    bool hasAnnounceBreached = currentEntityManager.HasComponent<AnnounceCastleBreached>(deathEvent.Died);
                    if (!hasAnnounceBreached)
                    {
                        continue;
                    }

                    bool isCorrectReason = deathEvent.StatChangeReason.Equals(StatChangeReason.StatChangeSystem_0);
                    if (!isCorrectReason)
                    {
                        continue;
                    }

                    if (!UserHelper.TryGetPlayerOwnerFromSource(currentEntityManager, deathEvent.Killer, out _, out Entity attackerUserEntity))
                    {
                        continue;
                    }

                    Entity castleHeartEntity = GetCastleHeartFromBreachedStructure(deathEvent.Died, currentEntityManager);
                    if (castleHeartEntity != Entity.Null && currentEntityManager.Exists(castleHeartEntity))
                    {
                        RaidInterferenceService.StartSiege(castleHeartEntity, attackerUserEntity);
                    }

                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[RaidEventDetector] Error while detecting breached castle structures.", ex);
            }
            finally { if (deathEvents.IsCreated) deathEvents.Dispose(); }
        }
    }
}
