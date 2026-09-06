using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Core;
using RaidForge.Data;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Services
{
    // Main-thread-only. Bootstrap once, then update only sources named by game events.
    // Never use the persisted ORP latch as evidence of current shard possession.
    public static class ShardOwnershipService
    {
        private readonly struct ShardSource
        {
            public readonly string OwnerKey;
            public readonly Entity User;
            public readonly Entity Heart;

            public ShardSource(string ownerKey, Entity user, Entity heart)
            {
                OwnerKey = ownerKey;
                User = user;
                Heart = heart;
            }
        }

        private static Dictionary<Entity, ShardSource> _sources = new();
        private static readonly Dictionary<string, int> _ownerSourceCounts = new(StringComparer.Ordinal);
        private static readonly HashSet<Entity> _pending = new();
        private static bool _flushQueued;
        private static int _generation;

        public static bool IsEnabled => Plugin.SystemsInitialized &&
            OptInRaidingConfig.EnableOptInRaiding?.Value == true &&
            OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value != true &&
            OptInRaidingConfig.AutoOptInShardHolders?.Value == true;

        public static void ResetRuntimeState()
        {
            _sources.Clear();
            _ownerSourceCounts.Clear();
            _pending.Clear();
            _flushQueued = false;
            _generation++;
        }

        // Damage, command and map lookups do not query the world or schedule work.
        public static bool HasShard(string ownerKey) => IsEnabled &&
            !string.IsNullOrEmpty(ownerKey) && _ownerSourceCounts.ContainsKey(ownerKey);

        public static void Rebuild()
        {
            if (!IsEnabled)
            {
                ResetRuntimeState();
                RaidMapIconService.MarkPersistentStateIconsDirty();
                return;
            }
            if (!VWorld.IsServerWorldReady()) return;

            EntityQuery userQuery = default;
            EntityQuery pedestalQuery = default;
            NativeArray<Entity> users = default;
            NativeArray<Entity> pedestals = default;
            try
            {
                var em = VWorld.EntityManager;
                var sources = new Dictionary<Entity, ShardSource>();
                userQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<User>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
                users = userQuery.ToEntityArray(Allocator.Temp);
                foreach (var user in users)
                {
                    if (TryReadSource(em, user, out var source)) sources[user] = source;
                }

                pedestalQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CastleHeartConnection>(),
                    ComponentType.ReadOnly<PrefabGUID>(), ComponentType.ReadOnly<AttachedBuffer>());
                pedestals = pedestalQuery.ToEntityArray(Allocator.Temp);
                foreach (var pedestal in pedestals)
                {
                    if (IsPedestal(em, pedestal) && TryReadSource(em, pedestal, out var source))
                        sources[pedestal] = source;
                }

                // Publish only a complete bootstrap; retain the previous state on read failure.
                ResetRuntimeState();
                _sources = sources;
                foreach (var source in sources.Values) AddOwner(source.OwnerKey);
                RaidMapIconService.MarkPersistentStateIconsDirty();
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[ShardOwnership] Bootstrap failed; retaining previous state. Use .raidrefreshcache to retry.", ex);
            }
            finally
            {
                if (users.IsCreated) users.Dispose();
                if (pedestals.IsCreated) pedestals.Dispose();
                if (userQuery != default) userQuery.Dispose();
                if (pedestalQuery != default) pedestalQuery.Dispose();
            }
        }

        public static void OnInventoryChanged(EntityManager em, InventoryChangedEvent change)
        {
            if (!IsEnabled || !IsShard(em, change.Item, change.ItemEntity)) return;
            QueueContainer(em, change.InventoryEntity);
        }

        public static void OnEquipmentChanged(EntityManager em, EquipmentChangedEvent change)
        {
            if (!IsEnabled || (change.EquipmentType != EquipmentType.MagicSource &&
                !IsShard(em, change.Item, change.ItemEntity))) return;
            QueueContainer(em, change.Target);
        }

        public static void OnUserChanged(Entity user)
        {
            if (!IsEnabled) return;
            QueueSource(user);
            // Only known shard-bearing sources belonging to this user.
            foreach (var pair in _sources)
                if (pair.Value.User == user) QueueSource(pair.Key);
        }

        public static void OnHeartChanged(Entity heart)
        {
            if (!IsEnabled) return;
            foreach (var pair in _sources)
                if (pair.Value.Heart == heart) QueueSource(pair.Key);
        }

        public static void OnDestroyed(EntityManager em, Entity entity)
        {
            if (!IsEnabled) return;
            if (_sources.ContainsKey(entity)) QueueSource(entity);
            if (!em.Exists(entity)) return;
            if (em.HasComponent<CastleHeart>(entity)) OnHeartChanged(entity);
            if (em.HasComponent<PlayerCharacter>(entity) || em.HasComponent<User>(entity) ||
                IsPedestal(em, entity) || IsShard(em, default, entity) ||
                em.HasComponent<InventoryConnection>(entity)) QueueContainer(em, entity);
        }

        private static bool IsShard(EntityManager em, PrefabGUID prefab, Entity item) =>
            PrefabData.SoulShardPrefabGUIDs.Contains(prefab) ||
            (em.Exists(item) && em.TryGetComponentData<PrefabGUID>(item, out var actual) &&
                PrefabData.SoulShardPrefabGUIDs.Contains(actual));

        private static bool IsPedestal(EntityManager em, Entity entity) =>
            em.Exists(entity) && em.TryGetComponentData<PrefabGUID>(entity, out var prefab) &&
            PrefabData.SoulShardPedestalPrefabGUIDs.Contains(prefab);

        private static void QueueContainer(EntityManager em, Entity entity)
        {
            // External inventories point to their owner; equipped items may use Attach/EntityOwner.
            // Bound traversal so malformed links cannot loop indefinitely.
            for (int depth = 0; depth < 8 && em.Exists(entity); depth++)
            {
                if (em.HasComponent<User>(entity) || IsPedestal(em, entity))
                {
                    QueueSource(entity);
                    return;
                }
                if (em.TryGetComponentData<PlayerCharacter>(entity, out var character))
                {
                    QueueSource(character.UserEntity);
                    return;
                }
                if (em.TryGetComponentData<InventoryConnection>(entity, out var connection) &&
                    connection.InventoryOwner != entity && em.Exists(connection.InventoryOwner))
                    entity = connection.InventoryOwner;
                else if (em.TryGetComponentData<InventoryItem>(entity, out var item) &&
                    item.ContainerEntity != entity && em.Exists(item.ContainerEntity))
                    entity = item.ContainerEntity;
                else if (em.TryGetComponentData<Attach>(entity, out var attach) &&
                    attach.Parent != entity && em.Exists(attach.Parent))
                    entity = attach.Parent;
                else if (em.TryGetComponentData<EntityOwner>(entity, out var owner) &&
                    owner.Owner != entity && em.Exists(owner.Owner))
                    entity = owner.Owner;
                else return;
            }
        }

        private static void QueueSource(Entity entity)
        {
            if (entity == Entity.Null) return;
            _pending.Add(entity);
            if (_flushQueued) return;
            _flushQueued = true;
            int generation = _generation;
            // Re-read after command buffers settle. Duplicate notifications share one job,
            // avoiding transient opt-outs during inventory <-> equipment moves.
            RaidForgeScheduler.RunAfterFrames(() =>
            {
                if (generation == _generation) ProcessPending();
            }, 2);
        }

        private static void ProcessPending()
        {
            _flushQueued = false;
            var pending = new List<Entity>(_pending);
            _pending.Clear();
            if (!IsEnabled || !VWorld.IsServerWorldReady()) return;
            var em = VWorld.EntityManager;
            foreach (var entity in pending)
            {
                try
                {
                    bool hasShard = TryReadSource(em, entity, out var current);
                    if (_sources.TryGetValue(entity, out var previous))
                    {
                        if (hasShard && previous.OwnerKey == current.OwnerKey &&
                            previous.User == current.User && previous.Heart == current.Heart) continue;
                        RemoveOwner(previous.OwnerKey);
                        _sources.Remove(entity);
                    }
                    if (hasShard)
                    {
                        _sources[entity] = current;
                        AddOwner(current.OwnerKey);
                    }
                }
                catch (Exception ex)
                {
                    // Keep this source's previous state; other queued sources still update.
                    LoggingHelper.Error("[ShardOwnership] Event update failed; use .raidrefreshcache if status is stale.", ex);
                }
            }
        }

        private static bool TryReadSource(EntityManager em, Entity entity, out ShardSource source)
        {
            source = default;
            if (!em.Exists(entity) || em.HasComponent<DestroyTag>(entity)) return false;
            Entity user;
            Entity heart = Entity.Null;
            if (em.TryGetComponentData<User>(entity, out var userData))
            {
                user = entity;
                var character = userData.LocalCharacter._Entity;
                if (!em.Exists(character) || em.HasComponent<DestroyTag>(character) ||
                    !UserHelper.TryGetSoulShardsOnPerson(em, character, out _, out _)) return false;
            }
            else
            {
                if (!IsPedestal(em, entity) ||
                    !em.TryGetComponentData<CastleHeartConnection>(entity, out var connection) ||
                    !UserHelper.TryGetSoulShardsInPedestal(em, entity, out _)) return false;
                heart = connection.CastleHeartEntity._Entity;
                if (!em.Exists(heart) || em.HasComponent<DestroyTag>(heart)) return false;
                if (em.TryGetComponentData<UserOwner>(heart, out var userOwner)) user = userOwner.Owner._Entity;
                else if (!OwnershipCacheService.TryGetHeartOwner(heart, out user)) return false;
            }

            if (!OwnerIdentityHelper.TryResolveFromUserEntity(em, user, out var owner, out var error) ||
                string.IsNullOrEmpty(owner.PersistentKey))
                throw new InvalidOperationException($"Could not resolve shard owner: {error}");
            source = new ShardSource(owner.PersistentKey, user, heart);
            return true;
        }

        private static void AddOwner(string key)
        {
            _ownerSourceCounts.TryGetValue(key, out int count);
            _ownerSourceCounts[key] = count + 1;
            if (count == 0) RaidMapIconService.MarkPersistentStateIconsDirty();
        }

        private static void RemoveOwner(string key)
        {
            if (!_ownerSourceCounts.TryGetValue(key, out int count)) return;
            if (count > 1) _ownerSourceCounts[key] = count - 1;
            else
            {
                _ownerSourceCounts.Remove(key);
                RaidMapIconService.MarkPersistentStateIconsDirty();
            }
        }
    }
}
