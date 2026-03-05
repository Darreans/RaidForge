using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using HookDOTS.API.Attributes;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Gameplay.Systems;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Data;
using RaidForge.Services;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using ProjectM.Scripting;

namespace RaidForge.Patches
{
	public static class DamageInterceptorPatch
	{
		private static readonly TimeSpan OfflineProtectedMessageCooldown = TimeSpan.FromSeconds(10);
		private static Dictionary<Entity, DateTime> _lastProtectedMessageTimes = new();

		private static readonly TimeSpan OfflineSiegeAnnouncementCooldown = TimeSpan.FromSeconds(30);
		private static Dictionary<Entity, DateTime> _lastOfflineSiegeAnnouncementTimes = new();

		private static readonly TimeSpan DecayedRaidAnnouncementCooldown = TimeSpan.FromMinutes(5);
		private static Dictionary<Entity, DateTime> _lastDecayedRaidAnnouncementTimes = new();

		private static EntityQuery _damageQuery;
		private static EntityManager _cachedEntityManager;
		private static bool _queryInitialized = false;

		private static ServerScriptMapper _serverScriptMapper;
		private static ServerGameManager _serverGameManager;
		private static bool _sgmCached = false;

		public static void ResetCache()
		{
			_queryInitialized = false;
			_damageQuery = default;
			_cachedEntityManager = default;
			_sgmCached = false;
			_serverScriptMapper = null;
			_serverGameManager = default;

			_lastProtectedMessageTimes.Clear();
			_lastOfflineSiegeAnnouncementTimes.Clear();
			_lastDecayedRaidAnnouncementTimes.Clear();
		}

		[EcsSystemUpdatePrefix(typeof(DealDamageSystem))]
		public static unsafe void Prefix(SystemState* systemState)
		{
			var em = systemState->EntityManager;

			if (!_queryInitialized || _cachedEntityManager != em)
			{
				_damageQuery = em.CreateEntityQuery(ComponentType.ReadWrite<DealDamageEvent>());
				_cachedEntityManager = em;
				_queryInitialized = true;
			}

			if (_damageQuery.IsEmptyIgnoreFilter) return;

			var damageEventEntities = _damageQuery.ToEntityArray(Allocator.Temp);

			DateTime nowUtc = DateTime.UtcNow;
			bool logDebug = TroubleshootingConfig.EnableVerboseLogging.Value;
			bool optInEnabled = OptInRaidingConfig.EnableOptInRaiding.Value;
			bool orpEnabled = OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value;
			bool announceOfflineDuringGrace = OfflineRaidProtectionConfig.AnnounceOfflineRaidDuringGrace.Value;
			bool announceDecayed = OfflineRaidProtectionConfig.AnnounceDecayedBaseRaid.Value;
			bool forcedRaidDay = OptInScheduleConfig.EnableOptInSchedule.Value && !OptInScheduleConfig.IsOptInSystemAllowedToday();
			float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;
			bool weaponRaidingEnabled = WeaponRaidingConfig.EnableWeaponRaiding.Value;
			float weaponDamageMultiplier = WeaponRaidingConfig.WeaponDamageVsStoneMultiplier.Value;

			if (!_sgmCached && VWorld.Server != null && VWorld.Server.IsCreated)
			{
				try
				{
					_serverScriptMapper = VWorld.Server.GetExistingSystemManaged<ServerScriptMapper>();
					if (_serverScriptMapper != null)
					{
						_serverGameManager = _serverScriptMapper.GetServerGameManager();
						_sgmCached = true;
					}
				}
				catch { }
			}

			try
			{
				foreach (var entity in damageEventEntities)
				{
					try
					{
						if (!em.Exists(entity)) continue;

						var damageEvent = em.GetComponentData<DealDamageEvent>(entity);
						if (damageEvent.MainFactor <= 0 && damageEvent.RawDamage <= 0) continue;

						Entity targetEntity = damageEvent.Target;
						Entity sourceEntity = damageEvent.SpellSource;

						bool isCastleConnected = em.HasComponent<CastleHeartConnection>(targetEntity);
						bool isCastleHeart = em.HasComponent<CastleHeart>(targetEntity);

						if (!isCastleConnected && !isCastleHeart) continue;

						if (!TryGetDefenderKeyFromDamagedEntity(em, targetEntity, out Entity castleHeartEntity, out Entity defenderKeyEntity))
							continue;

						string persistentKey = null;

						if (defenderKeyEntity != Entity.Null)
						{
							if (em.HasComponent<ClanTeam>(defenderKeyEntity))
								persistentKey = PersistentKeyHelper.GetClanKey(em, defenderKeyEntity);
							else if (em.TryGetComponentData<User>(defenderKeyEntity, out User u))
								persistentKey = PersistentKeyHelper.GetUserKey(u.PlatformId);
						}

						if (string.IsNullOrEmpty(persistentKey)) continue;

						string cachedDefenderBaseName = null;
						bool isFastTrackApproved = false;

						bool attackerResolved = false;
						Entity attackerCharEntity = Entity.Null;
						Entity attackerUserEntity = Entity.Null;
						string attackerName = null;
						string attackerKey = null;

						bool isBreached = em.GetComponentData<CastleHeart>(castleHeartEntity).IsSieged();
						bool isDecaying = OfflineProtectionService.IsBaseDecaying(castleHeartEntity, em);

						if (isBreached || isDecaying)
						{
							if (isDecaying && !isBreached)
							{
								RaidMapIconService.AddRaidIcon(castleHeartEntity, isOfflineRaid: false, isDecayRaid: true);

								if (announceDecayed)
								{
									if (!attackerResolved) ResolveAttacker(em, sourceEntity, ref attackerResolved, ref attackerCharEntity, ref attackerUserEntity, ref attackerName, ref attackerKey);
									if (cachedDefenderBaseName == null) cachedDefenderBaseName = GetDefenderBaseName(em, castleHeartEntity);

									if (!string.IsNullOrEmpty(attackerName))
										AnnounceDecayedBaseDamage(em, castleHeartEntity, attackerName, cachedDefenderBaseName, nowUtc);
								}
							}
							if (logDebug)
							{
								if (cachedDefenderBaseName == null) cachedDefenderBaseName = GetDefenderBaseName(em, castleHeartEntity);
								LoggingHelper.Debug($"[Damage] Allowed: Base {cachedDefenderBaseName} is Breached({isBreached}) or Decaying({isDecaying}).");
							}
							isFastTrackApproved = true;
						}

						if (!isFastTrackApproved && RaidForge.Config.ShardConfig.DisableOrpForShardHolders.Value && ShardVulnerabilityService.IsVulnerable(persistentKey))
						{
							if (logDebug)
							{
								var shardEntries = ShardVulnerabilityService.GetEntriesForOwner(persistentKey);
								string shardDetails = string.Join(" | ", shardEntries.Select(e => $"Shard: {e.ShardPrefabGuid} (Latched By: {e.LatchedByUserKey})"));
								LoggingHelper.Debug($"[Damage] Allowed: Owner '{persistentKey}' is vulnerable. Details: {shardDetails}");
							}
							isFastTrackApproved = true;
						}

						if (!isFastTrackApproved)
						{
							if (optInEnabled && !orpEnabled)
							{
								bool isDefenderOptedIn = OptInRaidService.IsOptedIn(persistentKey);
								if (forcedRaidDay) isDefenderOptedIn = true;

								if (!isDefenderOptedIn)
								{
									if (!attackerResolved) ResolveAttacker(em, sourceEntity, ref attackerResolved, ref attackerCharEntity, ref attackerUserEntity, ref attackerName, ref attackerKey);
									if (cachedDefenderBaseName == null) cachedDefenderBaseName = GetDefenderBaseName(em, castleHeartEntity);

									BlockDamageAndNotify(em, entity, attackerUserEntity, cachedDefenderBaseName, "PROTECTED", "(Not Opted-In)", nowUtc);
									continue;
								}

								if (!attackerResolved) ResolveAttacker(em, sourceEntity, ref attackerResolved, ref attackerCharEntity, ref attackerUserEntity, ref attackerName, ref attackerKey);

								bool isAttackerOptedIn = forcedRaidDay || (!string.IsNullOrEmpty(attackerKey) && OptInRaidService.IsOptedIn(attackerKey));
								if (!isAttackerOptedIn)
								{
									if (cachedDefenderBaseName == null) cachedDefenderBaseName = GetDefenderBaseName(em, castleHeartEntity);
									BlockDamageAndNotify(em, entity, attackerUserEntity, cachedDefenderBaseName, "PROTECTED", $"(Attacker {attackerName ?? "Unknown"} is not Opted-In)", nowUtc);
									continue;
								}
							}

							if (orpEnabled)
							{
								bool willBeFullyProtected = false;
								bool isInGracePeriod = false;
								bool allDefendersOffline = false;
								bool checkedAllDefendersOffline = false;

								if (OfflineGraceService.TryGetOfflineStartTime(persistentKey, out DateTime offlineStartTimeUtc))
								{
									TimeSpan timeOffline = nowUtc - offlineStartTimeUtc;

									if (configuredGraceMinutes > 0 && timeOffline.TotalMinutes < configuredGraceMinutes)
									{
										isInGracePeriod = true;
									}
									else
									{
										if (!checkedAllDefendersOffline) { allDefendersOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, em); checkedAllDefendersOffline = true; }
										if (Plugin.ServerHasJustBooted || allDefendersOffline) willBeFullyProtected = true;
									}
								}
								else if (OfflineGraceService.IsUnderDefaultBootORP(persistentKey))
								{
									if (!checkedAllDefendersOffline) { allDefendersOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(castleHeartEntity, em); checkedAllDefendersOffline = true; }
									if (Plugin.ServerHasJustBooted || allDefendersOffline) willBeFullyProtected = true;
								}

								if (allDefendersOffline)
								{
									if (!willBeFullyProtected)
									{
										RaidMapIconService.AddRaidIcon(castleHeartEntity, isOfflineRaid: true, isDecayRaid: false);
									}

									if (announceOfflineDuringGrace && !willBeFullyProtected)
									{
										if (!attackerResolved) ResolveAttacker(em, sourceEntity, ref attackerResolved, ref attackerCharEntity, ref attackerUserEntity, ref attackerName, ref attackerKey);
										if (IsSiegeWeaponDamage(em, sourceEntity, attackerCharEntity, ref attackerName))
										{
											if (cachedDefenderBaseName == null) cachedDefenderBaseName = GetDefenderBaseName(em, castleHeartEntity);
											MakeOfflineSiegeAnnouncement(em, castleHeartEntity, attackerName, cachedDefenderBaseName, isInGracePeriod, nowUtc);
										}
									}
								}

								if (willBeFullyProtected)
								{
									if (!attackerResolved) ResolveAttacker(em, sourceEntity, ref attackerResolved, ref attackerCharEntity, ref attackerUserEntity, ref attackerName, ref attackerKey);
									if (cachedDefenderBaseName == null) cachedDefenderBaseName = GetDefenderBaseName(em, castleHeartEntity);

									BlockDamageAndNotify(em, entity, attackerUserEntity, cachedDefenderBaseName, "OFFLINE PROTECTED", "", nowUtc);
									continue; 
								}
							}
						}

						
						if (weaponRaidingEnabled && damageEvent.MaterialModifiers.StoneStructure <= 0f)
						{
							if (!attackerResolved) ResolveAttacker(em, sourceEntity, ref attackerResolved, ref attackerCharEntity, ref attackerUserEntity, ref attackerName, ref attackerKey);

							if (attackerUserEntity != Entity.Null)
							{
								var modifiedModifiers = damageEvent.MaterialModifiers;
								modifiedModifiers.StoneStructure = weaponDamageMultiplier;
								damageEvent.MaterialModifiers = modifiedModifiers;
								em.SetComponentData(entity, damageEvent);

								if (logDebug) LoggingHelper.Debug($"[WeaponRaiding] Overwrote StoneStructure modifier to {weaponDamageMultiplier}");
							}
						}
					}
					catch (Exception ex)
					{
						if (logDebug) LoggingHelper.Error("[DamageInterceptorPatch] Error processing specific damage event.", ex);
					}
				}
			}
			finally
			{
				if (damageEventEntities.IsCreated) damageEventEntities.Dispose();
			}
		}

		private static void ResolveAttacker(EntityManager em, Entity sourceEntity, ref bool resolved, ref Entity charEntity, ref Entity userEntity, ref string name, ref string key)
		{
			resolved = true;
			if (UserHelper.TryGetPlayerOwnerFromSource(em, sourceEntity, out charEntity, out userEntity))
			{
				if (em.TryGetComponentData<User>(userEntity, out User u))
				{
					name = u.CharacterName.ToString();
					Entity clan = u.ClanEntity._Entity;
					if (clan.Exists() && em.HasComponent<ClanTeam>(clan))
						key = PersistentKeyHelper.GetClanKey(em, clan);
					else
						key = PersistentKeyHelper.GetUserKey(u.PlatformId);
				}
			}
		}

		private static void BlockDamageAndNotify(EntityManager em, Entity eventEntity, Entity attackerUserEntity, string defenderBaseName, string protectionStatusKeyword, string protectionContext, DateTime nowUtc)
		{
			em.DestroyEntity(eventEntity);

			if (attackerUserEntity == Entity.Null) return;

			var messageBuilder = new StringBuilder();
			messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} is "));
			messageBuilder.Append(ChatColors.AccentText(protectionStatusKeyword));

			if (!string.IsNullOrEmpty(protectionContext))
				messageBuilder.Append(ChatColors.InfoText($" {protectionContext}."));
			else
				messageBuilder.Append(ChatColors.InfoText("."));

			if (!_lastProtectedMessageTimes.TryGetValue(attackerUserEntity, out DateTime lastTimeSent) || (nowUtc - lastTimeSent) > OfflineProtectedMessageCooldown)
			{
				if (em.TryGetComponentData<User>(attackerUserEntity, out User attackerUser))
				{
					FixedString512Bytes message = new FixedString512Bytes(messageBuilder.ToString());
					ServerChatUtils.SendSystemMessageToClient(em, attackerUser, ref message);
					_lastProtectedMessageTimes[attackerUserEntity] = nowUtc;
				}
			}
		}

		private static bool TryGetDefenderKeyFromDamagedEntity(EntityManager em, Entity damagedEntity, out Entity castleHeartEntity, out Entity defenderKeyEntity)
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

		private static bool IsSiegeWeaponDamage(EntityManager em, Entity sourceEntity, Entity attackerCharEntity, ref string attackerName)
		{
			if (attackerCharEntity != Entity.Null && HasSiegeGolemBuff(em, attackerCharEntity)) return true;

			PrefabGUID sourcePrefabGuid = default;
			if (em.Exists(sourceEntity) && em.HasComponent<PrefabGUID>(sourceEntity)) sourcePrefabGuid = em.GetComponentData<PrefabGUID>(sourceEntity);
			else if (em.Exists(sourceEntity) && em.HasComponent<EntityOwner>(sourceEntity))
			{
				Entity ownerOfSource = em.GetComponentData<EntityOwner>(sourceEntity).Owner;
				if (em.Exists(ownerOfSource) && em.HasComponent<PrefabGUID>(ownerOfSource)) sourcePrefabGuid = em.GetComponentData<PrefabGUID>(ownerOfSource);
			}

			if (sourcePrefabGuid.Equals(PrefabData.TntExplosivePrefab1) || sourcePrefabGuid.Equals(PrefabData.TntExplosivePrefab2))
			{
				if (string.IsNullOrEmpty(attackerName)) attackerName = "Explosives";
				return true;
			}
			return false;
		}

		private static void MakeOfflineSiegeAnnouncement(EntityManager em, Entity castleHeartEntity, string attackerName, string defenderBaseName, bool isInGracePeriod, DateTime nowUtc)
		{
			if (!_lastOfflineSiegeAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnTime) || (nowUtc - lastAnnTime) > OfflineSiegeAnnouncementCooldown)
			{
				var messageBuilder = new StringBuilder();
				messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} is being "));
				messageBuilder.Append(ChatColors.WarningText("offline raided"));
				messageBuilder.Append(ChatColors.InfoText(" by "));
				messageBuilder.Append(ChatColors.HighlightText(attackerName ?? "Unknown"));
				messageBuilder.Append(ChatColors.InfoText("!"));

				FixedString512Bytes annMsg = new FixedString512Bytes(messageBuilder.ToString());
				ServerChatUtils.SendSystemMessageToAllClients(em, ref annMsg);
				_lastOfflineSiegeAnnouncementTimes[castleHeartEntity] = nowUtc;
			}
		}

		private static void AnnounceDecayedBaseDamage(EntityManager em, Entity castleHeartEntity, string attackerName, string defenderBaseName, DateTime nowUtc)
		{
			if (!_lastDecayedRaidAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnTime) || (nowUtc - lastAnnTime) > DecayedRaidAnnouncementCooldown)
			{
				var messageBuilder = new StringBuilder();
				messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} "));
				messageBuilder.Append(ChatColors.WarningText("is DECAYED"));
				messageBuilder.Append(ChatColors.InfoText(" and being raided by "));
				messageBuilder.Append(ChatColors.HighlightText(attackerName ?? "Unknown"));
				messageBuilder.Append(ChatColors.InfoText("!"));

				FixedString512Bytes decayedAnnMessage = new FixedString512Bytes(messageBuilder.ToString());
				ServerChatUtils.SendSystemMessageToAllClients(em, ref decayedAnnMessage);
				_lastDecayedRaidAnnouncementTimes[castleHeartEntity] = nowUtc;
			}
		}

		private static bool HasSiegeGolemBuff(EntityManager em, Entity playerCharacterEntity)
		{
			if (!em.Exists(playerCharacterEntity) || !em.HasComponent<PlayerCharacter>(playerCharacterEntity)) return false;
			if (!_sgmCached) return false;

			try
			{
				return _serverGameManager.TryGetBuff(playerCharacterEntity, PrefabData.SiegeGolemBuff, out _);
			}
			catch { return false; }
		}
	}
}