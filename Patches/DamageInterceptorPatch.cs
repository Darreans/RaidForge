using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
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
using System.Text;
using ProjectM.Scripting;
using ProjectM.Gameplay.Systems;

namespace RaidForge.Patches
{
    [HarmonyPatch]
    public static class DamageInterceptorPatch
    {
        private static readonly TimeSpan OfflineProtectedMessageCooldown = TimeSpan.FromSeconds(10);
        private static Dictionary<Entity, DateTime> _lastProtectedMessageTimes = new Dictionary<Entity, DateTime>();
        private static readonly TimeSpan OfflineSiegeAnnouncementCooldown = TimeSpan.FromSeconds(30);
        private static Dictionary<Entity, DateTime> _lastOfflineSiegeAnnouncementTimes = new Dictionary<Entity, DateTime>();
        private static readonly TimeSpan DecayedRaidAnnouncementCooldown = TimeSpan.FromMinutes(5);
        private static Dictionary<Entity, DateTime> _lastDecayedRaidAnnouncementTimes = new Dictionary<Entity, DateTime>();

        private static bool TryGetDefenderKeyFromDamagedEntity(EntityManager em, Entity damagedEntity,
                                                               out Entity castleHeartEntity, out Entity defenderKeyEntity)
        {
            castleHeartEntity = Entity.Null; defenderKeyEntity = Entity.Null;
            if (em.HasComponent<CastleHeartConnection>(damagedEntity)) castleHeartEntity = em.GetComponentData<CastleHeartConnection>(damagedEntity).CastleHeartEntity._Entity;
            else if (em.HasComponent<CastleHeart>(damagedEntity)) castleHeartEntity = damagedEntity;
            if (castleHeartEntity == Entity.Null || !em.Exists(castleHeartEntity)) return false;
            if (!OwnershipCacheService.TryGetHeartOwner(castleHeartEntity, out Entity ownerUserEntity) || ownerUserEntity == Entity.Null) return false;
            if (!em.Exists(ownerUserEntity) || !em.HasComponent<User>(ownerUserEntity)) return false;
            if (OwnershipCacheService.TryGetUserClan(ownerUserEntity, out Entity ownerClanEntity) && ownerClanEntity != Entity.Null && em.Exists(ownerClanEntity)) defenderKeyEntity = ownerClanEntity;
            else defenderKeyEntity = ownerUserEntity;
            return true;
        }

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
                    if (originalEvent.StatType != StatType.Health || originalEvent.Change >= 0) continue;

                    Entity targetEntity = originalEvent.Entity;
                    Entity sourceOfDamageEntity = originalEvent.Source;

                    if (TryGetDefenderKeyFromDamagedEntity(em, targetEntity, out Entity castleHeartEntity, out Entity defenderKeyEntity))
                    {
                        string defenderBaseName = GetDefenderBaseName(em, castleHeartEntity);
                        string persistentKey = null;
                        if (defenderKeyEntity != Entity.Null)
                        {
                            if (em.HasComponent<ClanTeam>(defenderKeyEntity)) persistentKey = PersistentKeyHelper.GetClanKey(em, defenderKeyEntity);
                            else if (em.HasComponent<User>(defenderKeyEntity) && em.TryGetComponentData<User>(defenderKeyEntity, out User u)) persistentKey = PersistentKeyHelper.GetUserKey(u.PlatformId);
                        }

                        if (em.GetComponentData<CastleHeart>(castleHeartEntity).IsSieged() || OfflineProtectionService.IsBaseDecaying(castleHeartEntity, em))
                        {
                        }
                        else if (ShardVulnerabilityService.IsVulnerable(persistentKey))
                        {
                            continue;
                        }
                        else if (OptInRaidingConfig.EnableOptInRaiding.Value && !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                        {
                            bool isProtected = true;
                            string reason = "(Not Opted-In)";

                            if (OptInScheduleConfig.EnableOptInSchedule.Value)
                            {
                                if (OptInScheduleConfig.IsOptInSystemAllowedToday())
                                {
                                    isProtected = !OptInRaidService.IsOptedIn(persistentKey);
                                }
                                else
                                {
                                    isProtected = false;
                                }
                            }
                            else
                            {
                                isProtected = !OptInRaidService.IsOptedIn(persistentKey);
                            }

                            if (isProtected)
                            {
                                BlockDamageAndNotify(em, eventEntity, originalEvent, sourceOfDamageEntity, defenderBaseName, "PROTECTED", reason);
                                continue;
                            }
                        }

                        bool isBreached = em.HasComponent<CastleHeart>(castleHeartEntity) && em.GetComponentData<CastleHeart>(castleHeartEntity).IsSieged();
                        bool isDecaying = OfflineProtectionService.IsBaseDecaying(castleHeartEntity, em);
                        bool orpDamageBlockingEnabled = OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value;

                        bool willBeFullyProtectedByORP = false;
                        bool isInGracePeriod = false;

                        if (orpDamageBlockingEnabled && !isBreached && !isDecaying && !string.IsNullOrEmpty(persistentKey))
                        {
                            bool effectivelyAllOffline;
                            if (OfflineGraceService.TryGetOfflineStartTime(persistentKey, out DateTime offlineStartTimeUtc))
                            {
                                TimeSpan timeOffline = DateTime.UtcNow - offlineStartTimeUtc;
                                float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;

                                if (configuredGraceMinutes > 0 && timeOffline.TotalMinutes < configuredGraceMinutes)
                                {
                                    isInGracePeriod = true;
                                }
                                else
                                {
                                    effectivelyAllOffline = Plugin.ServerHasJustBooted || OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, em);
                                    if (effectivelyAllOffline)
                                    {
                                        willBeFullyProtectedByORP = true;
                                    }
                                }
                            }
                            else if (OfflineGraceService.IsUnderDefaultBootORP(persistentKey))
                            {
                                effectivelyAllOffline = Plugin.ServerHasJustBooted || OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, em);
                                if (effectivelyAllOffline)
                                {
                                    willBeFullyProtectedByORP = true;
                                }
                            }
                        }

                        if (!isBreached)
                        {
                            if (isDecaying && OfflineRaidProtectionConfig.AnnounceDecayedBaseRaid.Value)
                            {
                                AnnounceDecayedBaseDamage(em, castleHeartEntity, sourceOfDamageEntity, defenderBaseName);
                            }
                            else if (OfflineRaidProtectionConfig.AnnounceOfflineRaidDuringGrace.Value)
                            {
                                if (OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, em))
                                {
                                    if (IsSiegeWeaponDamage(em, sourceOfDamageEntity, out string attackerName))
                                    {
                                        if (isInGracePeriod || !orpDamageBlockingEnabled || (orpDamageBlockingEnabled && !willBeFullyProtectedByORP))
                                        {
                                            MakeOfflineSiegeAnnouncement(em, castleHeartEntity, attackerName, defenderBaseName, isInGracePeriod);
                                        }
                                    }
                                }
                            }
                        }

                        if (willBeFullyProtectedByORP)
                        {
                            BlockDamageAndNotify(em, eventEntity, originalEvent, sourceOfDamageEntity, defenderBaseName, "OFFLINE PROTECTED", "");
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

        private static void BlockDamageAndNotify(EntityManager em, Entity eventEntity, StatChangeEvent originalEvent, Entity sourceOfDamageEntity, string defenderBaseName, string protectionStatusKeyword, string protectionContext)
        {
            StatChangeEvent modifiedEvent = originalEvent;
            modifiedEvent.Change = 0;
            em.SetComponentData(eventEntity, modifiedEvent);

            var messageBuilder = new StringBuilder();
            messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} is "));
            messageBuilder.Append(ChatColors.AccentText(protectionStatusKeyword));

            if (!string.IsNullOrEmpty(protectionContext))
            {
                messageBuilder.Append(ChatColors.InfoText($" {protectionContext}."));
            }
            else
            {
                messageBuilder.Append(ChatColors.InfoText("."));
            }

            SendAttackerMessageWithCooldown(em, sourceOfDamageEntity, messageBuilder.ToString(), _lastProtectedMessageTimes, OfflineProtectedMessageCooldown);
        }

        private static bool IsSiegeWeaponDamage(EntityManager em, Entity sourceOfDamageEntity, out string attackerName)
        {
            attackerName = "Unknown Attacker";
            bool isPlayerOwnedSource = UserHelper.TryGetPlayerOwnerFromSource(em, sourceOfDamageEntity, out Entity attackerCharEntity, out Entity attackerUserEntity);
            if (isPlayerOwnedSource && em.TryGetComponentData<User>(attackerUserEntity, out User attackerUserData)) attackerName = attackerUserData.CharacterName.ToString();
            if (isPlayerOwnedSource && HasSiegeGolemBuff(em, attackerCharEntity)) return true;
            PrefabGUID sourcePrefabGuid = default;
            if (em.Exists(sourceOfDamageEntity) && em.HasComponent<PrefabGUID>(sourceOfDamageEntity)) sourcePrefabGuid = em.GetComponentData<PrefabGUID>(sourceOfDamageEntity);
            else if (em.Exists(sourceOfDamageEntity) && em.HasComponent<EntityOwner>(sourceOfDamageEntity))
            {
                Entity ownerOfSource = em.GetComponentData<EntityOwner>(sourceOfDamageEntity).Owner;
                if (em.Exists(ownerOfSource) && em.HasComponent<PrefabGUID>(ownerOfSource)) sourcePrefabGuid = em.GetComponentData<PrefabGUID>(ownerOfSource);
            }
            if (sourcePrefabGuid.Equals(PrefabData.TntExplosivePrefab1) || sourcePrefabGuid.Equals(PrefabData.TntExplosivePrefab2))
            {
                if (attackerName == "Unknown Attacker") attackerName = "Explosives";
                return true;
            }
            return false;
        }

        private static void MakeOfflineSiegeAnnouncement(EntityManager em, Entity castleHeartEntity, string attackerName, string defenderBaseName, bool isInGracePeriod)
        {
            if (!_lastOfflineSiegeAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnTime) ||
                (DateTime.UtcNow - lastAnnTime) > OfflineSiegeAnnouncementCooldown)
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} is being "));
                messageBuilder.Append(ChatColors.WarningText("offline raided"));
                if (isInGracePeriod)
                {
                }
                messageBuilder.Append(ChatColors.InfoText(" by "));
                messageBuilder.Append(ChatColors.HighlightText(attackerName));
                messageBuilder.Append(ChatColors.InfoText("!"));

                FixedString512Bytes annMsg = new FixedString512Bytes(messageBuilder.ToString());
                ServerChatUtils.SendSystemMessageToAllClients(em, ref annMsg);
                _lastOfflineSiegeAnnouncementTimes[castleHeartEntity] = DateTime.UtcNow;
            }
        }

        private static void AnnounceDecayedBaseDamage(EntityManager em, Entity castleHeartEntity, Entity sourceOfDamageEntity, string defenderBaseName)
        {
            if (!OfflineRaidProtectionConfig.AnnounceDecayedBaseRaid.Value) return;
            if (UserHelper.TryGetPlayerOwnerFromSource(em, sourceOfDamageEntity, out _, out Entity attackerUserEntity))
            {
                if (em.TryGetComponentData<User>(attackerUserEntity, out User attackerUserData))
                {
                    if (!_lastDecayedRaidAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnTime) ||
                        (DateTime.UtcNow - lastAnnTime) > DecayedRaidAnnouncementCooldown)
                    {
                        var messageBuilder = new StringBuilder();
                        messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} "));
                        messageBuilder.Append(ChatColors.WarningText("is DECAYED"));
                        messageBuilder.Append(ChatColors.InfoText(" and being raided by "));
                        messageBuilder.Append(ChatColors.HighlightText(attackerUserData.CharacterName.ToString()));
                        messageBuilder.Append(ChatColors.InfoText("!"));
                        FixedString512Bytes decayedAnnMessage = new FixedString512Bytes(messageBuilder.ToString());
                        ServerChatUtils.SendSystemMessageToAllClients(em, ref decayedAnnMessage);
                        _lastDecayedRaidAnnouncementTimes[castleHeartEntity] = DateTime.UtcNow;
                    }
                }
            }
        }

        private static void SendAttackerMessageWithCooldown(EntityManager em, Entity sourceOfDamageEntity, string fullyFormattedMessageText, Dictionary<Entity, DateTime> cooldownDict, TimeSpan cooldownDuration)
        {
            if (UserHelper.TryGetPlayerOwnerFromSource(em, sourceOfDamageEntity, out _, out Entity attackerUserEntity))
            {
                if (!cooldownDict.TryGetValue(attackerUserEntity, out DateTime lastTimeSent) ||
                    (DateTime.UtcNow - lastTimeSent) > cooldownDuration)
                {
                    if (em.TryGetComponentData<User>(attackerUserEntity, out User attackerUser))
                    {
                        FixedString512Bytes message = new FixedString512Bytes(fullyFormattedMessageText);
                        ServerChatUtils.SendSystemMessageToClient(em, attackerUser, ref message);
                        cooldownDict[attackerUserEntity] = DateTime.UtcNow;
                    }
                }
            }
        }

        private static string GetDefenderBaseName(EntityManager em, Entity castleHeartEntity)
        {
            if (castleHeartEntity != Entity.Null && em.TryGetComponentData<UserOwner>(castleHeartEntity, out UserOwner heartOwner) &&
                em.Exists(heartOwner.Owner._Entity) &&
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

        private static bool HasSiegeGolemBuff(EntityManager em, Entity playerCharacterEntity)
        {
            if (!em.Exists(playerCharacterEntity) || !em.HasComponent<PlayerCharacter>(playerCharacterEntity)) return false;
            World world = VWorld.Server;
            if (world == null || !world.IsCreated) return false;
            var serverScriptMapper = world.GetExistingSystemManaged<ServerScriptMapper>();
            if (serverScriptMapper == null) return false;
            ServerGameManager sgm = serverScriptMapper.GetServerGameManager();
            try { return sgm.TryGetBuff(playerCharacterEntity, PrefabData.SiegeGolemBuff, out _); }
            catch { return false; }
        }
    }
}