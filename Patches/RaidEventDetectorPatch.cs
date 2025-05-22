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

            LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] Attempting for KillerSource: {killerSourceEntity}");
            if (killerSourceEntity == Entity.Null) return false;

            if (em.HasComponent<PlayerCharacter>(killerSourceEntity))
            {
                LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] KillerSource {killerSourceEntity} IS a PlayerCharacter.");
                resolvedPlayerCharacter = killerSourceEntity;
                if (em.TryGetComponentData<PlayerCharacter>(killerSourceEntity, out var pc))
                {
                    resolvedUserEntity = pc.UserEntity;
                    if (resolvedUserEntity != Entity.Null && em.Exists(resolvedUserEntity))
                    {
                        LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] Resolved directly: PC={resolvedPlayerCharacter}, User={resolvedUserEntity}"); 
                        return true;
                    }
                    LoggingHelper.Warning($"[ResolvePlayer - {eventContext}] KillerSource PC {killerSourceEntity} has invalid UserEntity: {resolvedUserEntity}.");
                }
                else { LoggingHelper.Warning($"[ResolvePlayer - {eventContext}] KillerSource PC {killerSourceEntity} failed GetComponentData<PlayerCharacter>."); }
                return false;
            }
            LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] KillerSource {killerSourceEntity} is NOT a PlayerCharacter. Checking owner...");

            if (em.HasComponent<EntityOwner>(killerSourceEntity))
            {
                Entity ownerEntity = em.GetComponentData<EntityOwner>(killerSourceEntity).Owner;
                LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] KillerSource {killerSourceEntity} has Owner: {ownerEntity}.");
                if (ownerEntity != Entity.Null && em.Exists(ownerEntity) && em.HasComponent<PlayerCharacter>(ownerEntity))
                {
                    LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] Owner {ownerEntity} IS a PlayerCharacter.");
                    resolvedPlayerCharacter = ownerEntity;
                    if (em.TryGetComponentData<PlayerCharacter>(ownerEntity, out var pcOwner))
                    {
                        resolvedUserEntity = pcOwner.UserEntity;
                        if (resolvedUserEntity != Entity.Null && em.Exists(resolvedUserEntity))
                        {
                            LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] Resolved from Owner: PC={resolvedPlayerCharacter}, User={resolvedUserEntity}"); 
                            return true;
                        }
                        LoggingHelper.Warning($"[ResolvePlayer - {eventContext}] Owner PC {ownerEntity} has invalid UserEntity: {resolvedUserEntity}.");
                    }
                    else { LoggingHelper.Warning($"[ResolvePlayer - {eventContext}] Owner PC {ownerEntity} failed GetComponentData<PlayerCharacter>."); }
                    return false;
                }
                LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] Owner {ownerEntity} of KillerSource {killerSourceEntity} is not a PlayerCharacter or invalid.");
            }
            LoggingHelper.Debug($"[ResolvePlayer - {eventContext}] KillerSource {killerSourceEntity} also does not have EntityOwner. Cannot resolve player.");
            return false;
        }

        private static Entity GetCastleHeartFromBreachedStructure(Entity breachedStructureEntity, EntityManager em)
        {
            LoggingHelper.Debug($"[GetCastleHeart] Trying for breached structure: {breachedStructureEntity}");
            if (em.HasComponent<CastleHeartConnection>(breachedStructureEntity))
            {
                NetworkedEntity heartNetEntity = em.GetComponentData<CastleHeartConnection>(breachedStructureEntity).CastleHeartEntity;
                Entity chEntity = heartNetEntity._Entity;
                LoggingHelper.Debug($"[GetCastleHeart] Found via CastleHeartConnection. CH Entity: {chEntity}");
                return chEntity;
            }
            if (em.HasComponent<CastleHeart>(breachedStructureEntity))
            {
                LoggingHelper.Warning($"[GetCastleHeart] Breached structure {breachedStructureEntity} IS a CastleHeart itself.");
                return breachedStructureEntity;
            }
            LoggingHelper.Warning($"[GetCastleHeart] No CastleHeartConnection on {breachedStructureEntity}, and it's not a heart itself.");
            return Entity.Null;
        }

        [HarmonyPatch(typeof(DeathEventListenerSystem), nameof(DeathEventListenerSystem.OnUpdate))]
        [HarmonyPostfix]
        static void OnUpdatePostfix(DeathEventListenerSystem __instance)
        {
            if (!RaidInterferenceConfig.EnableRaidInterference.Value) return;
            LoggingHelper.Debug("[RaidEventDetectorPatch] OnUpdatePostfix CALLED (Raid Interference Enabled).");

            NativeArray<DeathEvent> deathEvents = default;
            try
            {
                if (__instance._DeathEventQuery.IsEmptyIgnoreFilter) return;
                deathEvents = __instance._DeathEventQuery.ToComponentDataArray<DeathEvent>(Allocator.TempJob);
                if (deathEvents.Length == 0) { if (deathEvents.IsCreated) deathEvents.Dispose(); return; }

                LoggingHelper.Debug($"[RaidEventDetectorPatch] Processing {deathEvents.Length} death events."); 
                EntityManager currentEntityManager = __instance.EntityManager;
                ServerGameManager? SGM_nullable = VWorld.Server?.GetExistingSystemManaged<ServerScriptMapper>()?.GetServerGameManager();
                ServerGameManager SGM = SGM_nullable.HasValue ? SGM_nullable.Value : default;

                if (!SGM_nullable.HasValue) LoggingHelper.Warning("[RaidEventDetectorPatch] ServerGameManager NOT FOUND this frame.");

                foreach (DeathEvent deathEvent in deathEvents)
                {
                    LoggingHelper.Debug($"[RaidEventDetectorPatch] --- Event: Died={deathEvent.Died}, Killer={deathEvent.Killer}, Reason={deathEvent.StatChangeReason} ---");

                    if (!currentEntityManager.Exists(deathEvent.Died) || !currentEntityManager.Exists(deathEvent.Killer)) continue;

                    bool hasAnnounceBreached = currentEntityManager.HasComponent<AnnounceCastleBreached>(deathEvent.Died);
                    if (!hasAnnounceBreached)
                    {
                        LoggingHelper.Debug($"[RaidEventDetectorPatch] ...Died {deathEvent.Died} NO AnnounceCastleBreached. Skipping.");
                        continue;
                    }
                    LoggingHelper.Debug($"[RaidEventDetectorPatch] ...Died {deathEvent.Died} HAS AnnounceCastleBreached.");

                    bool isCorrectReason = deathEvent.StatChangeReason.Equals(StatChangeReason.StatChangeSystem_0);
                    if (!isCorrectReason)
                    {
                        LoggingHelper.Debug($"[RaidEventDetectorPatch] ...StatChangeReason is '{deathEvent.StatChangeReason}', not StatChangeSystem_0. Skipping.");
                        continue;
                    }
                    LoggingHelper.Debug($"[RaidEventDetectorPatch] >>> Breach Indication: Died={deathEvent.Died}, Killer={deathEvent.Killer} <<<"); 

                    Entity attackerPlayerCharacter;
                    Entity attackerUserEntity;

                    bool isPlayerResolved = TryResolvePlayerCharacterAndUser(deathEvent.Killer, currentEntityManager,
                        $"Breach of {deathEvent.Died}", out attackerPlayerCharacter, out attackerUserEntity);

                    if (!isPlayerResolved)
                    {
                        LoggingHelper.Debug($"[RaidEventDetectorPatch] ...Breach by {deathEvent.Killer}, but could not resolve to player character & user. No Golem buff check."); 
                        continue;
                    }
                    LoggingHelper.Debug($"[RaidEventDetectorPatch] ...PlayerCharacter for breach: {attackerPlayerCharacter}, User: {attackerUserEntity}. Checking Golem buff."); 

                    bool golemBuffFound = false;
                    if (SGM_nullable.HasValue)
                    {
                        if (attackerPlayerCharacter != Entity.Null && SGM.TryGetBuff(attackerPlayerCharacter, PrefabData.SiegeGolemBuff, out _))
                        {
                            golemBuffFound = true;
                            LoggingHelper.Info($"[RaidEventDetectorPatch] ...GOLEM BUFF FOUND on PlayerCharacter {attackerPlayerCharacter}!"); 
                        }
                        else { LoggingHelper.Debug($"[RaidEventDetectorPatch] ...Golem Buff NOT found on PlayerCharacter {attackerPlayerCharacter} (or PC was Null)."); } 
                    }
                    else { LoggingHelper.Warning($"[RaidEventDetectorPatch] ...Cannot check Golem buff: ServerGameManager not available."); }

                    if (golemBuffFound)
                    {
                        Entity castleHeartEntity = GetCastleHeartFromBreachedStructure(deathEvent.Died, currentEntityManager);
                        if (castleHeartEntity != Entity.Null && currentEntityManager.Exists(castleHeartEntity))
                        {
                            LoggingHelper.Info($"[RaidEventDetectorPatch] !!! Castle breach by Golem Player CONFIRMED! CH: {castleHeartEntity}, Attacker User: {attackerUserEntity}. Calling StartSiege().");
                            RaidInterferenceService.StartSiege(castleHeartEntity, attackerUserEntity);
                        }
                        else { LoggingHelper.Warning($"[RaidEventDetectorPatch] ...Golem Player {attackerUserEntity} breached {deathEvent.Died}, but NO valid CH found."); }
                    }
                    else { LoggingHelper.Debug($"[RaidEventDetectorPatch] ...Breach by player {attackerUserEntity}, but Golem Buff was NOT found on their character {attackerPlayerCharacter}. No interference started."); } 
                }
            }
            catch (Exception e) { LoggingHelper.Error("[RaidEventDetectorPatch] Exception in Postfix", e); }
            finally { if (deathEvents.IsCreated) deathEvents.Dispose(); }
        }
    }
}