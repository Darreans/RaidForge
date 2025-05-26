using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using Unity.Collections;
using Unity.Entities;
using Stunlock.Core; // PrefabGUID is in here
using RaidForge.Services;
using RaidForge.Config;
using RaidForge.Data; // Still needed for PrefabData.SiegeGolemBuff
using RaidForge.Utils;
using RaidForge; // For Plugin.BepInLogger
using System;
using System.Collections.Generic;
using ProjectM.Gameplay.Systems; // For ServerGameManager access in HasSiegeGolemBuff

namespace RaidForge.Patches
{
    [HarmonyPatch]
    public static class GolemDamageInterceptorPatch
    {
        private static readonly TimeSpan OfflineWarningCooldown = TimeSpan.FromSeconds(10);
        private static Dictionary<Entity, DateTime> _lastOfflineWarningMessageTimes = new Dictionary<Entity, DateTime>();

        [HarmonyPatch(typeof(StatChangeSystem), nameof(StatChangeSystem.OnUpdate))]
        [HarmonyPrefix]
        static bool OnUpdatePrefix(StatChangeSystem __instance)
        {
            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                return true;
            }

            EntityManager em = __instance.EntityManager;
            // Using Allocator.Temp for main-thread short-lived allocations
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
                    Entity sourceAbilityOrEffectEntity = originalEvent.Source;

                    if (!em.Exists(targetEntity) || !IsProtectedAndGetHeart(em, targetEntity, out Entity castleHeartEntity))
                    {
                        continue;
                    }

                    if (!em.Exists(sourceAbilityOrEffectEntity) || !em.HasComponent<EntityOwner>(sourceAbilityOrEffectEntity))
                    {
                        continue;
                    }

                    Entity actualDirectOwner = em.GetComponentData<EntityOwner>(sourceAbilityOrEffectEntity).Owner;

                    if (!em.Exists(actualDirectOwner))
                    {
                        continue;
                    }

                    Entity golemPlayerCharacterEntity = Entity.Null;

                    if (em.HasComponent<PlayerCharacter>(actualDirectOwner))
                    {
                        if (HasSiegeGolemBuff(em, actualDirectOwner))
                        {
                            golemPlayerCharacterEntity = actualDirectOwner;
                        }
                    }
                    else if (em.HasComponent<EntityOwner>(actualDirectOwner))
                    {
                        Entity ultimateOwner = em.GetComponentData<EntityOwner>(actualDirectOwner).Owner;
                        if (em.Exists(ultimateOwner) && em.HasComponent<PlayerCharacter>(ultimateOwner))
                        {
                            if (HasSiegeGolemBuff(em, ultimateOwner))
                            {
                                golemPlayerCharacterEntity = ultimateOwner;
                            }
                        }
                    }

                    if (golemPlayerCharacterEntity == Entity.Null)
                    {
                        continue;
                    }

                    if (OfflineProtectionService.ShouldProtectBase(castleHeartEntity, em, Plugin.BepInLogger))
                    {
                        StatChangeEvent modifiedEvent = originalEvent;
                        modifiedEvent.Change = 0;
                        em.SetComponentData(eventEntity, modifiedEvent);

                        if (em.HasComponent<PlayerCharacter>(golemPlayerCharacterEntity))
                        {
                            PlayerCharacter attackerPc = em.GetComponentData<PlayerCharacter>(golemPlayerCharacterEntity);
                            Entity attackerUserEntity = attackerPc.UserEntity;

                            if (em.Exists(attackerUserEntity) && em.HasComponent<User>(attackerUserEntity))
                            {
                                bool sendMessage = true;
                                if (_lastOfflineWarningMessageTimes.TryGetValue(attackerUserEntity, out DateTime lastTimeSent))
                                {
                                    if (DateTime.UtcNow - lastTimeSent < OfflineWarningCooldown)
                                    {
                                        sendMessage = false;
                                    }
                                }

                                if (sendMessage)
                                {
                                    User attackerUser = em.GetComponentData<User>(attackerUserEntity);
                                    FixedString512Bytes message = new FixedString512Bytes(ChatColors.WarningText("This base is currently offline raid protected!"));
                                    ServerChatUtils.SendSystemMessageToClient(em, attackerUser, ref message);
                                    _lastOfflineWarningMessageTimes[attackerUserEntity] = DateTime.UtcNow;
                                    LoggingHelper.Debug($"Sent offline protection warning to {attackerUser.CharacterName}.");
                                }
                            }
                        }

                        if (TroubleshootingConfig.EnableVerboseLogging.Value && em.Exists(golemPlayerCharacterEntity) && em.HasComponent<PlayerCharacter>(golemPlayerCharacterEntity))
                        {
                            PlayerCharacter playerChar = em.GetComponentData<PlayerCharacter>(golemPlayerCharacterEntity);
                            Entity userEntityForLog = playerChar.UserEntity;

                            if (em.Exists(userEntityForLog) && em.HasComponent<User>(userEntityForLog))
                            {
                                User attackerUser = em.GetComponentData<User>(userEntityForLog);
                                LoggingHelper.Debug($"Prevented Golem damage from {attackerUser.CharacterName} to protected structure {targetEntity} (CH: {castleHeartEntity}) due to offline defender.");
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

        private static bool HasSiegeGolemBuff(EntityManager em, Entity playerCharacterEntity)
        {
            World world = VWorld.Server;
            if (world == null || !world.IsCreated)
            {
                LoggingHelper.Warning("VWorld.Server not ready in HasSiegeGolemBuff.");
                return false;
            }
            ServerScriptMapper serverScriptMapper = world.GetExistingSystemManaged<ServerScriptMapper>();
            if (serverScriptMapper == null)
            {
                LoggingHelper.Warning("ServerScriptMapper is null in HasSiegeGolemBuff.");
                return false;
            }
            ServerGameManager serverGameManager = serverScriptMapper.GetServerGameManager();
            return serverGameManager.TryGetBuff(playerCharacterEntity, PrefabData.SiegeGolemBuff, out _);
        }

        private static bool IsProtectedAndGetHeart(EntityManager em, Entity entity, out Entity castleHeartEntity)
        {
            castleHeartEntity = Entity.Null;

            // Path 1: The entity being damaged IS a CastleHeart itself.
            if (em.HasComponent<CastleHeart>(entity))
            {
                // A Castle Heart is considered protectable if it's owned by a user.
                // The OfflineGraceService will handle if that user (or their clan) is offline.
                if (em.HasComponent<UserOwner>(entity))
                {
                    castleHeartEntity = entity;
                    return true;
                }
                else if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    // This might be a world/system Castle Heart, not a player's.
                    LoggingHelper.Debug($"Entity {entity} is a CastleHeart but lacks UserOwner. Not considered for player-base offline protection by this path.");
                }
            }

            // Path 2: The entity being damaged is a structure connected to a CastleHeart.
            if (em.HasComponent<CastleHeartConnection>(entity))
            {
                NetworkedEntity networkedCHEntity = em.GetComponentData<CastleHeartConnection>(entity).CastleHeartEntity;
                Entity actualCHEntity = networkedCHEntity._Entity; // Get the actual Entity

                if (em.Exists(actualCHEntity))
                {
                    // The connected Castle Heart must exist AND be a CastleHeart component AND have a UserOwner
                    // for it to be considered a protectable player base heart.
                    if (em.HasComponent<CastleHeart>(actualCHEntity) && em.HasComponent<UserOwner>(actualCHEntity))
                    {
                        castleHeartEntity = actualCHEntity;
                        return true;
                    }
                    else if (TroubleshootingConfig.EnableVerboseLogging.Value)
                    {
                        LoggingHelper.Debug($"Structure {entity} is connected to CH {actualCHEntity}, but this CH entity itself is not a CastleHeart or lacks UserOwner. Not protected via this path.");
                    }
                }
                else if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    // Corrected log message for NetworkedEntity
                    LoggingHelper.Debug($"Structure {entity} has CastleHeartConnection to a non-existent CastleHeart (NetworkedEntity: {networkedCHEntity}). Cannot apply offline protection via this path.");
                }
            }
            return false;
        }
    }
}