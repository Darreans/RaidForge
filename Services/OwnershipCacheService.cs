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
                _isHeartCachePopulatedFromInitialScan = false;
                return 0;
            }
            _heartToOwnerUserCache.Clear();
            _isHeartCachePopulatedFromInitialScan = false;

            EntityQueryDesc heartQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] { ComponentType.ReadOnly<CastleHeart>(), ComponentType.ReadOnly<UserOwner>() }
            };
            EntityQuery heartQuery = default;
            NativeArray<Entity> heartEntities = default;
            int heartsCached = 0;

            try
            {
                heartQuery = entityManager.CreateEntityQuery(heartQueryDesc);
                heartEntities = heartQuery.ToEntityArray(Allocator.TempJob);

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
                }
            }
            catch (Exception)
            {
                _isHeartCachePopulatedFromInitialScan = false;
                return heartsCached;
            }
            finally
            {
                if (heartEntities.IsCreated) heartEntities.Dispose();
                if (heartQuery != default) heartQuery.Dispose();
            }

            _isHeartCachePopulatedFromInitialScan = true;
            return heartsCached;
        }

        public static int InitializeUserToClanCache(EntityManager entityManager)
        {
            if (entityManager == default)
            {
                _isClanCachePopulatedFromInitialScan = false;
                return 0;
            }

            _userToClanCache.Clear();
            _isClanCachePopulatedFromInitialScan = false;

            EntityQuery userQuery = default;
            NativeArray<Entity> userEntities = default;
            int usersCached = 0;

            try
            {
                userQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>());
                userEntities = userQuery.ToEntityArray(Allocator.TempJob);

                foreach (Entity userEntity_iterator in userEntities)
                {
                    if (!entityManager.Exists(userEntity_iterator)) continue;
                    User userData = entityManager.GetComponentData<User>(userEntity_iterator);
                    _userToClanCache[userEntity_iterator] = userData.ClanEntity._Entity;
                    usersCached++;
                }
            }
            catch (Exception)
            {
                _isClanCachePopulatedFromInitialScan = false;
                return usersCached;
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                if (userQuery != default) userQuery.Dispose();
            }
            
            _isClanCachePopulatedFromInitialScan = true;
            return usersCached;
        }

        public static bool IsInitialScanAttemptedAndConsideredPopulated() => _isHeartCachePopulatedFromInitialScan && _isClanCachePopulatedFromInitialScan;

        public static bool UpdateUserClan(Entity userEntity, Entity clanEntity, EntityManager em)
        {
            if (em == default || userEntity == Entity.Null || !em.Exists(userEntity))
            {
                return false;
            }
            
            if (clanEntity != Entity.Null && !em.Exists(clanEntity))
            {
                clanEntity = Entity.Null;
            }

            bool madeChange = false;
            bool hadPrevious = _userToClanCache.TryGetValue(userEntity, out var prevClanInCache);
            
            if (!hadPrevious)
            {
                _userToClanCache[userEntity] = clanEntity;
                madeChange = true;
            }
            else if (prevClanInCache != clanEntity)
            {
                _userToClanCache[userEntity] = clanEntity;
                madeChange = true;
            }
            else
            {
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
            if (em == default || heartEntity == Entity.Null || !em.Exists(heartEntity))
            {
                return;
            }

            if (ownerUserEntity != Entity.Null && !em.Exists(ownerUserEntity))
            {
                ownerUserEntity = Entity.Null;
            }
            
            if (ownerUserEntity == Entity.Null)
            {
                _heartToOwnerUserCache.Remove(heartEntity);
            }
            else
            {
                _heartToOwnerUserCache[heartEntity] = ownerUserEntity;
            }
        }

        public static void RemoveHeart(Entity heartEntity)
        {
            if (heartEntity == Entity.Null) return;
            _heartToOwnerUserCache.Remove(heartEntity);
        }

        public static void HandlePlayerConnected(Entity userEntity, EntityManager entityManager)
        {
            if (!IsInitialScanAttemptedAndConsideredPopulated())
            {
                return;
            }

            if (entityManager.Exists(userEntity) && entityManager.HasComponent<User>(userEntity))
            {
                User userData = entityManager.GetComponentData<User>(userEntity);
                UpdateUserClan(userEntity, userData.ClanEntity._Entity, entityManager);
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
        }
    }
}