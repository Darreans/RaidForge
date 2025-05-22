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
        private static ManualLogSource _internalLogger;
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
            LoggingHelper.Info("[RaidInterferenceService] Initialize() CALLED (now INFO level).");
        }

        private static bool EnsureSystemsInitialized() 
        {
            if (!_isServiceLogicInitialized) return false;
            World world = VWorld.Server;
            if (world == null || !world.IsCreated)
            { LoggingHelper.Debug("[RaidInterferenceService] EnsureSystems: Server world NOT available."); return false; }
            if (_entityManager == default) _entityManager = VWorld.EntityManager;
            bool entityManagerValid = _entityManager != default;
            if (entityManagerValid && _debugEventsSystem_instance != null && _serverScriptMapper_instance != null && _serverGameManager_found) return true;
            if (_debugEventsSystem_instance == null) { try { _debugEventsSystem_instance = world.GetExistingSystemManaged<DebugEventsSystem>(); } catch (Exception ex) { LoggingHelper.Warning($"[RIS] EnsureSystems: DES error: {ex}"); _debugEventsSystem_instance = null; } }
            if (_serverScriptMapper_instance == null || !_serverGameManager_found) { try { if (_serverScriptMapper_instance == null) _serverScriptMapper_instance = world.GetExistingSystemManaged<ServerScriptMapper>(); if (_serverScriptMapper_instance != null) { if (!_serverGameManager_found) { _serverGameManager_instance = _serverScriptMapper_instance.GetServerGameManager(); _serverGameManager_found = true; } } else { _serverGameManager_found = false; LoggingHelper.Debug("[RIS] EnsureSystems: SSM is null."); } } catch (Exception ex) { LoggingHelper.Warning($"[RIS] EnsureSystems: SSM/SGM error: {ex}"); _serverScriptMapper_instance = null; _serverGameManager_found = false; } }
            return _entityManager != default && _debugEventsSystem_instance != null && _serverGameManager_found;
        }

        public static void StartSiege(Entity castleHeartEntity, Entity attackerUserEntity) 
        {
            LoggingHelper.Info($"[RIS-StartSiege] METHOD CALLED. CH: {castleHeartEntity}, AttackerUser: {attackerUserEntity}");
            if (!RaidInterferenceConfig.EnableRaidInterference.Value) { LoggingHelper.Debug("[RIS-StartSiege] Disabled via config."); return; }
            if (!_isServiceLogicInitialized || !EnsureSystemsInitialized()) { LoggingHelper.Debug("[RIS-StartSiege] Not ready."); return; }
            try
            { /* ... same logic ... */
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
                    else { LoggingHelper.Warning($"[RIS-StartSiege] CH {castleHeartEntity} Owner problem."); return; }
                }
                else { LoggingHelper.Warning($"[RIS-StartSiege] CH {castleHeartEntity} no UserOwner."); return; }
                if (_entityManager.Exists(attackerUserEntity) && _entityManager.HasComponent<User>(attackerUserEntity))
                {
                    User attackerRealUser = _entityManager.GetComponentData<User>(attackerUserEntity);
                    Entity attackerClanEntity = attackerRealUser.ClanEntity._Entity;
                    attackerInfo = (_entityManager.Exists(attackerClanEntity) && _entityManager.HasComponent<ClanTeam>(attackerClanEntity)) ? attackerClanEntity : attackerUserEntity;
                }
                else { LoggingHelper.Warning($"[RIS-StartSiege] AttackerUserEntity {attackerUserEntity} invalid."); return; }
                if (defendingInfo == Entity.Null || attackerInfo == Entity.Null) { LoggingHelper.Warning($"[RIS-StartSiege] Invalid D/A Info."); return; }
                _activeSieges[castleHeartEntity] = (attackerInfo, defendingInfo);
                LoggingHelper.Info($"[RIS-StartSiege] SIEGE ACTIVE for CH:{castleHeartEntity}. AttackerGrp:{attackerInfo}, DefenderGrp:{defendingInfo}");
            }
            catch (Exception e) { LoggingHelper.Error("[RIS-StartSiege] Exception", e); }
        }

        public static void EndSiege(Entity castleHeartEntity) 
        {
            LoggingHelper.Debug($"[RaidInterferenceService] EndSiege() called for CH: {castleHeartEntity}");
            if (!_isServiceLogicInitialized) return;
            if (_activeSieges.Remove(castleHeartEntity))
                LoggingHelper.Info($"[RIS] SIEGE ENDED for CH {castleHeartEntity}."); 
        }

        public static void StopService() 
        {
            LoggingHelper.Info("[RaidInterferenceService] StopService called.");
            _activeSieges.Clear();
            _isServiceLogicInitialized = false;
        }

        public static void Tick(float deltaTime)
        {
            LoggingHelper.Debug($"[RIS-Tick] Invoked. Enabled: {RaidInterferenceConfig.EnableRaidInterference.Value}, Init: {_isServiceLogicInitialized}, ActiveSieges: {_activeSieges.Count}");
            if (!RaidInterferenceConfig.EnableRaidInterference.Value || !_isServiceLogicInitialized || _activeSieges.Count == 0) return;
            _tickTimer += deltaTime;
            if (_tickTimer < CHECK_INTERVAL_SECONDS) return;
            _tickTimer = 0f;
            if (!EnsureSystemsInitialized()) { LoggingHelper.Debug("[RIS-Tick] Systems not init during Tick execution."); return; }

            LoggingHelper.Debug($"[RIS-Tick] Processing {_activeSieges.Count} active siege(s)..."); 
            List<Entity> heartsToRemoveFromSiege = new List<Entity>();
            var currentSiegeHeartsView = _activeSieges.Keys.ToList();

            foreach (var castleHeartEntity in currentSiegeHeartsView)
            {
                LoggingHelper.Debug($"[RIS-Tick] Iterating for active siege on CH: {castleHeartEntity}"); 
                if (!_activeSieges.TryGetValue(castleHeartEntity, out var siegeParticipants) ||
                    !_entityManager.Exists(castleHeartEntity) ||
                    !_entityManager.HasComponent<CastleHeart>(castleHeartEntity))
                { LoggingHelper.Warning($"[RIS-Tick] CH {castleHeartEntity} invalid. Removing."); heartsToRemoveFromSiege.Add(castleHeartEntity); continue; }

                CastleHeart chComponent = _entityManager.GetComponentData<CastleHeart>(castleHeartEntity);
                if (!chComponent.IsSieged())
                { LoggingHelper.Info($"[RIS-Tick] CH {castleHeartEntity} IsSieged() is false. Removing."); heartsToRemoveFromSiege.Add(castleHeartEntity); continue; }

                var (attackingParticipantInfo, defendingParticipantInfo) = siegeParticipants;
                LoggingHelper.Debug($"[RIS-Tick] CH {castleHeartEntity} IS Sieged. AttackerGrp:{attackingParticipantInfo}, DefenderGrp:{defendingParticipantInfo}. Iterating online players..."); // DEBUG

                var userQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>(), ComponentType.ReadOnly<IsConnected>());
                NativeArray<Entity> users = userQuery.ToEntityArray(Allocator.TempJob);
                LoggingHelper.Debug($"[RIS-Tick] Found {users.Length} online users to check.");
                try
                {
                    foreach (var userEntity in users)
                    {
                        LoggingHelper.Debug($"[RIS-Tick] --- Checking user: {userEntity} ---"); 
                        if (!_entityManager.Exists(userEntity)) { LoggingHelper.Debug($"[RIS-Tick] User {userEntity} no longer exists."); continue; }
                        User user = _entityManager.GetComponentData<User>(userEntity);
                        Entity characterEntity = user.LocalCharacter._Entity;
                        if (!user.IsConnected || characterEntity == Entity.Null || !_entityManager.Exists(characterEntity) || !_entityManager.HasComponent<TilePosition>(characterEntity)) { LoggingHelper.Debug($"[RIS-Tick] User '{user.CharacterName}' invalid/no char/no pos."); continue; }
                        TilePosition tilePos = _entityManager.GetComponentData<TilePosition>(characterEntity);
                        Entity playerClanEntity = user.ClanEntity._Entity;
                        LoggingHelper.Debug($"[RIS-Tick] Details for '{user.CharacterName}': Char={characterEntity}, Clan={playerClanEntity}, Pos={tilePos.Tile}"); 

                        bool isInTerritory = false;
                        NetworkedEntity territoryNetEntity = chComponent.CastleTerritoryEntity;
                        Entity actualTerritoryEntity = territoryNetEntity._Entity;

                        if (_entityManager.Exists(actualTerritoryEntity))
                        {
                            try
                            {
                                CastleTerritory fetchedTerritoryComponent;
                                isInTerritory = ProjectM.CastleBuilding.CastleTerritoryExtensions.IsTileInTerritory(_entityManager, tilePos.Tile, ref actualTerritoryEntity, out fetchedTerritoryComponent);
                                LoggingHelper.Debug($"[RIS-Tick] Player '{user.CharacterName}': IsTileInTerritory for CH {castleHeartEntity} result -> {isInTerritory}");
                            }
                            catch (Exception e_terr) { LoggingHelper.Error($"Territory check error for '{user.CharacterName}' on CH {castleHeartEntity}", e_terr); }
                        }
                        else { LoggingHelper.Debug($"[RIS-Tick] TerritoryEntity for CH {castleHeartEntity} non-existent."); }

                        if (isInTerritory)
                        {
                            LoggingHelper.Debug($"[RIS-Tick] Player '{user.CharacterName}' IS in territory of CH {castleHeartEntity}. Checking attacker/defender status."); 
                            bool isAttacker = (attackingParticipantInfo != Entity.Null) && ((playerClanEntity != Entity.Null && playerClanEntity == attackingParticipantInfo) || (attackingParticipantInfo == userEntity));
                            bool isDefender = (defendingParticipantInfo != Entity.Null) && ((playerClanEntity != Entity.Null && playerClanEntity == defendingParticipantInfo) || (defendingParticipantInfo == userEntity));
                            LoggingHelper.Debug($"[RIS-Tick] Player '{user.CharacterName}' -> IsAttacker:{isAttacker}, IsDefender:{isDefender}"); 

                            if (!isAttacker && !isDefender)
                            {
                                LoggingHelper.Info($"[RIS-Tick] Player '{user.CharacterName}' identified as INTERLOPER for CH {castleHeartEntity}. Attempting debuff.");
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
            LoggingHelper.Debug($"[RIService] ApplyInterloperDebuff for Char: {characterEntity}, User: {userEntity}");
            if (!EnsureSystemsInitialized()) return;
            string charNameForLog = "UnknownCharacter";
            if (_entityManager.Exists(userEntity) && _entityManager.HasComponent<User>(userEntity)) { charNameForLog = _entityManager.GetComponentData<User>(userEntity).CharacterName.ToString(); }
            if (_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.InterloperDebuff, out _)) { return; }
            LoggingHelper.Debug($"[RIService] Attempting to apply interloper debuff to '{charNameForLog}'.");
            var fromCharacter = new FromCharacter() { Character = characterEntity, User = userEntity };
            var applyBuffDebugEvent = new ApplyBuffDebugEvent() { BuffPrefabGUID = PrefabData.InterloperDebuff };
            _debugEventsSystem_instance.ApplyBuff(fromCharacter, applyBuffDebugEvent);
            if (_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.InterloperDebuff, out Entity debuffEntity))
            {
                LoggingHelper.Info($"[RIService] Applied INTERLOPER DEBUFF to {charNameForLog}. DebuffEntity: {debuffEntity}");
            }
            else { LoggingHelper.Warning($"[RIService] Failed to confirm debuff on '{charNameForLog}' AFTER ApplyBuff call."); }
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