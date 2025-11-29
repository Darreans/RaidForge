using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using Unity.Collections;
using Unity.Entities;
using RaidForge.Services;
using RaidForge.Config;
using RaidForge.Data;
using RaidForge.Utils;
using System;
using ProjectM.Scripting;

namespace RaidForge.Patches
{
    [HarmonyPatch]
    public static class RaidEventDetectorPatch
    {
        private static bool TryResolvePlayerCharacterAndUser(
            Entity killerSourceEntity,
            EntityManager em,
            string eventContext,
            out Entity resolvedPlayerCharacter,
            out Entity resolvedUserEntity)
        {
            resolvedPlayerCharacter = Entity.Null;
            resolvedUserEntity = Entity.Null;

            if (killerSourceEntity == Entity.Null) return false;

            if (em.HasComponent<PlayerCharacter>(killerSourceEntity))
            {
                resolvedPlayerCharacter = killerSourceEntity;
                if (em.TryGetComponentData<PlayerCharacter>(killerSourceEntity, out var pc))
                {
                    resolvedUserEntity = pc.UserEntity;
                    if (resolvedUserEntity != Entity.Null && em.Exists(resolvedUserEntity))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (em.HasComponent<EntityOwner>(killerSourceEntity))
            {
                Entity ownerEntity = em.GetComponentData<EntityOwner>(killerSourceEntity).Owner;
                if (ownerEntity != Entity.Null && em.Exists(ownerEntity) && em.HasComponent<PlayerCharacter>(ownerEntity))
                {
                    resolvedPlayerCharacter = ownerEntity;
                    if (em.TryGetComponentData<PlayerCharacter>(ownerEntity, out var pcOwner))
                    {
                        resolvedUserEntity = pcOwner.UserEntity;
                        if (resolvedUserEntity != Entity.Null && em.Exists(resolvedUserEntity))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            return false;
        }

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
                if (deathEvents.Length == 0) { if (deathEvents.IsCreated) deathEvents.Dispose(); return; }

                EntityManager currentEntityManager = __instance.EntityManager;
                ServerGameManager? SGM_nullable = VWorld.Server?.GetExistingSystemManaged<ServerScriptMapper>()?.GetServerGameManager();
                ServerGameManager SGM = SGM_nullable.HasValue ? SGM_nullable.Value : default;


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

                    Entity attackerPlayerCharacter;
                    Entity attackerUserEntity;

                    bool isPlayerResolved = TryResolvePlayerCharacterAndUser(deathEvent.Killer, currentEntityManager,
                        $"Breach of {deathEvent.Died}", out attackerPlayerCharacter, out attackerUserEntity);

                    if (!isPlayerResolved)
                    {
                        continue;
                    }

                    bool golemBuffFound = false;
                    if (SGM_nullable.HasValue)
                    {
                        if (attackerPlayerCharacter != Entity.Null && SGM.TryGetBuff(attackerPlayerCharacter, PrefabData.SiegeGolemBuff, out _))
                        {
                            golemBuffFound = true;
                        }
                    }

                    if (golemBuffFound)
                    {
                        Entity castleHeartEntity = GetCastleHeartFromBreachedStructure(deathEvent.Died, currentEntityManager);
                        if (castleHeartEntity != Entity.Null && currentEntityManager.Exists(castleHeartEntity))
                        {
                            RaidInterferenceService.StartSiege(castleHeartEntity, attackerUserEntity);
                        }
                    }
                   
                }
            }
            catch (Exception e)
            {
               
            }
            finally { if (deathEvents.IsCreated) deathEvents.Dispose(); }
        }
    }
}