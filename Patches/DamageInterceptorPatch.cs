using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using Unity.Collections;
using Unity.Entities;
using Stunlock.Core;
using RaidForge.Services;
using RaidForge.Config;
using RaidForge.Data;
using RaidForge.Utils;
using RaidForge;
using System;
using System.Collections.Generic;
using ProjectM.Gameplay.Systems;

namespace RaidForge.Patches
{
    [HarmonyPatch]
    public static class DamageInterceptorPatch
    {
        private static readonly TimeSpan OfflineWarningCooldown = TimeSpan.FromSeconds(10);
        private static Dictionary<Entity, DateTime> _lastOfflineWarningMessageTimes = new Dictionary<Entity, DateTime>();

        private static readonly TimeSpan OfflineRaidAnnouncementCooldown = TimeSpan.FromMinutes(30);
        private static Dictionary<Entity, DateTime> _lastOfflineRaidAnnouncementTimes = new Dictionary<Entity, DateTime>();

        [HarmonyPatch(typeof(StatChangeSystem), nameof(StatChangeSystem.OnUpdate))]
        [HarmonyPrefix]
        static bool OnUpdatePrefix(StatChangeSystem __instance)
        {
            EntityManager em = __instance.EntityManager;
            NativeArray<Entity> statChangeEventEntities = __instance._Query.ToEntityArray(Allocator.Temp);

            try
            {
                foreach (Entity eventEntity in statChangeEventEntities)
                {
                    if (!em.Exists(eventEntity) || !em.HasComponent<StatChangeEvent>(eventEntity)) continue;

                    StatChangeEvent originalEvent = em.GetComponentData<StatChangeEvent>(eventEntity);

                    if (originalEvent.StatType != StatType.Health || originalEvent.Change >= 0)
                    {
                        continue;
                    }

                    Entity targetEntity = originalEvent.Entity;
                    Entity sourceOfDamageEntity = originalEvent.Source;

                    if (IsProtectedAndGetHeart(em, targetEntity, out Entity castleHeartEntity))
                    {
                        bool isBaseProtectedByFullORP = OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value &&
                                                      OfflineProtectionService.ShouldProtectBase(castleHeartEntity, em, Plugin.BepInLogger);

                        if (!isBaseProtectedByFullORP)
                        {
                            Entity keyForGraceCheck = GetGraceKeyForCastleHeart(em, castleHeartEntity);

                            if (keyForGraceCheck != Entity.Null)
                            {
                                bool isInActiveGrace = OfflineGraceService.GetClanGracePeriodInfo(em, keyForGraceCheck, out _, out _, out _);
                                bool allDefendersCurrentlyOffline = AreAllDefendersOfHeartOffline(em, castleHeartEntity);

                                if (allDefendersCurrentlyOffline && isInActiveGrace &&
                                    em.HasComponent<CastleHeartConnection>(targetEntity) &&
                                    !em.HasComponent<CastleHeart>(targetEntity))
                                {
                                    bool isSourceGolem = false;
                                    bool isSourceTnt = false;
                                    string attackerName = "Unknown Attacker";
                                    Entity eventAttackerUserEntity = Entity.Null;

                                    if (UserHelper.TryGetPlayerOwnerFromSource(em, sourceOfDamageEntity, out Entity playerCharSource, out eventAttackerUserEntity))
                                    {
                                        if (HasSiegeGolemBuff(em, playerCharSource))
                                        {
                                            isSourceGolem = true;
                                            if (em.TryGetComponentData<User>(eventAttackerUserEntity, out User golemUserData))
                                            {
                                                attackerName = golemUserData.CharacterName.ToString();
                                            }
                                        }
                                    }

                                    if (!isSourceGolem && em.Exists(sourceOfDamageEntity))
                                    {
                                        PrefabGUID sourcePrefabGuid = default;
                                        if (em.HasComponent<PrefabGUID>(sourceOfDamageEntity))
                                        {
                                            sourcePrefabGuid = em.GetComponentData<PrefabGUID>(sourceOfDamageEntity);
                                        }
                                        else if (em.HasComponent<EntityOwner>(sourceOfDamageEntity))
                                        {
                                            Entity ownerOfSource = em.GetComponentData<EntityOwner>(sourceOfDamageEntity).Owner;
                                            if (em.Exists(ownerOfSource) && em.HasComponent<PrefabGUID>(ownerOfSource))
                                            {
                                                sourcePrefabGuid = em.GetComponentData<PrefabGUID>(ownerOfSource);
                                            }
                                        }

                                        if (sourcePrefabGuid == PrefabData.TntExplosivePrefab1 || sourcePrefabGuid == PrefabData.TntExplosivePrefab2)
                                        {
                                            isSourceTnt = true;
                                            if (eventAttackerUserEntity == Entity.Null && UserHelper.TryGetPlayerOwnerFromSource(em, sourceOfDamageEntity, out _, out Entity tntUserEntity))
                                            {
                                                eventAttackerUserEntity = tntUserEntity;
                                            }
                                            if (eventAttackerUserEntity != Entity.Null && em.TryGetComponentData<User>(eventAttackerUserEntity, out User tntUserData))
                                            {
                                                attackerName = tntUserData.CharacterName.ToString();
                                            }
                                            else
                                            {
                                                attackerName = "Explosives";
                                            }
                                        }
                                    }

                                    if (isSourceGolem || isSourceTnt)
                                    {
                                        if (OfflineRaidProtectionConfig.AnnounceOfflineRaidDuringGrace.Value)
                                        {
                                            bool allowAnnouncement = true;
                                            if (_lastOfflineRaidAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnouncementTime))
                                            {
                                                if (DateTime.UtcNow - lastAnnouncementTime < OfflineRaidAnnouncementCooldown)
                                                {
                                                    allowAnnouncement = false;
                                                }
                                            }

                                            if (allowAnnouncement)
                                            {
                                                string defenderBaseName = GetDefenderBaseName(em, castleHeartEntity);
                                                var announcementMessage = new FixedString512Bytes($"{ChatColors.WarningText(defenderBaseName)}{ChatColors.InfoText(" is being offline raided by ")}{ChatColors.HighlightText(attackerName)}{ChatColors.InfoText("!")}"); ServerChatUtils.SendSystemMessageToAllClients(em, ref announcementMessage);
                                                _lastOfflineRaidAnnouncementTimes[castleHeartEntity] = DateTime.UtcNow;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (isBaseProtectedByFullORP)
                        {
                            StatChangeEvent modifiedEvent = originalEvent;
                            modifiedEvent.Change = 0;
                            em.SetComponentData(eventEntity, modifiedEvent);

                            Entity attackerUserEntityForWarning = Entity.Null;
                            if (UserHelper.TryGetPlayerOwnerFromSource(em, sourceOfDamageEntity, out _, out Entity potentialAttackerUserEntity))
                            {
                                if (em.Exists(potentialAttackerUserEntity) && em.HasComponent<User>(potentialAttackerUserEntity))
                                {
                                    attackerUserEntityForWarning = potentialAttackerUserEntity;
                                }
                            }

                            if (attackerUserEntityForWarning != Entity.Null)
                            {
                                bool sendMessage = true;
                                if (_lastOfflineWarningMessageTimes.TryGetValue(attackerUserEntityForWarning, out DateTime lastTimeSent))
                                {
                                    if (DateTime.UtcNow - lastTimeSent < OfflineWarningCooldown)
                                    {
                                        sendMessage = false;
                                    }
                                }
                                if (sendMessage)
                                {
                                    User attackerUser = em.GetComponentData<User>(attackerUserEntityForWarning);
                                    FixedString512Bytes message = new FixedString512Bytes(ChatColors.WarningText("This base is currently offline raid protected!"));
                                    ServerChatUtils.SendSystemMessageToClient(em, attackerUser, ref message);
                                    _lastOfflineWarningMessageTimes[attackerUserEntityForWarning] = DateTime.UtcNow;
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (statChangeEventEntities.IsCreated) statChangeEventEntities.Dispose();
            }
            return true;
        }

        private static Entity GetGraceKeyForCastleHeart(EntityManager em, Entity castleHeartEntity)
        {
            if (em.TryGetComponentData<UserOwner>(castleHeartEntity, out UserOwner chOwner))
            {
                Entity ownerUserEntity = chOwner.Owner._Entity;
                if (em.TryGetComponentData<User>(ownerUserEntity, out User ownerData))
                {
                    if (em.Exists(ownerData.ClanEntity._Entity) && em.HasComponent<ClanTeam>(ownerData.ClanEntity._Entity))
                    {
                        return ownerData.ClanEntity._Entity;
                    }
                    return ownerUserEntity;
                }
            }
            return Entity.Null;
        }

        private static string GetDefenderBaseName(EntityManager em, Entity castleHeartEntity)
        {
            if (em.TryGetComponentData<UserOwner>(castleHeartEntity, out UserOwner heartOwner) &&
                em.TryGetComponentData<User>(heartOwner.Owner._Entity, out User ownerUserData))
            {
                if (em.TryGetComponentData<NameableInteractable>(castleHeartEntity, out NameableInteractable castleNameComp) && !string.IsNullOrWhiteSpace(castleNameComp.Name.ToString()))
                {
                    return $"'{castleNameComp.Name.ToString()}'";
                }
                return $"{ownerUserData.CharacterName.ToString()}'s base";
            }
            return "A base";
        }


        private static bool AreAllDefendersOfHeartOffline(EntityManager em, Entity castleHeartEntity)
        {
            if (!em.TryGetComponentData<UserOwner>(castleHeartEntity, out UserOwner heartOwner))
            {
                return false;
            }
            Entity ownerUserEntity = heartOwner.Owner._Entity;
            if (!em.TryGetComponentData<User>(ownerUserEntity, out User ownerUserData))
            {
                return false;
            }

            Entity clanEntity = ownerUserData.ClanEntity._Entity;
            if (em.Exists(clanEntity) && em.HasComponent<ClanTeam>(clanEntity))
            {
                return !UserHelper.IsAnyClanMemberOnline(em, clanEntity);
            }
            else
            {
                return !ownerUserData.IsConnected;
            }
        }

        private static bool HasSiegeGolemBuff(EntityManager em, Entity playerCharacterEntity)
        {
            if (!em.Exists(playerCharacterEntity) || !em.HasComponent<PlayerCharacter>(playerCharacterEntity))
            {
                return false;
            }
            World world = VWorld.Server;
            if (world == null || !world.IsCreated)
            {
                return false;
            }
            ServerScriptMapper serverScriptMapper = world.GetExistingSystemManaged<ServerScriptMapper>();
            if (serverScriptMapper == null)
            {
                return false;
            }
            ServerGameManager serverGameManager = serverScriptMapper.GetServerGameManager();
            return serverGameManager.TryGetBuff(playerCharacterEntity, PrefabData.SiegeGolemBuff, out _);
        }

        private static bool IsProtectedAndGetHeart(EntityManager em, Entity entity, out Entity castleHeartEntity)
        {
            castleHeartEntity = Entity.Null;
            if (em.HasComponent<CastleHeart>(entity))
            {
                if (em.HasComponent<UserOwner>(entity))
                {
                    castleHeartEntity = entity;
                    return true;
                }
            }
            if (em.HasComponent<CastleHeartConnection>(entity))
            {
                NetworkedEntity networkedCHEntity = em.GetComponentData<CastleHeartConnection>(entity).CastleHeartEntity;
                Entity actualCHEntity = networkedCHEntity._Entity;
                if (em.Exists(actualCHEntity))
                {
                    if (em.HasComponent<CastleHeart>(actualCHEntity) && em.HasComponent<UserOwner>(actualCHEntity))
                    {
                        castleHeartEntity = actualCHEntity;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}