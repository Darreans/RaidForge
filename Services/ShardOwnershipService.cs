using System;
using System.Collections.Generic;
using System.Diagnostics;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Data;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Services
{
    // Main-thread-only live ownership snapshot. This is deliberately separate from
    // the persisted ORP vulnerability latch, which can outlive actual possession.
    public static class ShardOwnershipService
    {
        private static HashSet<string> _owners = new(StringComparer.Ordinal);
        private static long _nextScanAt;

        public static void ResetRuntimeState()
        {
            _owners.Clear();
            _nextScanAt = 0;
        }

        public static bool HasShard(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey)) return false;
            Refresh();
            return _owners.Contains(ownerKey);
        }

        public static void Refresh()
        {
            if (OptInRaidingConfig.EnableOptInRaiding?.Value != true ||
                OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value == true ||
                OptInRaidingConfig.AutoOptInShardHolders?.Value != true)
            {
                if (_owners.Count > 0) RaidMapIconService.MarkPersistentStateIconsDirty();
                ResetRuntimeState();
                return;
            }

            if (!Plugin.SystemsInitialized || !VWorld.IsServerWorldReady()) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextScanAt) return;
            _nextScanAt = now + Stopwatch.Frequency;

            try
            {
                var owners = ScanOwners(VWorld.EntityManager);
                if (!_owners.SetEquals(owners))
                {
                    _owners = owners;
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                }
            }
            catch (Exception ex)
            {
                // Keep the last complete snapshot on failure; never publish a partial scan.
                _nextScanAt = now + 10 * Stopwatch.Frequency;
                LoggingHelper.Error("[ShardOwnership] Could not refresh shard holders; retaining the previous snapshot and retrying in 10 seconds.", ex);
            }
        }

        private static HashSet<string> ScanOwners(EntityManager em)
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            var ownerKeysByUser = new Dictionary<Entity, string>();
            EntityQuery userQuery = default;
            EntityQuery connectionQuery = default;
            NativeArray<Entity> users = default;
            NativeArray<Entity> connections = default;
            try
            {
                userQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<User>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
                users = userQuery.ToEntityArray(Allocator.Temp);

                // Check every member, including offline users, before grouping by clan.
                foreach (var userEntity in users)
                {
                    if (!OwnerIdentityHelper.TryResolveFromUserEntity(em, userEntity, out var owner, out string error) ||
                        string.IsNullOrEmpty(owner.PersistentKey))
                    {
                        throw new InvalidOperationException($"Could not resolve shard owner: {error}");
                    }

                    ownerKeysByUser[userEntity] = owner.PersistentKey;
                    if (UserHelper.TryGetSoulShardsOnPerson(em, owner.UserData.LocalCharacter._Entity, out _, out _))
                    {
                        owners.Add(owner.PersistentKey);
                    }
                }

                // One pass over castle connections, rather than a full world scan per player.
                connectionQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<CastleHeartConnection>(), ComponentType.ReadOnly<PrefabGUID>(),
                    ComponentType.ReadOnly<AttachedBuffer>());
                connections = connectionQuery.ToEntityArray(Allocator.Temp);
                foreach (var connection in connections)
                {
                    if (!PrefabData.SoulShardPedestalPrefabGUIDs.Contains(em.GetComponentData<PrefabGUID>(connection))) continue;
                    var heart = em.GetComponentData<CastleHeartConnection>(connection).CastleHeartEntity._Entity;
                    if (!OwnershipCacheService.TryGetHeartOwner(heart, out var userEntity) ||
                        !ownerKeysByUser.TryGetValue(userEntity, out string ownerKey) || owners.Contains(ownerKey)) continue;

                    if (UserHelper.TryGetSoulShardsInPedestal(em, connection, out _)) owners.Add(ownerKey);
                }
            }
            finally
            {
                if (users.IsCreated) users.Dispose();
                if (connections.IsCreated) connections.Dispose();
                if (userQuery != default) userQuery.Dispose();
                if (connectionQuery != default) connectionQuery.Dispose();
            }

            return owners;
        }
    }
}
