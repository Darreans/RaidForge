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

			if (_debugEventsSystem_instance == null) { try { _debugEventsSystem_instance = world.GetExistingSystemManaged<DebugEventsSystem>(); } catch (Exception) { _debugEventsSystem_instance = null; } }

			if (_serverScriptMapper_instance == null) { try { _serverScriptMapper_instance = world.GetExistingSystemManaged<ServerScriptMapper>(); } catch (Exception) { _serverScriptMapper_instance = null; } }

			if (_serverScriptMapper_instance != null)
			{
				try
				{
					_serverGameManager_instance = _serverScriptMapper_instance.GetServerGameManager();
					_serverGameManager_found = true;
				}
				catch
				{
					_serverGameManager_found = false;
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
					LoggingHelper.Info($"[RaidInterference] Siege Started on Heart {castleHeartEntity.Index}. Attacker: {attackerInfo.Index}, Defender: {defendingInfo.Index}");
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
					LoggingHelper.Info($"[RaidInterference] Siege Ended on Heart {castleHeartEntity.Index}.");
				}
			}
		}

		public static void StopService()
		{
			_activeSieges.Clear();
			_lastDebuffMessageTimes.Clear(); 
			_isServiceLogicInitialized = false;
		}

		public static void ProcessInterference()
		{
			if (!RaidInterferenceConfig.EnableRaidInterference.Value || !_isServiceLogicInitialized || _activeSieges.Count == 0) return;

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
				{
					if (TroubleshootingConfig.EnableVerboseLogging.Value)
						LoggingHelper.Info($"[RaidInterference] Heart {castleHeartEntity.Index} no longer reports IsSieged(). Removing.");
					heartsToRemoveFromSiege.Add(castleHeartEntity);
					continue;
				}

				if (RaidInterferenceConfig.DisableInterferenceForDecayingBases.Value)
				{
					if (OfflineProtectionService.IsBaseDecaying(castleHeartEntity, _entityManager))
					{
						continue;
					}
				}

				if (RaidInterferenceConfig.DisableInterferenceForOfflineRaids.Value)
				{
					if (OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, _entityManager))
					{
						continue;
					}
				}

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

							if (TroubleshootingConfig.EnableVerboseLogging.Value)
							{
								LoggingHelper.Info($"[RaidInterference] Player '{user.CharacterName}' is in sieged territory.");
								LoggingHelper.Info($" -> IsAttacker: {isAttacker}");
								LoggingHelper.Info($" -> IsDefender: {isDefender}");
							}

							if (!isAttacker && !isDefender)
							{
								bool shouldBurn = true;

								if (user.IsAdmin) shouldBurn = false;

								if (shouldBurn && RaidInterferenceConfig.ExemptBearFormUsers.Value)
								{
									if (_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.BearFormBuff, out _) ||
										_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.BearFormSkinBuff, out _))
									{
										shouldBurn = false;
										if (TroubleshootingConfig.EnableVerboseLogging.Value)
											LoggingHelper.Info($" -> Skipping burn for '{user.CharacterName}' (Bear Form Detected).");
									}
								}

								if (shouldBurn)
								{
									if (TroubleshootingConfig.EnableVerboseLogging.Value)
										LoggingHelper.Info($" -> APPLYING BURN to '{user.CharacterName}'.");

									ApplyInterloperDebuff(characterEntity, userEntity);
								}
							}
						}
					}
				}
				finally { if (users.IsCreated) users.Dispose(); userQuery.Dispose(); }
			}
			foreach (var heart in heartsToRemoveFromSiege) EndSiege(heart);
		}

		private static void ApplyInterloperDebuff(Entity characterEntity, Entity userEntity)
		{
			if (!EnsureSystemsInitialized()) return;

			if (_serverGameManager_instance.TryGetBuff(characterEntity, PrefabData.InterloperDebuff, out Entity existingBuffEntity))
			{
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
					BuffPrefabGUID = PrefabData.InterloperDebuff
				};

				try
				{
					_debugEventsSystem_instance.ApplyBuff(fromCharacter, applyBuffDebugEvent);
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