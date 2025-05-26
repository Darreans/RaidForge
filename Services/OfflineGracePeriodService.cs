using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Collections;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using ProjectM.CastleBuilding;
using RaidForge.Config; // Make sure this is included
using RaidForge.Utils;

namespace RaidForge.Services
{
    public static class OfflineGraceService
    {
        private struct ClanGraceDetails
        {
            public DateTime GracePeriodStartTimeUtc;
            public FixedString64Bytes LastMemberCharacterName;
        }

        private static Dictionary<Entity, ClanGraceDetails> _gracePeriodEntries = new Dictionary<Entity, ClanGraceDetails>();
        // REMOVED: private const double GRACE_PERIOD_MINUTES = 15.0;
        private static ManualLogSource _internalLogger;

        public static void Initialize(ManualLogSource logger)
        {
            _internalLogger = logger;
            LoggingHelper.Debug("[OfflineGraceService] Initialized.");
        }

        public static void HandleUserDisconnected(EntityManager entityManager, Entity disconnectedUserEntity)
        {
            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                // LoggingHelper.Debug already checks for verbose logging
                LoggingHelper.Debug("[OfflineGraceService] Offline raid protection is disabled. Skipping HandleUserDisconnected.");
                return;
            }

            LoggingHelper.Debug($"[OfflineGraceService] HandleUserDisconnected CALLED for UserEntity: {disconnectedUserEntity}.");
            if (!entityManager.Exists(disconnectedUserEntity) || !entityManager.HasComponent<User>(disconnectedUserEntity))
            {
                LoggingHelper.Warning($"[OfflineGraceService] HandleUserDisconnected: UserEntity {disconnectedUserEntity} does not exist or lacks User component. Aborting.");
                return;
            }

            User disconnectedUserData = entityManager.GetComponentData<User>(disconnectedUserEntity);
            LoggingHelper.Debug($"[OfflineGraceService] Processing disconnect for User: {disconnectedUserData.CharacterName.ToString()} (PlatformID: {disconnectedUserData.PlatformId})");

            Entity clanEntity = disconnectedUserData.ClanEntity._Entity;
            LoggingHelper.Debug($"[OfflineGraceService] User's ClanEntity: {clanEntity}");

            Entity keyForGrace = Entity.Null;
            bool isClanLogout = false;

            if (entityManager.Exists(clanEntity) && entityManager.HasComponent<ClanTeam>(clanEntity))
            {
                LoggingHelper.Debug($"[OfflineGraceService] User {disconnectedUserData.CharacterName.ToString()} is in Clan {clanEntity}. Checking other members...");
                keyForGrace = clanEntity;
                isClanLogout = true;

                if (UserHelper.IsAnyClanMemberOnline(entityManager, clanEntity, disconnectedUserEntity))
                {
                    LoggingHelper.Debug($"[OfflineGraceService] Clan {clanEntity} still has members online after {disconnectedUserData.CharacterName.ToString()} logged off. No grace period started for clan.");
                    return;
                }
                LoggingHelper.Info($"[OfflineGraceService] {disconnectedUserData.CharacterName.ToString()} was the last online member of Clan {clanEntity}. Setting grace period for clan.");
            }
            else
            {
                LoggingHelper.Info($"[OfflineGraceService] User {disconnectedUserData.CharacterName.ToString()} is not in a valid clan. Setting grace period for SOLO user entity {disconnectedUserEntity}.");
                keyForGrace = disconnectedUserEntity;
                isClanLogout = false;
            }

            // Check if GracePeriodDurationMinutes is positive before setting a grace period
            if (OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value > 0)
            {
                _gracePeriodEntries[keyForGrace] = new ClanGraceDetails
                {
                    GracePeriodStartTimeUtc = DateTime.UtcNow,
                    LastMemberCharacterName = disconnectedUserData.CharacterName
                };
                LoggingHelper.Debug($"[OfflineGraceService] Grace period set for Key: {keyForGrace} (IsClan: {isClanLogout}). StartTimeUTC: {_gracePeriodEntries[keyForGrace].GracePeriodStartTimeUtc}, TriggeredBy: {disconnectedUserData.CharacterName.ToString()}. Duration: {OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value} mins.");
            }
            else
            {
                LoggingHelper.Debug($"[OfflineGraceService] Grace period duration is 0 or negative. No grace period timer started for Key: {keyForGrace}. Protection (if applicable) would be immediate.");
            }
        }

        public static void HandleClanMemberDeparted(EntityManager entityManager, Entity userWhoLeft, Entity clanThatWasLeft, FixedString64Bytes userWhoLeftCharacterName)
        {
            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                LoggingHelper.Debug("[OfflineGraceService] Offline raid protection is disabled. Skipping HandleClanMemberDeparted.");
                return;
            }

            LoggingHelper.Debug($"[OfflineGraceService] HandleClanMemberDeparted CALLED. UserWhoLeft: {userWhoLeft} ('{userWhoLeftCharacterName.ToString()}'), ClanTheyLeft: {clanThatWasLeft}.");

            if (!entityManager.Exists(clanThatWasLeft) || !entityManager.HasComponent<ClanTeam>(clanThatWasLeft))
            {
                LoggingHelper.Warning($"[OfflineGraceService] HandleClanMemberDeparted: ClanEntity {clanThatWasLeft} invalid or not a ClanTeam. Aborting.");
                return;
            }

            if (!UserHelper.IsAnyClanMemberOnline(entityManager, clanThatWasLeft))
            {
                LoggingHelper.Info($"[OfflineGraceService] No members of Clan {clanThatWasLeft} are online after {userWhoLeftCharacterName.ToString()} departed. Setting grace period for clan.");
                // Check if GracePeriodDurationMinutes is positive
                if (OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value > 0)
                {
                    _gracePeriodEntries[clanThatWasLeft] = new ClanGraceDetails
                    {
                        GracePeriodStartTimeUtc = DateTime.UtcNow,
                        LastMemberCharacterName = userWhoLeftCharacterName
                    };
                    LoggingHelper.Debug($"[OfflineGraceService] Clan {clanThatWasLeft} grace period set due to member departure. StartTimeUTC: {_gracePeriodEntries[clanThatWasLeft].GracePeriodStartTimeUtc}, TriggeredByDepartureOf: {userWhoLeftCharacterName.ToString()}. Duration: {OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value} mins.");
                }
                else
                {
                    LoggingHelper.Debug($"[OfflineGraceService] Grace period duration is 0 or negative. No grace period timer started for Clan: {clanThatWasLeft} after member departure.");
                }
            }
            else
            {
                LoggingHelper.Debug($"[OfflineGraceService] Clan {clanThatWasLeft} still has members online after {userWhoLeftCharacterName.ToString()} departed. No grace period started.");
            }
        }

        public static bool IsBaseVulnerableDueToGracePeriodOrBreach(EntityManager entityManager, Entity castleHeartEntity)
        {
            // Grace period config value (float)
            float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;

            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                LoggingHelper.Debug("[OfflineGraceService] Offline raid protection is disabled. Base is considered vulnerable from this service's perspective (grace periods effectively off).");
                return true; // Base is not protected by this mod if feature is off
            }

            LoggingHelper.Debug($"[OfflineGraceService] IsBaseVulnerable? CH: {castleHeartEntity}");
            if (!entityManager.Exists(castleHeartEntity))
            {
                LoggingHelper.Warning($"[OfflineGraceService] IsBaseVulnerable: CH {castleHeartEntity} does not exist.");
                return false; // Cannot determine vulnerability if CH doesn't exist, assume not vulnerable by grace
            }

            // Check if the Castle Heart is currently breached (IsSieged)
            if (entityManager.HasComponent<CastleHeart>(castleHeartEntity))
            {
                CastleHeart chComponent = entityManager.GetComponentData<CastleHeart>(castleHeartEntity);
                if (chComponent.IsSieged())
                {
                    LoggingHelper.Debug($"[OfflineGraceService] CH {castleHeartEntity} is IN BREACH (IsSieged=true). Base is vulnerable.");
                    return true; // Base is vulnerable if already breached
                }
            }

            // If grace period is zero or negative, then any offline status (not voided by login) means protection is active,
            // and it's not "vulnerable due to grace period".
            // The vulnerability here is about the *grace period itself* making it vulnerable.
            if (configuredGraceMinutes <= 0)
            {
                LoggingHelper.Debug($"[OfflineGraceService] Grace period duration ({configuredGraceMinutes} mins) is zero or negative. Base is not considered vulnerable due to an active grace period timer.");
                // If it's not breached, and grace is 0, it's not vulnerable *due to grace*.
                // The OfflineProtectionService.ShouldProtectBase will then just evaluate if owner is online/offline.
                return false;
            }


            if (!entityManager.HasComponent<UserOwner>(castleHeartEntity))
            {
                LoggingHelper.Warning($"[OfflineGraceService] IsBaseVulnerable: CH {castleHeartEntity} has no UserOwner component. Cannot check grace period.");
                return false; // Not vulnerable by grace if no owner to track
            }

            Entity ownerUserEntity = entityManager.GetComponentData<UserOwner>(castleHeartEntity).Owner._Entity;
            if (!entityManager.Exists(ownerUserEntity) || !entityManager.HasComponent<User>(ownerUserEntity))
            {
                LoggingHelper.Warning($"[OfflineGraceService] IsBaseVulnerable: CH {castleHeartEntity} owner UserEntity {ownerUserEntity} invalid or no User comp.");
                return false;
            }

            User ownerData = entityManager.GetComponentData<User>(ownerUserEntity);
            Entity clanEntity = ownerData.ClanEntity._Entity;
            Entity keyForGraceCheck = entityManager.Exists(clanEntity) && entityManager.HasComponent<ClanTeam>(clanEntity) ? clanEntity : ownerUserEntity;

            LoggingHelper.Debug($"[OfflineGraceService] IsBaseVulnerable: CH {castleHeartEntity}, Owner: {ownerData.CharacterName.ToString()} ({ownerUserEntity}), KeyForGrace: {keyForGraceCheck} (IsActuallyClanKey: {keyForGraceCheck == clanEntity && entityManager.Exists(clanEntity)})");

            if (_gracePeriodEntries.TryGetValue(keyForGraceCheck, out ClanGraceDetails details))
            {
                LoggingHelper.Debug($"[OfflineGraceService] Found grace details for key {keyForGraceCheck}. StartTimeUTC: {details.GracePeriodStartTimeUtc}");

                bool voidGrace = false;
                if (entityManager.Exists(clanEntity) && keyForGraceCheck == clanEntity && entityManager.HasComponent<ClanTeam>(clanEntity))
                {
                    if (UserHelper.IsAnyClanMemberOnline(entityManager, clanEntity))
                    {
                        voidGrace = true;
                        LoggingHelper.Info($"[OfflineGraceService] A member of Clan {clanEntity} is now online. Voiding grace for key {keyForGraceCheck}.");
                    }
                }
                else if (keyForGraceCheck == ownerUserEntity && entityManager.HasComponent<User>(ownerUserEntity))
                {
                    if (entityManager.GetComponentData<User>(ownerUserEntity).IsConnected)
                    {
                        LoggingHelper.Info($"[OfflineGraceService] Solo owner {ownerData.CharacterName.ToString()} for key {keyForGraceCheck} logged back in. Voiding grace.");
                        voidGrace = true;
                    }
                }

                if (voidGrace)
                {
                    _gracePeriodEntries.Remove(keyForGraceCheck);
                    LoggingHelper.Debug($"[OfflineGraceService] Grace voided for key {keyForGraceCheck}. Base is NOT vulnerable due to grace period.");
                    return false; // Grace period removed, so not vulnerable *due to grace*
                }

                // Using configuredGraceMinutes (float) cast to double for TimeSpan.FromMinutes
                TimeSpan timeSinceLogout = DateTime.UtcNow - details.GracePeriodStartTimeUtc;
                LoggingHelper.Debug($"[OfflineGraceService] Time since logout for key {keyForGraceCheck}: {timeSinceLogout.TotalMinutes:F2} mins. Configured grace period is {configuredGraceMinutes} mins.");

                if (timeSinceLogout.TotalMinutes < configuredGraceMinutes)
                {
                    double remainingMins = configuredGraceMinutes - timeSinceLogout.TotalMinutes;
                    LoggingHelper.Debug($"[OfflineGraceService] Key {keyForGraceCheck} (CH: {castleHeartEntity}) is WITHIN grace period ({remainingMins:F1} mins remaining, triggered by {details.LastMemberCharacterName.ToString()}). Base is vulnerable due to grace period.");
                    return true; // Within grace period, therefore vulnerable
                }
                else
                {
                    LoggingHelper.Info($"[OfflineGraceService] Grace period for key {keyForGraceCheck} has EXPIRED. Removing entry. Base no longer vulnerable due to this expired grace.");
                    _gracePeriodEntries.Remove(keyForGraceCheck);
                }
            }
            else
            {
                LoggingHelper.Debug($"[OfflineGraceService] No active grace details found for key {keyForGraceCheck}. Base is not vulnerable due to an active grace period timer.");
            }
            // If no active (or an expired) grace period applies, it's not vulnerable *due to the grace period itself*.
            return false;
        }

        public static bool GetClanGracePeriodInfo(EntityManager entityManager, Entity keyEntity,
                                                out TimeSpan remainingTime,
                                                out FixedString64Bytes lastLogoffName,
                                                out DateTime actualLogoutTime)
        {
            remainingTime = TimeSpan.Zero; lastLogoffName = default; actualLogoutTime = default;
            float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;

            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value || configuredGraceMinutes <= 0)
            {
                LoggingHelper.Debug("[OfflineGraceService] Offline raid protection is disabled or grace period is zero/negative. Grace period info not applicable.");
                return false;
            }
            // ... (rest of the method remains similar, just replace hardcoded GRACE_PERIOD_MINUTES with configuredGraceMinutes)
            // Example change within GetClanGracePeriodInfo:
            // if (elapsed.TotalMinutes < configuredGraceMinutes) // Use configuredGraceMinutes
            // {
            //     remainingTime = TimeSpan.FromMinutes(configuredGraceMinutes) - elapsed; // Use configuredGraceMinutes
            // ...
            // }
            // The existing logic for voiding grace if users log back in is good.
            // The rest of this method should be updated to use 'configuredGraceMinutes'
            // where GRACE_PERIOD_MINUTES was previously used.

            // Fully updated GetClanGracePeriodInfo:
            if (!entityManager.Exists(keyEntity))
            {
                LoggingHelper.Warning($"[OfflineGraceService] GetClanGracePeriodInfo: KeyEntity {keyEntity} does not exist.");
                return false;
            }
            LoggingHelper.Debug($"[OfflineGraceService] GetClanGracePeriodInfo called for key: {keyEntity}");

            if (_gracePeriodEntries.TryGetValue(keyEntity, out ClanGraceDetails details))
            {
                LoggingHelper.Debug($"[OfflineGraceService] Found grace details for key {keyEntity}. StartTimeUTC: {details.GracePeriodStartTimeUtc}");

                bool voidGrace = false;
                if (entityManager.HasComponent<ClanTeam>(keyEntity)) // It's a clan key
                {
                    if (UserHelper.IsAnyClanMemberOnline(entityManager, keyEntity))
                    {
                        voidGrace = true;
                        LoggingHelper.Info($"[OfflineGraceService] A member of Clan {keyEntity} is now online (for GetClanGracePeriodInfo). Voiding grace.");
                    }
                }
                else if (entityManager.HasComponent<User>(keyEntity)) // It's a solo user key
                {
                    if (entityManager.GetComponentData<User>(keyEntity).IsConnected)
                    {
                        LoggingHelper.Info($"[OfflineGraceService] Solo owner (key {keyEntity}) logged back in. Voiding grace for GetClanGracePeriodInfo.");
                        voidGrace = true;
                    }
                }

                if (voidGrace)
                {
                    _gracePeriodEntries.Remove(keyEntity);
                    return false; // Grace voided
                }

                TimeSpan elapsed = DateTime.UtcNow - details.GracePeriodStartTimeUtc;
                LoggingHelper.Debug($"[OfflineGraceService] Time since logout for key {keyEntity} (in GetClanGracePeriodInfo): {elapsed.TotalMinutes:F2} mins. Configured grace period is {configuredGraceMinutes} mins.");
                if (elapsed.TotalMinutes < configuredGraceMinutes)
                {
                    remainingTime = TimeSpan.FromMinutes((double)configuredGraceMinutes) - elapsed;
                    lastLogoffName = details.LastMemberCharacterName;
                    actualLogoutTime = details.GracePeriodStartTimeUtc;
                    LoggingHelper.Debug($"[OfflineGraceService] Active grace for key {keyEntity}. Remaining: {remainingTime.TotalSeconds:F0}s. Last logoff: {lastLogoffName.ToString()}.");
                    return true;
                }
                else
                {
                    LoggingHelper.Info($"[OfflineGraceService] Grace period for key {keyEntity} has EXPIRED. Removing entry (in GetClanGracePeriodInfo).");
                    _gracePeriodEntries.Remove(keyEntity);
                }
            }
            else
            {
                LoggingHelper.Debug($"[OfflineGraceService] No active grace details found for key {keyEntity} (in GetClanGracePeriodInfo).");
            }
            return false;
        }


        public static void CleanupOldGraceTimers()
        {
            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                if (_gracePeriodEntries.Count > 0)
                {
                    LoggingHelper.Info("[OfflineGraceService] Offline protection disabled, clearing any existing grace period entries.");
                    _gracePeriodEntries.Clear();
                }
                return;
            }

            float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;
            // If grace period is not positive, there should be no timers to clean up from this system.
            if (configuredGraceMinutes <= 0 && _gracePeriodEntries.Count > 0)
            {
                LoggingHelper.Info($"[OfflineGraceService] Grace period duration is {configuredGraceMinutes} mins. Clearing any stray grace entries as they shouldn't exist.");
                _gracePeriodEntries.Clear();
                return;
            }
            if (configuredGraceMinutes <=0) return; 

            var keysToRemove = new List<Entity>();
            double cleanupThresholdMinutes = (double)configuredGraceMinutes + 10.0;

            foreach (var kvp in _gracePeriodEntries)
            {
                if ((DateTime.UtcNow - kvp.Value.GracePeriodStartTimeUtc).TotalMinutes > cleanupThresholdMinutes)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            if (keysToRemove.Count > 0)
            {
                LoggingHelper.Info($"[OfflineGraceService] Cleaning up {keysToRemove.Count} old grace timers (older than {cleanupThresholdMinutes:F0} mins).");
                foreach (var key in keysToRemove)
                {
                    _gracePeriodEntries.Remove(key);
                }
            }
        }
    }
}