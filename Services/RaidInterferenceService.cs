using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
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
        private static DateTime _lastDebuffMessageTimeForAllPlayers = DateTime.MinValue;
        private static float _tickTimer = 0f;
        private const float CHECK_INTERVAL_SECONDS = 1.5f;

        public static void Initialize(ManualLogSource loggerFromPlugin)
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
            if (_debugEventsSystem_instance == null) { try { _debugEventsSystem_instance = world.GetExistingSystemManaged<DebugEventsSystem>(); } catch (Exception) { _debugEventsSystem_instance = null; } }
            if (_serverScriptMapper_instance == null || !_serverGameManager_found) { try { if (_serverScriptMapper_instance == null) _serverScriptMapper_instance = world.GetExistingSystemManaged<ServerScriptMapper>(); if (_serverScriptMapper_instance != null) { if (!_serverGameManager_found) { _serverGameManager_instance = _serverScriptMapper_instance.GetServerGameManager(); _serverGameManager_found = true; } } else { _serverGameManager_found = false; } } catch (Exception) { _serverScriptMapper_instance = null; _serverGameManager_found = false; } }
            return _entityManager != default && _debugEventsSystem_instance != null && _serverGameManager_found;
        }

        public static void StartSiege(Entity castleHeartEntity, Entity attackerUserEntity)
        {
            if (!RaidInterferenceConfig.EnableRaidInterference.Value) { return; }
            if (!_isServiceLogicInitialized || !EnsureSystemsInitialized()) { return; }
            try
            {
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
            }
            catch (Exception) { }
        }

        public static void EndSiege(Entity castleHeartEntity)
        {
            if (!_isServiceLogicInitialized) return;
            _activeSieges.Remove(castleHeartEntity);
        }

        public static void StopService()
        {
            _activeSieges.Clear();
            _isServiceLogicInitialized = false;
        }

        public static void Tick(float deltaTime)
        {
            if (!RaidInterferenceConfig.EnableRaidInterference.Value || !_isServiceLogicInitialized || _activeSieges.Count == 0) return;
            _tickTimer += deltaTime;
            if (_tickTimer < CHECK_INTERVAL_SECONDS) return;
            _tickTimer = 0f;
            if (!EnsureSystemsInitialized()) { return; }

            List<Entity> heartsToRemoveFromSiege = new List<Entity>();
            var currentSiegeHeartsView = _activeSieges.Keys.ToList();

            foreach (var castleHeartEntity in currentSiegeHeartsView)
            {
                if (!_activeSieges.TryGetValue(castleHeartEntity, out var siegeParticipants) ||
                    !_entityManager.Exists(castleHeartEntity) ||
                    !_entityManager.HasComponent<CastleHeart>(castleHeartEntity))
                { heartsToRemoveFromSiege.Add(castleHeartEntity); continue; }

                CastleHeart chComponent = _entityManager.GetComponentData<CastleHeart>(castleHeartEntity);
                if (!chComponent.IsSieged())
                { heartsToRemoveFromSiege.Add(castleHeartEntity); continue; }

                var (attackingParticipantInfo, defendingParticipantInfo) = siegeParticipants;

                var userQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>(), ComponentType.ReadOnly<IsConnected>());
                NativeArray<Entity> users = userQuery.ToEntityArray(Allocator.TempJob);
                try
                {
                    foreach (var userEntity in users)
                    {
                        if (!_entityManager.Exists(userEntity)) { continue; }
                        User user = _entityManager.GetComponentData<User>(userEntity);
                        Entity characterEntity = user.LocalCharacter._Entity;
                        if (!user.IsConnected || characterEntity == Entity.Null || !_entityManager.Exists(characterEntity) || !_entityManager.HasComponent<TilePosition>(characterEntity)) { continue; }
                        TilePosition tilePos = _entityManager.GetComponentData<TilePosition>(characterEntity);
                        Entity playerClanEntity = user.ClanEntity._Entity;

                        bool isInTerritory = false;
                        NetworkedEntity territoryNetEntity = chComponent.CastleTerritoryEntity;
                        Entity actualTerritoryEntity = territoryNetEntity._Entity;

                        if (_entityManager.Exists(actualTerritoryEntity))
                        {
                            try
                            {
                                CastleTerritory fetchedTerritoryComponent;
                                isInTerritory = ProjectM.CastleBuilding.CastleTerritoryExtensions.IsTileInTerritory(_entityManager, tilePos.Tile, ref actualTerritoryEntity, out fetchedTerritoryComponent);
                            }
                            catch (Exception) { }
                        }

                        if (isInTerritory)
                        {
                            bool isAttacker = (attackingParticipantInfo != Entity.Null) && ((playerClanEntity != Entity.Null && playerClanEntity == attackingParticipantInfo) || (attackingParticipantInfo == userEntity));
                            bool isDefender = (defendingParticipantInfo != Entity.Null) && ((playerClanEntity != Entity.Null && playerClanEntity == defendingParticipantInfo) || (defendingParticipantInfo == userEntity));

                            if (!isAttacker && !isDefender)
                            {
                                ApplyInterloperDebuff(characterEntity, userEntity);
                            }
                        }
                    }
                }
                finally { if (users.IsCreated) users.Dispose(); }
            }
            foreach (var heart in heartsToRemoveFromSiege) EndSiege(heart);
        }

        private static void ApplyInterloperDebuff(Entity characterEntity, Entity userEntity)
        {
            if (!EnsureSystemsInitialized()) return;
            if (_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.InterloperDebuff, out _)) { return; }

            var fromCharacter = new FromCharacter() { Character = characterEntity, User = userEntity };
            var applyBuffDebugEvent = new ApplyBuffDebugEvent() { BuffPrefabGUID = PrefabData.InterloperDebuff };
            _debugEventsSystem_instance.ApplyBuff(fromCharacter, applyBuffDebugEvent);

            _serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.InterloperDebuff, out Entity debuffEntity);

            if ((DateTime.UtcNow - _lastDebuffMessageTimeForAllPlayers).TotalSeconds > 10)
            {
                if (_entityManager.Exists(userEntity) && _entityManager.HasComponent<User>(userEntity))
                {
                    FixedString512Bytes message = new FixedString512Bytes(ChatColors.WarningText("You are interfering in an active siege, leave now!"));
                    ServerChatUtils.SendSystemMessageToClient(_entityManager, _entityManager.GetComponentData<User>(userEntity), ref message);
                    _lastDebuffMessageTimeForAllPlayers = DateTime.UtcNow;
                }
            }
        }
    }
}