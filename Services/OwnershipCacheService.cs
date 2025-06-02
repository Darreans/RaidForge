using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using System;
using System.Collections.Generic;
using RaidForge.Utils;         
using RaidForge.Config;     
using ProjectM.CastleBuilding;

namespace RaidForge.Services
{
    public static class OwnershipCacheService
    {
        private static Dictionary<Entity, Entity> _heartToOwnerUserCache = new Dictionary<Entity, Entity>();
        private static bool _isHeartCachePopulatedFromInitialScan = false;

        private static Dictionary<Entity, Entity> _userToClanCache = new Dictionary<Entity, Entity>();
        private static bool _isClanCachePopulatedFromInitialScan = false;

        public static int InitializeHeartOwnershipCache(EntityManager entityManager)
        {
            if (entityManager == default)
            {
                LoggingHelper.Error("[OwnershipCacheService] EntityManager is null during InitializeHeartOwnershipCache. Heart cache NOT initialized.");
                _isHeartCachePopulatedFromInitialScan = false;
                return 0;
            }
            LoggingHelper.Info("[OwnershipCacheService] Initializing Heart Ownership Cache...");
            _heartToOwnerUserCache.Clear();
            _isHeartCachePopulatedFromInitialScan = false;

            EntityQueryDesc heartQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] { ComponentType.ReadOnly<CastleHeart>(), ComponentType.ReadOnly<UserOwner>() }
            };
            EntityQuery heartQuery = default; 
            NativeArray<Entity> heartEntities = default;
            int heartsCached = 0;
            int invalidOwnerHearts = 0;
            int totalHeartsQueried = 0;

            try
            {
                heartQuery = entityManager.CreateEntityQuery(heartQueryDesc); 
                heartEntities = heartQuery.ToEntityArray(Allocator.TempJob);
                totalHeartsQueried = heartEntities.Length;
                LoggingHelper.Debug($"[OwnershipCacheService] Initial scan found {totalHeartsQueried} entities with CastleHeart & UserOwner for heart cache.");

                foreach (Entity heartEntity in heartEntities)
                {
                    if (!entityManager.Exists(heartEntity)) continue;
                    UserOwner userOwner = entityManager.GetComponentData<UserOwner>(heartEntity);
                    Entity ownerEntity = userOwner.Owner._Entity;
                    if (ownerEntity != Entity.Null && entityManager.Exists(ownerEntity))
                    {
                        _heartToOwnerUserCache[heartEntity] = ownerEntity;
                        heartsCached++;
                    }
                    else
                    {
                        invalidOwnerHearts++;
                        LoggingHelper.Debug($"[OwnershipCacheService] Heart {heartEntity} has null or non-existent owner UserEntity {ownerEntity}. Not cached.");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[OwnershipCacheService] Exception during Heart Ownership Cache initialization.", ex);
                _isHeartCachePopulatedFromInitialScan = false;
                return heartsCached;
            }
            finally
            {
                if (heartEntities.IsCreated) heartEntities.Dispose();
                if (heartQuery != default) heartQuery.Dispose(); 
            }

            LoggingHelper.Info($"[OwnershipCacheService] Heart Ownership Cache init complete. Cached {heartsCached} hearts out of {totalHeartsQueried} queried. Invalid/Null owners found for: {invalidOwnerHearts} hearts.");
            _isHeartCachePopulatedFromInitialScan = true;
            return heartsCached;
        }

        public static int InitializeUserToClanCache(EntityManager entityManager)
        {
            if (entityManager == default)
            {
                LoggingHelper.Error("[OwnershipCacheService] EntityManager is null during InitializeUserToClanCache. Clan cache NOT initialized.");
                _isClanCachePopulatedFromInitialScan = false;
                return 0;
            }
            LoggingHelper.Info("[OwnershipCacheService] Initializing User to Clan Cache...");
            _userToClanCache.Clear();
            _isClanCachePopulatedFromInitialScan = false;

            EntityQuery userQuery = default; 
            NativeArray<Entity> userEntities = default;
            int usersCached = 0;
            int totalUsersQueried = 0;

            try
            {
                userQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>()); 
                userEntities = userQuery.ToEntityArray(Allocator.TempJob);
                totalUsersQueried = userEntities.Length;
                LoggingHelper.Debug($"[OwnershipCacheService] Found {totalUsersQueried} User entities for clan cache.");

                foreach (Entity userEntity_iterator in userEntities)
                {
                    if (!entityManager.Exists(userEntity_iterator)) continue;
                    User userData = entityManager.GetComponentData<User>(userEntity_iterator);
                    _userToClanCache[userEntity_iterator] = userData.ClanEntity._Entity;
                    usersCached++;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[OwnershipCacheService] Exception during User to Clan Cache initialization.", ex);
                _isClanCachePopulatedFromInitialScan = false;
                return usersCached;
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                if (userQuery != default) userQuery.Dispose(); 
            }

            LoggingHelper.Info($"[OwnershipCacheService] User to Clan Cache init complete. Cached {usersCached} users out of {totalUsersQueried} queried.");
            _isClanCachePopulatedFromInitialScan = true;
            return usersCached;
        }

        public static bool IsInitialScanAttemptedAndConsideredPopulated() => _isHeartCachePopulatedFromInitialScan && _isClanCachePopulatedFromInitialScan;

        public static bool UpdateUserClan(Entity userEntity, Entity clanEntity, EntityManager em)
        {
            if (em == default)
            {
                LoggingHelper.Warning($"[OwnershipCacheService] UpdateUserClan: EntityManager is default. Cannot update cache for User: {userEntity}.");
                return false;
            }
            if (userEntity == Entity.Null)
            {
                LoggingHelper.Warning($"[OwnershipCacheService] UpdateUserClan: Attempted to update clan for a Null UserEntity.");
                return false;
            }
            if (!em.Exists(userEntity))
            {
                LoggingHelper.Warning($"[OwnershipCacheService] UpdateUserClan: UserEntity {userEntity} does not exist. Cannot update cache.");
                return false;
            }

            if (clanEntity != Entity.Null && !em.Exists(clanEntity))
            {
                LoggingHelper.Warning($"[OwnershipCacheService] UpdateUserClan: Attempt to set non-existent Clan {clanEntity} for User {userEntity}. Caching as clanless (Null) instead.");
                clanEntity = Entity.Null;
            }

            bool madeChange = false;
            bool hadPrevious = _userToClanCache.TryGetValue(userEntity, out var prevClanInCache);

            string userCharacterName = "UnknownUser";
            if (em.HasComponent<User>(userEntity))
            {
                try
                {
                    userCharacterName = em.GetComponentData<User>(userEntity).CharacterName.ToString();
                }
                catch (Exception e)
                {
                    LoggingHelper.Debug($"[OwnershipCacheService] Could not get CharacterName for User {userEntity} in UpdateUserClan: {e.Message}");
                }
            }

            string newClanStr = (clanEntity == Entity.Null) ? "None (clanless)" : clanEntity.ToString();
            string prevClanStr = !hadPrevious ? "N/A (new to cache)" : ((prevClanInCache == Entity.Null) ? "None (clanless)" : prevClanInCache.ToString());

            if (!hadPrevious)
            {
                LoggingHelper.Info($"[OwnershipCacheService] ADDING User {userEntity} ('{userCharacterName}') to clan cache. New Clan: {newClanStr}.");
                _userToClanCache[userEntity] = clanEntity;
                madeChange = true;
            }
            else if (prevClanInCache != clanEntity)
            {
                LoggingHelper.Info($"[OwnershipCacheService] UPDATING Clan for User {userEntity} ('{userCharacterName}'). Previous Cached Clan: {prevClanStr}, New Clan: {newClanStr}.");
                _userToClanCache[userEntity] = clanEntity;
                madeChange = true;
            }
            else
            {
                if (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false)
                    LoggingHelper.Debug($"[OwnershipCacheService] User {userEntity} ('{userCharacterName}') clan data ({newClanStr}) is already up-to-date in cache. No change made.");
                madeChange = false;
            }
            return madeChange;
        }

        public static bool TryGetUserClan(Entity userEntity, out Entity clanEntity)
        {
            clanEntity = Entity.Null;
            if (userEntity == Entity.Null) return false;
            return _userToClanCache.TryGetValue(userEntity, out clanEntity);
        }

        public static bool TryGetHeartOwner(Entity heartEntity, out Entity ownerUserEntity)
        {
            ownerUserEntity = Entity.Null;
            if (heartEntity == Entity.Null) return false;
            return _heartToOwnerUserCache.TryGetValue(heartEntity, out ownerUserEntity);
        }

        public static void UpdateHeartOwner(Entity heartEntity, Entity ownerUserEntity, EntityManager em)
        {
          
            if (em == default) { LoggingHelper.Error("[OwnershipCacheService] EM null in UpdateHeartOwner."); return; }
            if (heartEntity == Entity.Null) { LoggingHelper.Warning($"[OwnershipCacheService] UpdateHeartOwner: HeartEntity is Null."); return; }
            if (!em.Exists(heartEntity)) { LoggingHelper.Warning($"[OwnershipCacheService] UpdateHeartOwner: Heart {heartEntity} does not exist."); return; }

            if (ownerUserEntity != Entity.Null && !em.Exists(ownerUserEntity))
            {
                LoggingHelper.Warning($"[OwnershipCacheService] UpdateHeartOwner: Attempting to set non-existent User {ownerUserEntity} as owner for Heart {heartEntity}. Setting owner to Null instead.");
                ownerUserEntity = Entity.Null;
            }

            bool hadPrevious = _heartToOwnerUserCache.TryGetValue(heartEntity, out var prevOwner);
            string heartOwnerName = "None";
            if (ownerUserEntity != Entity.Null && em.HasComponent<User>(ownerUserEntity))
            {
                try
                {
                    heartOwnerName = em.GetComponentData<User>(ownerUserEntity).CharacterName.ToString();
                }
                catch (Exception e)
                {
                    LoggingHelper.Debug($"[OwnershipCacheService] Could not get CharacterName for new owner {ownerUserEntity} of heart {heartEntity}: {e.Message}");
                }
            }

            if (ownerUserEntity == Entity.Null)
            {
                if (_heartToOwnerUserCache.Remove(heartEntity))
                    LoggingHelper.Info($"[OwnershipCacheService] Ownership REMOVED for Heart {heartEntity}. Previously owned by: {prevOwner}");
            }
            else
            {
                if (!hadPrevious)
                    LoggingHelper.Info($"[OwnershipCacheService] ADDING new Heart {heartEntity} to cache with Owner {ownerUserEntity} ('{heartOwnerName}').");
                else if (prevOwner != ownerUserEntity)
                    LoggingHelper.Info($"[OwnershipCacheService] UPDATING owner for Heart {heartEntity}. Previous: {prevOwner}, New: {ownerUserEntity} ('{heartOwnerName}').");
                else
                {
                    if (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false)
                        LoggingHelper.Debug($"[OwnershipCacheService] Heart {heartEntity} owner ({ownerUserEntity} ('{heartOwnerName}')) already up-to-date in cache.");
                    return;
                }
                _heartToOwnerUserCache[heartEntity] = ownerUserEntity;
            }
        }

        public static void RemoveHeart(Entity heartEntity)
        {
            if (heartEntity == Entity.Null) return;
            _heartToOwnerUserCache.TryGetValue(heartEntity, out Entity ownerUserEntity);
            if (_heartToOwnerUserCache.Remove(heartEntity))
            {
                LoggingHelper.Info($"[OwnershipCacheService] Removed Heart {heartEntity} (was owned by {ownerUserEntity}) from ownership cache.");
            }
            else
            {
                LoggingHelper.Debug($"[OwnershipCacheService] RemoveHeart: Heart {heartEntity} not found in cache, nothing to remove.");
            }
        }

        public static void HandlePlayerConnected(Entity userEntity, EntityManager entityManager)
        {
            if (!IsInitialScanAttemptedAndConsideredPopulated())
            {
                LoggingHelper.Debug($"[OwnershipCacheService] HandlePlayerConnected: Initial cache scan not yet complete. Deferring specific update for User {userEntity}. Global scan will handle it.");
                return;
            }

            if (entityManager.Exists(userEntity) && entityManager.HasComponent<User>(userEntity))
            {
                User userData = entityManager.GetComponentData<User>(userEntity);
                UpdateUserClan(userEntity, userData.ClanEntity._Entity, entityManager);
                LoggingHelper.Debug($"[OwnershipCacheService] Refreshed clan data for connected User {userEntity} ('{userData.CharacterName}').");
            }
            else
            {
                LoggingHelper.Warning($"[OwnershipCacheService] HandlePlayerConnected: UserEntity {userEntity} does not exist or lacks User component. Cannot refresh clan data.");
            }
        }

        public static IReadOnlyDictionary<Entity, Entity> GetHeartToOwnerCacheView() =>
            new Dictionary<Entity, Entity>(_heartToOwnerUserCache);

        public static IReadOnlyDictionary<Entity, Entity> GetUserToClanCacheView() =>
            new Dictionary<Entity, Entity>(_userToClanCache);

        public static void ClearAllCaches()
        {
            _heartToOwnerUserCache.Clear();
            _userToClanCache.Clear();
            _isHeartCachePopulatedFromInitialScan = false;
            _isClanCachePopulatedFromInitialScan = false;
            LoggingHelper.Info("[OwnershipCacheService] All ownership caches cleared and initialization flags reset.");
        }
    }
}