using System;
using System.Collections.Generic;
using System.Linq;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using RaidForge.Config;
using RaidForge.Data;
using RaidForge.Utils;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Services
{
    public static class RaidInterferenceService
    {
        private static EntityManager _entityManager;
        private static DebugEventsSystem _debugEventsSystem_instance;
        private static ServerScriptMapper _serverScriptMapper_instance;
        private static ServerGameManager _serverGameManager_instance;
        private static bool _serverGameManager_found = false;
        private static readonly Dictionary<Entity, (Entity AttackerInfo, Entity DefenderInfo)> _activeSieges = new();
        private static bool _isServiceLogicInitialized = false;

        private static Dictionary<Entity, DateTime> _lastDebuffMessageTimes = new Dictionary<Entity, DateTime>();

        public static void Initialize()
        {
            _isServiceLogicInitialized = true;
        }

        private static bool EnsureSystemsInitialized()
        {
            if (!_isServiceLogicInitialized) return false;

            World world = VWorld.Server;
            if (world == null || !world.IsCreated)
            { return false; }

            if (_entityManager == default) _entityManager = VWorld.EntityManager;

            bool entityManagerValid = _entityManager != default;

            if (entityManagerValid && _debugEventsSystem_instance != null && _serverScriptMapper_instance != null && _serverGameManager_found) return true;

            if (_debugEventsSystem_instance == null)
            {
                try
                {
                    _debugEventsSystem_instance = world.GetExistingSystemManaged<DebugEventsSystem>();
                }
                catch (Exception ex)
                {
                    _debugEventsSystem_instance = null;
                    LoggingHelper.Debug($"[RaidInterference] DebugEventsSystem not ready: {ex.Message}");
                }
            }

            if (_serverScriptMapper_instance == null)
            {
                try
                {
                    _serverScriptMapper_instance = world.GetExistingSystemManaged<ServerScriptMapper>();
                }
                catch (Exception ex)
                {
                    _serverScriptMapper_instance = null;
                    LoggingHelper.Debug($"[RaidInterference] ServerScriptMapper not ready: {ex.Message}");
                }
            }

            if (_serverScriptMapper_instance != null)
            {
                try
                {
                    _serverGameManager_instance = _serverScriptMapper_instance.GetServerGameManager();
                    _serverGameManager_found = true;
                }
                catch (Exception ex)
                {
                    _serverGameManager_found = false;
                    LoggingHelper.Debug($"[RaidInterference] ServerGameManager not ready: {ex.Message}");
                }
            }
            else
            {
                _serverGameManager_found = false;
            }

            return _entityManager != default && _debugEventsSystem_instance != null && _serverGameManager_found;
        }

        public static void StartSiege(Entity castleHeartEntity, Entity attackerUserEntity)
        {
            if (!RaidInterferenceConfig.EnableRaidInterference.Value) { return; }
            if (!_isServiceLogicInitialized || !EnsureSystemsInitialized()) { return; }
            try
            {
                if (ShouldSkipInterference(castleHeartEntity))
                {
                    EndSiege(castleHeartEntity);
                    return;
                }

                Entity defendingInfo = Entity.Null; Entity attackerInfo = Entity.Null;

                if (_entityManager.HasComponent<UserOwner>(castleHeartEntity))
                {
                    UserOwner uo = _entityManager.GetComponentData<UserOwner>(castleHeartEntity);
                    Entity ownerUserEntity = uo.Owner._Entity;
                    if (_entityManager.Exists(ownerUserEntity) && _entityManager.HasComponent<User>(ownerUserEntity))
                    {
                        User ownerUser = _entityManager.GetComponentData<User>(ownerUserEntity);
                        Entity ownerClanEntity = ownerUser.ClanEntity._Entity;
                        defendingInfo = (_entityManager.Exists(ownerClanEntity) && _entityManager.HasComponent<ClanTeam>(ownerClanEntity)) ? ownerClanEntity : ownerUserEntity;
                    }
                    else { return; }
                }
                else { return; }

                if (_entityManager.Exists(attackerUserEntity) && _entityManager.HasComponent<User>(attackerUserEntity))
                {
                    User attackerRealUser = _entityManager.GetComponentData<User>(attackerUserEntity);
                    Entity attackerClanEntity = attackerRealUser.ClanEntity._Entity;
                    attackerInfo = (_entityManager.Exists(attackerClanEntity) && _entityManager.HasComponent<ClanTeam>(attackerClanEntity)) ? attackerClanEntity : attackerUserEntity;
                }
                else { return; }

                if (defendingInfo == Entity.Null || attackerInfo == Entity.Null) { return; }

                _activeSieges[castleHeartEntity] = (attackerInfo, defendingInfo);

                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($"[RaidInterference] Siege Started on Heart {castleHeartEntity.Index}. Attacker: {attackerInfo.Index}, Defender: {defendingInfo.Index}");
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[RaidInterference] Error starting siege logic", ex);
            }
        }

        public static void EndSiege(Entity castleHeartEntity)
        {
            if (!_isServiceLogicInitialized) return;
            if (_activeSieges.Remove(castleHeartEntity))
            {
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($"[RaidInterference] Siege Ended on Heart {castleHeartEntity.Index}.");
                }
            }
        }

        public static void StopService()
        {
            ResetRuntimeState();
        }

        public static void ResetRuntimeState()
        {
            _activeSieges.Clear();
            _lastDebuffMessageTimes.Clear();
            _entityManager = default;
            _debugEventsSystem_instance = null;
            _serverScriptMapper_instance = null;
            _serverGameManager_instance = default;
            _serverGameManager_found = false;
            _isServiceLogicInitialized = false;
        }

        public static void ProcessInterference()
        {
            if (!RaidInterferenceConfig.EnableRaidInterference.Value || !_isServiceLogicInitialized || _activeSieges.Count == 0) return;

            if (!EnsureSystemsInitialized()) { return; }

            PruneInvalidRuntimeEntities();

            List<Entity> heartsToRemoveFromSiege = new List<Entity>();
            var currentSiegeHeartsView = _activeSieges.Keys.ToList();
            EntityQuery userQuery = default;
            NativeArray<Entity> users = default;

            try
            {
                userQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>(), ComponentType.ReadOnly<IsConnected>());

                if (userQuery.IsEmptyIgnoreFilter)
                {
                    return;
                }

                users = userQuery.ToEntityArray(Allocator.TempJob);

                foreach (var castleHeartEntity in currentSiegeHeartsView)
                {
                    if (!TryGetActiveSiege(castleHeartEntity, out var siegeParticipants, out CastleHeart castleHeart))
                    {
                        heartsToRemoveFromSiege.Add(castleHeartEntity);
                        continue;
                    }

                    if (ShouldSkipInterference(castleHeartEntity))
                    {
                        heartsToRemoveFromSiege.Add(castleHeartEntity);
                        continue;
                    }

                    ProcessUsersForSiege(castleHeartEntity, castleHeart, siegeParticipants, users);
                }
            }
            finally
            {
                if (users.IsCreated) users.Dispose();
                if (userQuery != default) userQuery.Dispose();

                foreach (var heart in heartsToRemoveFromSiege) EndSiege(heart);
            }
        }

        private static bool TryGetActiveSiege(
            Entity castleHeartEntity,
            out (Entity AttackerInfo, Entity DefenderInfo) siegeParticipants,
            out CastleHeart castleHeart)
        {
            siegeParticipants = default;
            castleHeart = default;

            if (!_activeSieges.TryGetValue(castleHeartEntity, out siegeParticipants) ||
                !_entityManager.Exists(castleHeartEntity) ||
                !_entityManager.HasComponent<CastleHeart>(castleHeartEntity))
            {
                return false;
            }

            castleHeart = _entityManager.GetComponentData<CastleHeart>(castleHeartEntity);

            if (IsCastleHeartInActiveRaidEvent(castleHeart))
            {
                return true;
            }

            if (TroubleshootingConfig.EnableVerboseLogging.Value)
            {
                LoggingHelper.Debug($"[RaidInterference] Heart {castleHeartEntity.Index} no longer reports an active raid event. Removing.");
            }

            return false;
        }

        private static bool IsCastleHeartInActiveRaidEvent(CastleHeart castleHeart)
        {
            return castleHeart.IsSieged() ||
                castleHeart.IsAttacked() ||
                castleHeart.IsRaided() ||
                castleHeart.ActiveEvent == CastleHeartEvent.Attacked ||
                castleHeart.ActiveEvent == CastleHeartEvent.Breached ||
                castleHeart.ActiveEvent == CastleHeartEvent.Raided;
        }

        private static void PruneInvalidRuntimeEntities()
        {
            if (_entityManager == default)
            {
                return;
            }

            foreach (var heart in _activeSieges.Keys.ToList())
            {
                if (heart == Entity.Null || !_entityManager.Exists(heart))
                {
                    _activeSieges.Remove(heart);
                }
            }

            foreach (var userEntity in _lastDebuffMessageTimes.Keys.ToList())
            {
                if (userEntity == Entity.Null || !_entityManager.Exists(userEntity))
                {
                    _lastDebuffMessageTimes.Remove(userEntity);
                }
            }
        }

        private static bool ShouldSkipInterference(Entity castleHeartEntity)
        {
            if (RaidInterferenceConfig.DisableInterferenceForDecayingBases.Value &&
                OfflineProtectionService.IsBaseDecaying(castleHeartEntity, _entityManager))
            {
                return true;
            }

            if (RaidInterferenceConfig.DisableInterferenceForOfflineRaids.Value &&
                OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, _entityManager))
            {
                return true;
            }

            return false;
        }

        private static void ProcessUsersForSiege(
            Entity castleHeartEntity,
            CastleHeart castleHeart,
            (Entity AttackerInfo, Entity DefenderInfo) siegeParticipants,
            NativeArray<Entity> users)
        {
            foreach (var userEntity in users)
            {
                if (!TryGetConnectedPlayer(userEntity, out User user, out Entity characterEntity, out TilePosition tilePosition))
                {
                    continue;
                }

                if (!IsCharacterInSiegeTerritory(castleHeartEntity, castleHeart, tilePosition))
                {
                    continue;
                }

                bool isAttacker = IsParticipant(userEntity, user.ClanEntity._Entity, siegeParticipants.AttackerInfo);
                bool isDefender = IsParticipant(userEntity, user.ClanEntity._Entity, siegeParticipants.DefenderInfo);

                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($"[RaidInterference] Player '{user.CharacterName}' is in sieged territory.");
                    LoggingHelper.Debug($" -> IsAttacker: {isAttacker}");
                    LoggingHelper.Debug($" -> IsDefender: {isDefender}");
                }

                if (isAttacker || isDefender || !ShouldApplyInterferenceBurn(user, characterEntity))
                {
                    continue;
                }

                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($" -> APPLYING BURN to '{user.CharacterName}'.");
                }

                ApplyInterloperDebuff(characterEntity, userEntity);
            }
        }

        private static bool TryGetConnectedPlayer(Entity userEntity, out User user, out Entity characterEntity, out TilePosition tilePosition)
        {
            user = default;
            characterEntity = Entity.Null;
            tilePosition = default;

            if (!_entityManager.Exists(userEntity) || !_entityManager.HasComponent<User>(userEntity))
            {
                return false;
            }

            user = _entityManager.GetComponentData<User>(userEntity);
            characterEntity = user.LocalCharacter._Entity;

            if (!user.IsConnected ||
                characterEntity == Entity.Null ||
                !_entityManager.Exists(characterEntity) ||
                !_entityManager.HasComponent<TilePosition>(characterEntity))
            {
                return false;
            }

            tilePosition = _entityManager.GetComponentData<TilePosition>(characterEntity);
            return true;
        }

        private static bool IsCharacterInSiegeTerritory(Entity castleHeartEntity, CastleHeart castleHeart, TilePosition tilePosition)
        {
            NetworkedEntity networkedTerritoryEntity = castleHeart.CastleTerritoryEntity;
            Entity territoryEntity = networkedTerritoryEntity._Entity;

            if (!_entityManager.Exists(territoryEntity))
            {
                return false;
            }

            try
            {
                CastleTerritory fetchedTerritoryComponent;

                if (!_entityManager.TryGetComponentData<CastleTerritory>(territoryEntity, out fetchedTerritoryComponent))
                {
                    return false;
                }

                if (!fetchedTerritoryComponent.WorldBounds.Contains(tilePosition.Tile))
                {
                    return false;
                }

                return CastleTerritoryExtensions.IsTileInTerritory(_entityManager, tilePosition.Tile, ref territoryEntity, out fetchedTerritoryComponent);
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[RaidInterference] Failed territory check for Heart {castleHeartEntity.Index}: {ex.Message}");
                return false;
            }
        }

        private static bool IsParticipant(Entity userEntity, Entity playerClanEntity, Entity participantInfo)
        {
            return participantInfo != Entity.Null &&
                ((playerClanEntity != Entity.Null && playerClanEntity == participantInfo) || participantInfo == userEntity);
        }

        private static bool ShouldApplyInterferenceBurn(User user, Entity characterEntity)
        {
            if (user.IsAdmin)
            {
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($" -> Skipping burn for '{user.CharacterName}' (Admin Detected).");
                }

                return false;
            }

            bool hasBearBuff = ShapeshiftHelper.IsInBearForm(_entityManager, characterEntity);

            if (TroubleshootingConfig.EnableVerboseLogging.Value)
            {
                LoggingHelper.Debug($" -> BearBuffDetected: {hasBearBuff}");
                LoggingHelper.Debug($" -> BearFormExemptEnabled: {RaidInterferenceConfig.ExemptBearFormUsers.Value}");
            }

            if (RaidInterferenceConfig.ExemptBearFormUsers.Value && hasBearBuff)
            {
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($" -> Skipping burn for '{user.CharacterName}' (Bear Buff Detected).");
                }

                return false;
            }

            return true;
        }

        private static void ApplyInterloperDebuff(Entity characterEntity, Entity userEntity)
        {
            if (!EnsureSystemsInitialized()) return;

            if (_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.InterloperDebuff.Guid, out Entity existingBuffEntity))
            {
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Debug($"[RaidInterference] Refreshing existing InterloperDebuff on character {characterEntity.Index}.");
                }

                if (_entityManager.HasComponent<Age>(existingBuffEntity))
                {
                    _entityManager.SetComponentData(existingBuffEntity, new Age { Value = 0f });
                }

                var lifeTime = new LifeTime
                {
                    Duration = 5.0f,
                    EndAction = LifeTimeEndAction.Destroy
                };

                if (_entityManager.HasComponent<LifeTime>(existingBuffEntity))
                {
                    _entityManager.SetComponentData(existingBuffEntity, lifeTime);
                }
                else
                {
                    _entityManager.AddComponentData(existingBuffEntity, lifeTime);
                }
            }
            else
            {
                var fromCharacter = new FromCharacter() { Character = characterEntity, User = userEntity };

                var applyBuffDebugEvent = new ApplyBuffDebugEvent()
                {
                    BuffPrefabGUID = PrefabData.InterloperDebuff.Guid
                };

                try
                {
                    _debugEventsSystem_instance.ApplyBuff(fromCharacter, applyBuffDebugEvent);

                    if (TroubleshootingConfig.EnableVerboseLogging.Value)
                    {
                        LoggingHelper.Debug($"[RaidInterference] Applied InterloperDebuff to character {characterEntity.Index}.");
                    }
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error($"[RaidInterference] Failed to apply debug buff event: {ex.Message}");
                }
            }

            bool shouldSendMessage = false;
            if (!_lastDebuffMessageTimes.TryGetValue(userEntity, out DateTime lastSentTime))
            {
                shouldSendMessage = true;
            }
            else if ((DateTime.UtcNow - lastSentTime).TotalSeconds > 10)
            {
                shouldSendMessage = true;
            }

            if (shouldSendMessage)
            {
                if (_entityManager.Exists(userEntity) && _entityManager.HasComponent<User>(userEntity))
                {
                    FixedString512Bytes message = new FixedString512Bytes(ChatColors.WarningText("You are interfering in an active siege, leave now!"));
                    ServerChatUtils.SendSystemMessageToClient(_entityManager, _entityManager.GetComponentData<User>(userEntity), ref message);

                    _lastDebuffMessageTimes[userEntity] = DateTime.UtcNow;
                }
            }
        }
    }
}
