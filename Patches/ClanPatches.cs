using System;
using System.Collections.Generic;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Clan;
using ProjectM.Network;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ClanSystem_Server), nameof(ClanSystem_Server.OnUpdate))]
    public static class ClanCacheSyncPatch
    {
        private static readonly Dictionary<Entity, Entity> _preClanSnapshot = new Dictionary<Entity, Entity>();

        public static void Prefix(ClanSystem_Server __instance)
        {
            if (!Plugin.SystemsInitialized) return;
            if (!HasRelevantClanEvents(__instance)) return;

            _preClanSnapshot.Clear();

            var em = __instance.EntityManager;
            EntityQuery query = default;
            NativeArray<Entity> userEntities = default;

            try
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
                if (query.IsEmptyIgnoreFilter) return;

                userEntities = query.ToEntityArray(Allocator.TempJob);

                foreach (var userEntity in userEntities)
                {
                    if (!em.Exists(userEntity)) continue;

                    var user = em.GetComponentData<User>(userEntity);
                    _preClanSnapshot[userEntity] = user.ClanEntity._Entity;
                }
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                if (query != default) query.Dispose();
            }
        }

        public static void Postfix(ClanSystem_Server __instance)
        {
            if (!Plugin.SystemsInitialized || _preClanSnapshot.Count == 0) return;

            var em = __instance.EntityManager;
            EntityQuery query = default;
            NativeArray<Entity> userEntities = default;

            try
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
                if (query.IsEmptyIgnoreFilter) return;

                userEntities = query.ToEntityArray(Allocator.TempJob);

                foreach (var userEntity in userEntities)
                {
                    if (!em.Exists(userEntity)) continue;

                    var user = em.GetComponentData<User>(userEntity);
                    var newClan = user.ClanEntity._Entity;

                    if (!_preClanSnapshot.TryGetValue(userEntity, out var oldClan))
                    {
                        continue;
                    }

                    if (oldClan == newClan)
                    {
                        continue;
                    }

                    LoggingHelper.Info($"[ClanCacheSync] Clan membership change detected for '{user.CharacterName}'. Updating cache.");
                    OwnershipCacheService.UpdateUserClan(userEntity, newClan, em);
                    PlayerRegistryService.UpsertUser(em, userEntity, user.IsConnected);
                }
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                if (query != default) query.Dispose();
                _preClanSnapshot.Clear();
            }
        }

        private static bool HasRelevantClanEvents(ClanSystem_Server clanSystem)
        {
            return !clanSystem._ClanInviteResponseQuery.IsEmptyIgnoreFilter ||
                !clanSystem._LeaveClanEventQuery.IsEmptyIgnoreFilter ||
                !clanSystem._KickRequestQuery.IsEmptyIgnoreFilter ||
                !clanSystem._CreateClanEventQuery.IsEmptyIgnoreFilter ||
                !clanSystem._EditClanEventQuery.IsEmptyIgnoreFilter;
        }
    }

    [HarmonyPatch(typeof(ClanSystem_Server), nameof(ClanSystem_Server.LeaveClan))]
    public static class ClanLeaveHookPatch
    {
        private static void Postfix(
            ClanSystem_Server __instance,
            EntityCommandBuffer commandBuffer,
            ModificationsRegistry modificationsRegistry,
            Entity clanEntity,
            Entity userToLeave,
            ClanSystem_Server.LeaveReason reason,
            CastleHeartLimitType castleHeartLimitType)
        {
            EntityManager entityManager = __instance.EntityManager;

            if (entityManager == default || userToLeave == Entity.Null)
            {
                return;
            }

            try
            {
                FixedString64Bytes userWhoLeftCharacterName = new FixedString64Bytes("Unknown (User Data N/A)");
                if (entityManager.Exists(userToLeave) && entityManager.HasComponent<User>(userToLeave))
                {
                    userWhoLeftCharacterName = entityManager.GetComponentData<User>(userToLeave).CharacterName;
                }

                if (entityManager.Exists(clanEntity))
                {
                    OfflineGraceService.HandleClanMemberDeparted(entityManager, userToLeave, clanEntity, userWhoLeftCharacterName);
                }

                OwnershipCacheService.UpdateUserClan(userToLeave, Entity.Null, entityManager);
                PlayerRegistryService.UpsertUser(entityManager, userToLeave);
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[ClanLeaveHook] Error while updating ORP and ownership cache after clan leave.", ex);
            }
        }
    }
}
