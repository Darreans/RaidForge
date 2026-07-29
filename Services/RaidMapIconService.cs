using System;
using System.Collections.Generic;
using System.Linq;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared;
using RaidForge.Config;
using RaidForge.Core;
using RaidForge.Data;
using RaidForge.Systems;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace RaidForge.Services
{
    public static class RaidMapIconService
    {
        private const double AttackStateUpdateGraceSeconds = 2.0;
        private const double PassiveIconFailureLogCooldownSeconds = 30.0;

        private enum RaidMapIconKind
        {
            OfflineRaid,
            DecayRaid,
            OptIn,
            OptOut
        }

        private readonly struct ActiveRaidIcon
        {
            public readonly Entity IconProxy;
            public readonly DateTime LastHit;
            public readonly RaidMapIconKind Kind;

            public ActiveRaidIcon(Entity iconProxy, DateTime lastHit, RaidMapIconKind kind)
            {
                IconProxy = iconProxy;
                LastHit = lastHit;
                Kind = kind;
            }
        }

        private static readonly Dictionary<Entity, ActiveRaidIcon> _activeRaidIcons = new();
        private static readonly HashSet<Entity> _activeIconProxies = new();
        private static readonly Dictionary<string, DateTime> _passiveIconFailureLogTimes = new();
        private static EntityQuery _mapIconAttachQuery;
        private static EntityManager _cachedMapIconAttachQueryEntityManager;
        private static bool _mapIconAttachQueryInitialized;
        private static bool _persistentStateIconsDirty = true;

        public static bool HasActiveIcons => _activeRaidIcons.Count > 0;
        public static bool IsRaidIconProxy(Entity iconProxy) => _activeIconProxies.Contains(iconProxy);

        public static void ResetRuntimeState()
        {
            RemoveAllIcons();
            _activeRaidIcons.Clear();
            _activeIconProxies.Clear();
            _passiveIconFailureLogTimes.Clear();
            _mapIconAttachQuery = default;
            _cachedMapIconAttachQueryEntityManager = default;
            _mapIconAttachQueryInitialized = false;
            _persistentStateIconsDirty = true;
        }

        public static void MarkPersistentStateIconsDirty()
        {
            _persistentStateIconsDirty = true;
        }

        public static void AddRaidIcon(Entity castleHeartEntity, bool isOfflineRaid, bool isDecayRaid)
        {
            if (isOfflineRaid)
            {
                AddRaidIconInternal(castleHeartEntity, RaidMapIconKind.OfflineRaid);
                return;
            }

            if (isDecayRaid)
            {
                AddRaidIconInternal(castleHeartEntity, RaidMapIconKind.DecayRaid);
            }
        }

        public static void ClearAllRaidForgeIconEntities(EntityManager em)
        {
            RemoveAllIcons();

            if (em == default)
            {
                return;
            }

            var knownIconGuidHashes = GetKnownRaidForgeIconGuidHashes();
            var parentsToDestroy = new HashSet<Entity>();
            var attachedIconsToDestroy = new HashSet<Entity>();
            EntityQuery query = default;
            NativeArray<Entity> entities = default;
            int standaloneIconsDestroyed = 0;

            try
            {
                bool hasProxyPrefab = TryGetMapIconProxyPrefabGuid(out PrefabGUID proxyPrefabGuid);

                query = GetMapIconAttachQuery(em);
                if (!query.IsEmptyIgnoreFilter && hasProxyPrefab)
                {
                    entities = query.ToEntityArray(Allocator.Temp);

                    foreach (var entity in entities)
                    {
                        if (!em.Exists(entity) || !em.HasComponent<Attach>(entity))
                        {
                            continue;
                        }

                        Entity parent = em.GetComponentData<Attach>(entity).Parent;
                        if (parent == Entity.Null ||
                            !em.Exists(parent) ||
                            !IsRaidForgeMapIconProxyEntity(em, parent, proxyPrefabGuid) ||
                            !IsKnownRaidForgeIconEntity(em, entity, knownIconGuidHashes))
                        {
                            continue;
                        }

                        attachedIconsToDestroy.Add(entity);
                        parentsToDestroy.Add(parent);
                    }
                }

                standaloneIconsDestroyed = DestroyStandaloneRaidForgeIconEntities(em, knownIconGuidHashes, Entity.Null, Entity.Null);
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }

            foreach (var entity in attachedIconsToDestroy)
            {
                DestroyEntityIfAlive(em, entity);
            }

            foreach (var parent in parentsToDestroy)
            {
                DestroyAttachedIconEntities(em, parent);
                DestroyEntityIfAlive(em, parent);
            }

            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true &&
                (attachedIconsToDestroy.Count > 0 || parentsToDestroy.Count > 0 || standaloneIconsDestroyed > 0))
            {
                LoggingHelper.Info($"[RaidMapIconService] Cleared {parentsToDestroy.Count} stale RaidForge map icon proxy/proxies and {standaloneIconsDestroyed} targeted icon(s).");
            }
        }

        private static void RemoveRaidIcon(Entity castleHeartEntity)
        {
            if (!_activeRaidIcons.TryGetValue(castleHeartEntity, out var iconData))
            {
                return;
            }

            if (VWorld.IsServerWorldReady() && VWorld.EntityManager.Exists(iconData.IconProxy))
            {
                var em = VWorld.EntityManager;

                DestroyAttachedIconEntities(em, iconData.IconProxy);
                DestroyEntityIfAlive(em, iconData.IconProxy);
            }

            _activeRaidIcons.Remove(castleHeartEntity);
            _activeIconProxies.Remove(iconData.IconProxy);
        }

        public static void RemoveOfflineRaidIconsForOwner(string ownerPersistentKey, EntityManager em)
        {
            if (string.IsNullOrEmpty(ownerPersistentKey) || _activeRaidIcons.Count == 0 || em == default)
            {
                return;
            }

            var heartsToRemove = new List<Entity>();

            foreach (var entry in _activeRaidIcons)
            {
                if (entry.Value.Kind != RaidMapIconKind.OfflineRaid)
                {
                    continue;
                }

                if (!TryGetOwnerPersistentKey(entry.Key, em, out string heartOwnerKey))
                {
                    continue;
                }

                if (heartOwnerKey == ownerPersistentKey)
                {
                    heartsToRemove.Add(entry.Key);
                }
            }

            foreach (var heart in heartsToRemove)
            {
                RemoveRaidIcon(heart);
            }

            if (_persistentStateIconsDirty)
            {
                try
                {
                    SyncPersistentStateIcons(em);
                }
                finally
                {
                    _persistentStateIconsDirty = false;
                }
            }
        }

        public static void RemoveDecayRaidIconsForReclaimedPlot(Entity newCastleHeartEntity, EntityManager em)
        {
            if (em == default ||
                newCastleHeartEntity == Entity.Null ||
                !em.Exists(newCastleHeartEntity) ||
                !em.HasComponent<CastleHeart>(newCastleHeartEntity) ||
                !TryGetMapIconPosition(em, newCastleHeartEntity, out float3 newHeartPosition))
            {
                return;
            }

            const float reclaimedPlotCleanupRadius = 18f;
            float cleanupRadiusSq = reclaimedPlotCleanupRadius * reclaimedPlotCleanupRadius;
            Entity newTerritoryEntity = em.GetComponentData<CastleHeart>(newCastleHeartEntity).CastleTerritoryEntity;
            var heartsToRemove = new List<Entity>();

            foreach (var entry in _activeRaidIcons.ToList())
            {
                if (entry.Value.Kind != RaidMapIconKind.DecayRaid)
                {
                    continue;
                }

                Entity trackedHeart = entry.Key;
                Entity iconProxy = entry.Value.IconProxy;
                bool shouldRemove = trackedHeart == newCastleHeartEntity;

                if (!shouldRemove && (trackedHeart == Entity.Null || !em.Exists(trackedHeart) || !em.HasComponent<CastleHeart>(trackedHeart)))
                {
                    shouldRemove = true;
                }

                if (!shouldRemove && newTerritoryEntity != Entity.Null)
                {
                    CastleHeart trackedCastleHeart = em.GetComponentData<CastleHeart>(trackedHeart);
                    shouldRemove = trackedCastleHeart.CastleTerritoryEntity == newTerritoryEntity;
                }

                if (!shouldRemove && TryGetMapIconPosition(em, trackedHeart, out float3 trackedHeartPosition))
                {
                    shouldRemove = math.distancesq(newHeartPosition, trackedHeartPosition) <= cleanupRadiusSq;
                }

                if (!shouldRemove && iconProxy != Entity.Null && em.Exists(iconProxy) && TryGetMapIconPosition(em, iconProxy, out float3 iconPosition))
                {
                    shouldRemove = math.distancesq(newHeartPosition, iconPosition) <= cleanupRadiusSq;
                }

                if (shouldRemove)
                {
                    heartsToRemove.Add(trackedHeart);
                }
            }

            foreach (Entity heart in heartsToRemove)
            {
                RemoveRaidIcon(heart);
            }

            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true && heartsToRemove.Count > 0)
            {
                LoggingHelper.Debug($"[RaidMapIconService] Removed {heartsToRemove.Count} decay raid icon(s) after castle heart placement reclaimed a plot.");
            }
        }

        public static void ProcessCleanup()
        {
            if (!VWorld.IsServerWorldReady()) return;

            var em = VWorld.EntityManager;

            if (!AnyIconKindEnabled())
            {
                RemoveAllIcons();
                return;
            }

            int timeoutSeconds = MapIconsConfig.RaidMapIconTimeoutSeconds.Value;
            DateTime nowUtc = DateTime.UtcNow;

            var heartsToCheck = _activeRaidIcons.Keys.ToList();
            foreach (var heart in heartsToCheck)
            {
                var iconData = _activeRaidIcons[heart];

                if (!em.Exists(heart) || !em.HasComponent<CastleHeart>(heart))
                {
                    RemoveRaidIcon(heart);
                    continue;
                }

                if (!IsIconKindEnabled(iconData.Kind))
                {
                    RemoveRaidIcon(heart);
                    continue;
                }

                CastleHeart castleHeart = em.GetComponentData<CastleHeart>(heart);
                double secondsSinceLastHit = (nowUtc - iconData.LastHit).TotalSeconds;

                if (iconData.Kind == RaidMapIconKind.OfflineRaid)
                {
                    if (!RaidToggleSystem.AreRaidsEnabled() ||
                        !OfflineProtectionService.AreAllDefendersActuallyOffline(heart, em) ||
                        (!castleHeart.IsAttacked() && secondsSinceLastHit > AttackStateUpdateGraceSeconds) ||
                        secondsSinceLastHit > timeoutSeconds)
                    {
                        RemoveRaidIcon(heart);
                    }

                    continue;
                }

                if (iconData.Kind == RaidMapIconKind.DecayRaid)
                {
                    if (!castleHeart.IsDecaying() ||
                        secondsSinceLastHit > timeoutSeconds)
                    {
                        RemoveRaidIcon(heart);
                    }

                    continue;
                }

                if (iconData.Kind == RaidMapIconKind.OptIn && !ShouldShowOptInIcon(heart, em))
                {
                    RemoveRaidIcon(heart);
                }

                if (iconData.Kind == RaidMapIconKind.OptOut && !ShouldShowOptOutIcon(heart, em))
                {
                    RemoveRaidIcon(heart);
                }
            }

            if (_persistentStateIconsDirty)
            {
                try
                {
                    SyncPersistentStateIcons(em);
                }
                finally
                {
                    _persistentStateIconsDirty = false;
                }
            }
        }

        public static void RemoveAllIcons()
        {
            var keys = _activeRaidIcons.Keys.ToList();
            foreach (var k in keys) RemoveRaidIcon(k);
        }

        private static void SyncPersistentStateIcons(EntityManager em)
        {
            if (!ShouldShowOptInIcons())
            {
                RemoveIconsOfKind(RaidMapIconKind.OptIn);
            }

            if (!ShouldShowOptOutIcons())
            {
                RemoveIconsOfKind(RaidMapIconKind.OptOut);
            }

            if (!ShouldShowOptInIcons() && !ShouldShowOptOutIcons())
            {
                return;
            }

            foreach (var entry in OwnershipCacheService.GetHeartToOwnerCacheView().ToList())
            {
                Entity heart = entry.Key;

                if (ShouldShowOptInIcon(heart, em))
                {
                    if (_activeRaidIcons.TryGetValue(heart, out var activeIcon) &&
                        GetIconPriority(activeIcon.Kind) > GetIconPriority(RaidMapIconKind.OptIn))
                    {
                        continue;
                    }

                    if (_activeRaidIcons.TryGetValue(heart, out activeIcon) &&
                        activeIcon.Kind == RaidMapIconKind.OptOut)
                    {
                        RemoveRaidIcon(heart);
                    }

                    AddRaidIconInternal(heart, RaidMapIconKind.OptIn);
                    continue;
                }

                if (ShouldShowOptOutIcon(heart, em))
                {
                    if (_activeRaidIcons.TryGetValue(heart, out var activeIcon) &&
                        GetIconPriority(activeIcon.Kind) > GetIconPriority(RaidMapIconKind.OptOut))
                    {
                        continue;
                    }

                    if (_activeRaidIcons.TryGetValue(heart, out activeIcon) &&
                        activeIcon.Kind == RaidMapIconKind.OptIn)
                    {
                        RemoveRaidIcon(heart);
                    }

                    AddRaidIconInternal(heart, RaidMapIconKind.OptOut);
                    continue;
                }

                if (_activeRaidIcons.TryGetValue(heart, out var existing) &&
                    (existing.Kind == RaidMapIconKind.OptIn ||
                        existing.Kind == RaidMapIconKind.OptOut))
                {
                    RemoveRaidIcon(heart);
                }
            }
        }

        private static void RemoveIconsOfKind(RaidMapIconKind kind)
        {
            foreach (var heart in _activeRaidIcons
                .Where(entry => entry.Value.Kind == kind)
                .Select(entry => entry.Key)
                .ToList())
            {
                RemoveRaidIcon(heart);
            }
        }

        private static void AddRaidIconInternal(Entity castleHeartEntity, RaidMapIconKind kind)
        {
            if (!VWorld.IsServerWorldReady()) return;

            var em = VWorld.EntityManager;

            if (!em.Exists(castleHeartEntity) ||
                (!em.HasComponent<Translation>(castleHeartEntity) && !em.HasComponent<LocalToWorld>(castleHeartEntity)))
            {
                LogPassiveIconFailure(kind, castleHeartEntity, "castle heart is missing Translation/LocalToWorld position data");
                return;
            }

            if (!IsIconKindEnabled(kind)) return;

            if (_activeRaidIcons.TryGetValue(castleHeartEntity, out var iconData) &&
                em.Exists(iconData.IconProxy))
            {
                if (iconData.Kind == kind)
                {
                    _activeRaidIcons[castleHeartEntity] = new ActiveRaidIcon(iconData.IconProxy, DateTime.UtcNow, iconData.Kind);
                    _activeIconProxies.Add(iconData.IconProxy);
                    return;
                }

                if (GetIconPriority(iconData.Kind) > GetIconPriority(kind))
                {
                    _activeIconProxies.Add(iconData.IconProxy);
                    return;
                }

                RemoveRaidIcon(castleHeartEntity);
            }
            else if (_activeRaidIcons.TryGetValue(castleHeartEntity, out iconData))
            {
                _activeRaidIcons.Remove(castleHeartEntity);
                _activeIconProxies.Remove(iconData.IconProxy);
            }

            RemoveStaleIconEntitiesForHeart(em, castleHeartEntity, Entity.Null);

            if (!TryGetIconPrefabGuid(kind, out PrefabGUID iconPrefab))
            {
                if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                {
                    LoggingHelper.Warning($"[RaidMapIconService] Could not resolve map icon prefab for {kind}. Icon was not spawned.");
                }

                return;
            }

            if (IsPassiveStateIconKind(kind))
            {
                if (TrySpawnTargetedMapIcon(em, castleHeartEntity, iconPrefab, kind, out Entity targetedMapIcon))
                {
                    _activeRaidIcons[castleHeartEntity] = new ActiveRaidIcon(targetedMapIcon, DateTime.UtcNow, kind);
                    _activeIconProxies.Add(targetedMapIcon);
                }
                else
                {
                    LogPassiveIconFailure(kind, castleHeartEntity, $"targeted icon spawn failed for prefab {iconPrefab.GuidHash}");
                }

                return;
            }

            var prefabCollection = VWorld.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
            if (!TryGetMapIconProxyPrefabGuid(out var proxyPrefabGUID)) return;
            if (!prefabCollection._PrefabGuidToEntityMap.TryGetValue(proxyPrefabGUID, out var proxyPrefabEntity)) return;

            var pos = em.GetComponentData<Translation>(castleHeartEntity).Value;
            var mapIconProxy = em.Instantiate(proxyPrefabEntity);

            em.SetComponentData(mapIconProxy, new Translation { Value = pos });
            em.AddComponentData(mapIconProxy, new SpawnedBy { Value = castleHeartEntity });

            if (em.HasComponent<SyncToUserBitMask>(mapIconProxy)) em.RemoveComponent<SyncToUserBitMask>(mapIconProxy);
            if (em.HasBuffer<SyncToUserBuffer>(mapIconProxy)) em.GetBuffer<SyncToUserBuffer>(mapIconProxy).Clear();
            if (em.HasComponent<OnlySyncToUsersTag>(mapIconProxy)) em.RemoveComponent<OnlySyncToUsersTag>(mapIconProxy);

            var attachBuffer = em.GetBuffer<AttachMapIconsToEntity>(mapIconProxy);
            attachBuffer.Clear();
            attachBuffer.Add(new AttachMapIconsToEntity { Prefab = iconPrefab });

            _activeRaidIcons[castleHeartEntity] = new ActiveRaidIcon(mapIconProxy, DateTime.UtcNow, kind);
            _activeIconProxies.Add(mapIconProxy);
        }

        private static bool TrySpawnTargetedMapIcon(EntityManager em, Entity castleHeartEntity, PrefabGUID iconPrefab, RaidMapIconKind kind, out Entity mapIconEntity)
        {
            mapIconEntity = Entity.Null;

            if (castleHeartEntity == Entity.Null ||
                !em.Exists(castleHeartEntity) ||
                !em.HasComponent<NetworkId>(castleHeartEntity))
            {
                LogPassiveIconFailure(kind, castleHeartEntity, "castle heart is missing NetworkId or no longer exists");
                return false;
            }

            var prefabCollection = VWorld.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
            if (prefabCollection == null)
            {
                LogPassiveIconFailure(kind, castleHeartEntity, "PrefabCollectionSystem is unavailable");
                return false;
            }

            if (!prefabCollection._PrefabGuidToEntityMap.TryGetValue(iconPrefab, out Entity iconPrefabEntity))
            {
                LogPassiveIconFailure(kind, castleHeartEntity, $"map icon prefab {iconPrefab.GuidHash} was not found in _PrefabGuidToEntityMap");
                return false;
            }

            if (!TryGetMapIconPosition(em, castleHeartEntity, out float3 pos))
            {
                LogPassiveIconFailure(kind, castleHeartEntity, "could not resolve castle heart position");
                return false;
            }

            mapIconEntity = em.Instantiate(iconPrefabEntity);

            ConfigureSpawnedMapIconTransformLikeModCore(em, mapIconEntity, pos);
            AddOrUpdateSpawnedBy(em, mapIconEntity, castleHeartEntity);

            Entity spawnedMapIcon = mapIconEntity;
            RaidForgeScheduler.RunAfterFrames(() =>
            {
                if (!VWorld.IsServerWorldReady())
                {
                    return;
                }

                var currentEm = VWorld.EntityManager;
                if (!ConfigureTargetedMapIconLikeModCore(currentEm, spawnedMapIcon, castleHeartEntity))
                {
                    LogPassiveIconFailure(kind, castleHeartEntity, $"failed to configure spawned icon entity {spawnedMapIcon.Index}:{spawnedMapIcon.Version}");
                    DestroyEntityIfAlive(currentEm, spawnedMapIcon);

                    if (_activeRaidIcons.TryGetValue(castleHeartEntity, out var activeIcon) &&
                        activeIcon.IconProxy == spawnedMapIcon)
                    {
                        _activeRaidIcons.Remove(castleHeartEntity);
                    }

                    _activeIconProxies.Remove(spawnedMapIcon);
                    return;
                }

                AttachMapIconAsChild(currentEm, spawnedMapIcon, castleHeartEntity, iconPrefab);
            }, 1);

            RaidForgeScheduler.RunAfterFrames(() =>
            {
                if (!VWorld.IsServerWorldReady())
                {
                    return;
                }

                var currentEm = VWorld.EntityManager;
                if (!ConfigureTargetedMapIconLikeModCore(currentEm, spawnedMapIcon, castleHeartEntity))
                {
                    return;
                }

                AttachMapIconAsChild(currentEm, spawnedMapIcon, castleHeartEntity, iconPrefab);
            }, 5);

            return true;
        }

        private static bool TryGetMapIconPosition(EntityManager em, Entity targetEntity, out float3 position)
        {
            position = default;

            if (em.HasComponent<LocalToWorld>(targetEntity))
            {
                position = em.GetComponentData<LocalToWorld>(targetEntity).Position;
                return true;
            }

            if (em.HasComponent<Translation>(targetEntity))
            {
                position = em.GetComponentData<Translation>(targetEntity).Value;
                return true;
            }

            return false;
        }

        private static void ConfigureSpawnedMapIconTransformLikeModCore(EntityManager em, Entity mapIconEntity, float3 position)
        {
            if (!em.HasComponent<CanFly>(mapIconEntity))
            {
                em.AddComponent<CanFly>(mapIconEntity);
            }

            if (em.HasComponent<Translation>(mapIconEntity))
            {
                em.SetComponentData(mapIconEntity, new Translation { Value = position });
            }
            else
            {
                em.AddComponentData(mapIconEntity, new Translation { Value = position });
            }

            if (em.HasComponent<LocalToWorld>(mapIconEntity))
            {
                var localToWorld = em.GetComponentData<LocalToWorld>(mapIconEntity);
                localToWorld.Value.c3 = new float4(position, 1f);
                em.SetComponentData(mapIconEntity, localToWorld);
            }

            if (em.HasComponent<Rotation>(mapIconEntity))
            {
                em.SetComponentData(mapIconEntity, new Rotation { Value = quaternion.identity });
            }
            else
            {
                em.AddComponentData(mapIconEntity, new Rotation { Value = quaternion.identity });
            }

            if (em.HasComponent<SpawnTransform>(mapIconEntity))
            {
                em.SetComponentData(mapIconEntity, new SpawnTransform
                {
                    Position = position,
                    Rotation = quaternion.identity
                });
            }

            if (em.HasComponent<LastTranslation>(mapIconEntity))
            {
                em.SetComponentData(mapIconEntity, new LastTranslation { Value = position });
            }
        }

        private static void AddOrUpdateSpawnedBy(EntityManager em, Entity mapIconEntity, Entity sourceEntity)
        {
            if (em.HasComponent<SpawnedBy>(mapIconEntity))
            {
                em.SetComponentData(mapIconEntity, new SpawnedBy { Value = sourceEntity });
            }
            else
            {
                em.AddComponentData(mapIconEntity, new SpawnedBy { Value = sourceEntity });
            }
        }

        private static bool ConfigureTargetedMapIconLikeModCore(EntityManager em, Entity mapIconEntity, Entity targetEntity)
        {
            if (em == default ||
                mapIconEntity == Entity.Null ||
                targetEntity == Entity.Null ||
                !em.Exists(mapIconEntity) ||
                !em.Exists(targetEntity) ||
                !em.HasComponent<NetworkId>(targetEntity))
            {
                return false;
            }

            if (!em.HasComponent<MapIconData>(mapIconEntity))
            {
                em.AddComponent<MapIconData>(mapIconEntity);
            }

            var mapIconData = em.GetComponentData<MapIconData>(mapIconEntity);
            mapIconData.RequiresReveal = false;
            mapIconData.ShowOutsideVision = true;
            mapIconData.ShowOnMinimap = true;
            mapIconData.AllySetting = MapIconShowSettings.Global;
            mapIconData.EnemySetting = MapIconShowSettings.Global;
            em.SetComponentData(mapIconEntity, mapIconData);

            if (!em.HasComponent<MapIconTargetEntity>(mapIconEntity))
            {
                em.AddComponent<MapIconTargetEntity>(mapIconEntity);
            }

            var mapIconTargetEntity = em.GetComponentData<MapIconTargetEntity>(mapIconEntity);
            mapIconTargetEntity.TargetEntity = NetworkedEntity.ServerEntity(targetEntity);
            mapIconTargetEntity.TargetNetworkId = em.GetComponentData<NetworkId>(targetEntity);
            em.SetComponentData(mapIconEntity, mapIconTargetEntity);

            return true;
        }

        private static void AttachMapIconAsChild(EntityManager em, Entity mapIconEntity, Entity parentEntity, PrefabGUID iconPrefab)
        {
            if (mapIconEntity == Entity.Null ||
                parentEntity == Entity.Null ||
                !em.Exists(mapIconEntity) ||
                !em.Exists(parentEntity))
            {
                return;
            }

            if (em.HasComponent<Attach>(mapIconEntity))
            {
                em.SetComponentData(mapIconEntity, new Attach(parentEntity));
            }
            else
            {
                em.AddComponentData(mapIconEntity, new Attach(parentEntity));
            }

            DynamicBuffer<AttachedBuffer> attachedBuffer = em.HasBuffer<AttachedBuffer>(parentEntity)
                ? em.GetBuffer<AttachedBuffer>(parentEntity)
                : em.AddBuffer<AttachedBuffer>(parentEntity);

            for (int i = 0; i < attachedBuffer.Length; i++)
            {
                if (attachedBuffer[i].Entity == mapIconEntity)
                {
                    AttachedBuffer existing = attachedBuffer[i];
                    existing.PrefabGuid = iconPrefab;
                    attachedBuffer[i] = existing;
                    return;
                }
            }

            attachedBuffer.Add(new AttachedBuffer
            {
                PrefabGuid = iconPrefab,
                Entity = mapIconEntity
            });
        }

        private static void LogPassiveIconFailure(RaidMapIconKind kind, Entity heart, string reason)
        {
            if (TroubleshootingConfig.EnableVerboseLogging?.Value != true)
            {
                return;
            }

            string key = $"{kind}:{heart.Index}:{heart.Version}:{reason}";
            DateTime nowUtc = DateTime.UtcNow;

            if (_passiveIconFailureLogTimes.TryGetValue(key, out DateTime lastLoggedUtc) &&
                (nowUtc - lastLoggedUtc).TotalSeconds < PassiveIconFailureLogCooldownSeconds)
            {
                return;
            }

            _passiveIconFailureLogTimes[key] = nowUtc;
            LoggingHelper.Debug($"[RaidMapIconService] Passive {kind} icon skipped for heart {heart.Index}:{heart.Version}: {reason}.");
        }

        private static bool ShouldShowOptInIcons()
        {
            return !OptInRaidingConfig.UseDefaultOptInMode &&
                MapIconsConfig.EnableOptInRaidMapIcon.Value &&
                OptInRaidingConfig.EnableOptInRaiding.Value &&
                !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value &&
                RaidToggleSystem.AreRaidsEnabled();
        }

        private static bool ShouldShowOptOutIcons()
        {
            return OptInRaidingConfig.UseDefaultOptInMode &&
                MapIconsConfig.EnableOptOutRaidMapIcon.Value &&
                OptInRaidingConfig.EnableOptInRaiding.Value &&
                !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value &&
                RaidToggleSystem.AreRaidsEnabled();
        }

        private static bool ShouldShowOptInIcon(Entity castleHeartEntity, EntityManager em)
        {
            if (!ShouldShowOptInIcons() ||
                castleHeartEntity == Entity.Null ||
                !em.Exists(castleHeartEntity) ||
                !em.HasComponent<CastleHeart>(castleHeartEntity))
            {
                return false;
            }

            if (!TryGetOwnerPersistentKey(castleHeartEntity, em, out string ownerPersistentKey))
            {
                return false;
            }

            return OptInRaidService.IsOptedIn(ownerPersistentKey);
        }

        private static bool ShouldShowOptOutIcon(Entity castleHeartEntity, EntityManager em)
        {
            if (!ShouldShowOptOutIcons() ||
                castleHeartEntity == Entity.Null ||
                !em.Exists(castleHeartEntity) ||
                !em.HasComponent<CastleHeart>(castleHeartEntity))
            {
                return false;
            }

            if (!TryGetOwnerPersistentKey(castleHeartEntity, em, out string ownerPersistentKey))
            {
                return false;
            }

            return OptInRaidService.IsManuallyOptedOut(ownerPersistentKey);
        }

        private static bool TryGetOwnerPersistentKey(Entity castleHeartEntity, EntityManager em, out string ownerPersistentKey)
        {
            ownerPersistentKey = null;

            if (!OwnershipCacheService.TryGetHeartOwner(castleHeartEntity, out Entity ownerUserEntity) ||
                ownerUserEntity == Entity.Null ||
                !em.Exists(ownerUserEntity) ||
                !em.HasComponent<User>(ownerUserEntity))
            {
                return false;
            }

            if (OwnershipCacheService.TryGetUserClan(ownerUserEntity, out Entity ownerClanEntity) &&
                ownerClanEntity != Entity.Null &&
                em.Exists(ownerClanEntity))
            {
                ownerPersistentKey = PersistentKeyHelper.GetClanKey(em, ownerClanEntity);
                return !string.IsNullOrEmpty(ownerPersistentKey);
            }

            User ownerUser = em.GetComponentData<User>(ownerUserEntity);
            ownerPersistentKey = PersistentKeyHelper.GetUserKey(ownerUser.PlatformId);
            return !string.IsNullOrEmpty(ownerPersistentKey);
        }

        private static bool AnyIconKindEnabled()
        {
            return MapIconsConfig.EnableOfflineRaidMapIcon.Value ||
                MapIconsConfig.EnableDecayRaidMapIcon.Value ||
                MapIconsConfig.EnableOptInRaidMapIcon.Value ||
                MapIconsConfig.EnableOptOutRaidMapIcon.Value;
        }

        private static bool IsIconKindEnabled(RaidMapIconKind kind)
        {
            return kind switch
            {
                RaidMapIconKind.OfflineRaid => MapIconsConfig.EnableOfflineRaidMapIcon.Value,
                RaidMapIconKind.DecayRaid => MapIconsConfig.EnableDecayRaidMapIcon.Value,
                RaidMapIconKind.OptIn => MapIconsConfig.EnableOptInRaidMapIcon.Value,
                RaidMapIconKind.OptOut => MapIconsConfig.EnableOptOutRaidMapIcon.Value,
                _ => false
            };
        }

        private static bool TryGetIconPrefabGuid(RaidMapIconKind kind, out PrefabGUID iconPrefabGuid)
        {
            iconPrefabGuid = default;

            if (IsPassiveStateIconKind(kind))
            {
                iconPrefabGuid = MapIconPrefabCatalog.PassiveRaidStateMapIcon.Guid;
                return true;
            }

            string configuredPrefab = kind switch
            {
                RaidMapIconKind.OfflineRaid => MapIconsConfig.OfflineRaidMapIconPrefab.Value,
                RaidMapIconKind.DecayRaid => MapIconsConfig.DecayRaidMapIconPrefab.Value,
                _ => MapIconPrefabCatalog.DefaultRaidForgeMapIcon.Name
            };

            iconPrefabGuid = PrefabGuidResolver.ResolveMapIconOrDefault(
                configuredPrefab,
                MapIconPrefabCatalog.DefaultRaidForgeMapIcon,
                $"{kind} map icon");
            return true;
        }

        private static int GetIconPriority(RaidMapIconKind kind)
        {
            return kind == RaidMapIconKind.OfflineRaid || kind == RaidMapIconKind.DecayRaid ? 2 : 1;
        }

        private static HashSet<int> GetKnownRaidForgeIconGuidHashes()
        {
            var hashes = new HashSet<int>
            {
                MapIconPrefabCatalog.DefaultRaidForgeMapIcon.GuidHash,
                MapIconPrefabCatalog.PassiveRaidStateMapIcon.GuidHash,
                MapIconPrefabCatalog.RecommendedTerritoryMapIcon.GuidHash,
                MapIconPrefabCatalog.HistoricalRelicSpawnBaseMapIcon.GuidHash,
                MapIconPrefabCatalog.HistoricalRelicBaseMapIcon.GuidHash,
                MapIconPrefabCatalog.HistoricalCastleHeartMapIcon.GuidHash,
            };

            if (TryGetPrefabGuidByName(MapIconPrefabCatalog.PassiveRaidStateMapIcon.Name, out PrefabGUID passiveStateIcon))
            {
                hashes.Add(passiveStateIcon.GuidHash);
            }

            if (MapIconsConfig.OfflineRaidMapIconPrefab != null &&
                PrefabGuidResolver.TryResolveMapIcon(MapIconsConfig.OfflineRaidMapIconPrefab.Value, out PrefabGUID offlineRaidIcon))
            {
                hashes.Add(offlineRaidIcon.GuidHash);
            }

            if (MapIconsConfig.DecayRaidMapIconPrefab != null &&
                PrefabGuidResolver.TryResolveMapIcon(MapIconsConfig.DecayRaidMapIconPrefab.Value, out PrefabGUID decayRaidIcon))
            {
                hashes.Add(decayRaidIcon.GuidHash);
            }

            return hashes;
        }

        private static bool IsPassiveStateIconKind(RaidMapIconKind kind)
        {
            return kind == RaidMapIconKind.OptIn ||
                kind == RaidMapIconKind.OptOut;
        }

        private static void RemoveStaleIconEntitiesForHeart(EntityManager em, Entity castleHeartEntity, Entity keepProxy)
        {
            if (em == default ||
                castleHeartEntity == Entity.Null ||
                !em.Exists(castleHeartEntity))
            {
                return;
            }

            bool hasProxyPrefab = TryGetMapIconProxyPrefabGuid(out PrefabGUID proxyPrefabGuid);
            var knownIconGuidHashes = GetKnownRaidForgeIconGuidHashes();
            var parentsToDestroy = new HashSet<Entity>();
            var attachedIconsToDestroy = new HashSet<Entity>();
            EntityQuery query = default;
            NativeArray<Entity> entities = default;

            try
            {
                query = GetMapIconAttachQuery(em);
                if (!query.IsEmptyIgnoreFilter && hasProxyPrefab)
                {
                    entities = query.ToEntityArray(Allocator.Temp);

                    foreach (var entity in entities)
                    {
                        if (!em.Exists(entity) ||
                            !em.HasComponent<Attach>(entity) ||
                            !IsKnownRaidForgeIconEntity(em, entity, knownIconGuidHashes))
                        {
                            continue;
                        }

                        Entity parent = em.GetComponentData<Attach>(entity).Parent;
                        if (parent == Entity.Null ||
                            parent == keepProxy ||
                            !em.Exists(parent) ||
                            !IsRaidForgeMapIconProxyEntity(em, parent, proxyPrefabGuid))
                        {
                            continue;
                        }

                        SpawnedBy spawnedBy = em.GetComponentData<SpawnedBy>(parent);
                        if (spawnedBy.Value != castleHeartEntity)
                        {
                            continue;
                        }

                        attachedIconsToDestroy.Add(entity);
                        parentsToDestroy.Add(parent);
                    }
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }

            foreach (var entity in attachedIconsToDestroy)
            {
                DestroyEntityIfAlive(em, entity);
            }

            foreach (var parent in parentsToDestroy)
            {
                DestroyAttachedIconEntities(em, parent);
                DestroyEntityIfAlive(em, parent);
                _activeIconProxies.Remove(parent);
            }

            DestroyStandaloneRaidForgeIconEntities(em, knownIconGuidHashes, castleHeartEntity, keepProxy);
        }

        private static int DestroyStandaloneRaidForgeIconEntities(
            EntityManager em,
            HashSet<int> knownIconGuidHashes,
            Entity heartToMatch,
            Entity keepIcon)
        {
            EntityQuery query = default;
            NativeArray<Entity> entities = default;
            var iconsToDestroy = new HashSet<Entity>();

            try
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<MapIconData>(), ComponentType.ReadOnly<SpawnedBy>());
                if (query.IsEmptyIgnoreFilter)
                {
                    return 0;
                }

                entities = query.ToEntityArray(Allocator.Temp);

                foreach (var entity in entities)
                {
                    if (entity == keepIcon ||
                        !em.Exists(entity) ||
                        em.HasComponent<Attach>(entity) ||
                        !IsKnownRaidForgeIconEntity(em, entity, knownIconGuidHashes))
                    {
                        continue;
                    }

                    SpawnedBy spawnedBy = em.GetComponentData<SpawnedBy>(entity);
                    if (spawnedBy.Value == Entity.Null ||
                        (heartToMatch != Entity.Null && spawnedBy.Value != heartToMatch))
                    {
                        continue;
                    }

                    iconsToDestroy.Add(entity);
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
                if (query != default) query.Dispose();
            }

            foreach (var icon in iconsToDestroy)
            {
                DestroyEntityIfAlive(em, icon);
                _activeIconProxies.Remove(icon);
            }

            return iconsToDestroy.Count;
        }

        private static bool TryGetMapIconProxyPrefabGuid(out PrefabGUID proxyPrefabGuid)
        {
            if (TryGetPrefabGuidByName(MapIconPrefabCatalog.MapIconProxyPrefab.Name, out proxyPrefabGuid))
            {
                return true;
            }

            proxyPrefabGuid = MapIconPrefabCatalog.MapIconProxyPrefab.Guid;
            return true;
        }

        private static bool TryGetPrefabGuidByName(string prefabName, out PrefabGUID prefabGuid)
        {
            return PrefabGuidResolver.TryGetPrefabGuidByName(prefabName, out prefabGuid);
        }

        private static bool IsRaidForgeMapIconProxyEntity(EntityManager em, Entity entity, PrefabGUID proxyPrefabGuid)
        {
            return em.Exists(entity) &&
                em.HasComponent<SpawnedBy>(entity) &&
                em.HasComponent<PrefabGUID>(entity) &&
                em.GetComponentData<PrefabGUID>(entity).GuidHash == proxyPrefabGuid.GuidHash;
        }

        private static bool IsKnownRaidForgeIconEntity(EntityManager em, Entity entity, HashSet<int> knownIconGuidHashes)
        {
            if (!em.Exists(entity) || !em.HasComponent<PrefabGUID>(entity))
            {
                return false;
            }

            PrefabGUID prefabGuid = em.GetComponentData<PrefabGUID>(entity);
            return knownIconGuidHashes.Contains(prefabGuid.GuidHash);
        }

        private static void DestroyEntityIfAlive(EntityManager em, Entity entity)
        {
            if (entity == Entity.Null || !em.Exists(entity))
            {
                return;
            }

            try
            {
                DestroyUtility.Destroy(em, entity, DestroyDebugReason.None);
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[RaidMapIconService] DestroyUtility failed for icon entity {entity.Index}: {ex.Message}. Falling back to EntityManager.DestroyEntity.");

                if (em.Exists(entity))
                {
                    em.DestroyEntity(entity);
                }
            }
        }

        private static void DestroyAttachedIconEntities(EntityManager em, Entity iconProxy)
        {
            if (iconProxy == Entity.Null || !em.Exists(iconProxy))
            {
                return;
            }

            if (em.HasBuffer<AttachedBuffer>(iconProxy))
            {
                var attached = em.GetBuffer<AttachedBuffer>(iconProxy);
                for (int i = 0; i < attached.Length; i++)
                {
                    DestroyEntityIfAlive(em, attached[i].Entity);
                }
            }

            EntityQuery query = default;
            NativeArray<Entity> entities = default;

            try
            {
                query = GetMapIconAttachQuery(em);
                if (query.IsEmptyIgnoreFilter)
                {
                    return;
                }

                entities = query.ToEntityArray(Allocator.Temp);

                foreach (var entity in entities)
                {
                    if (!em.Exists(entity) || !em.HasComponent<Attach>(entity))
                    {
                        continue;
                    }

                    if (em.GetComponentData<Attach>(entity).Parent == iconProxy)
                    {
                        DestroyEntityIfAlive(em, entity);
                    }
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }
        }

        private static EntityQuery GetMapIconAttachQuery(EntityManager em)
        {
            if (!_mapIconAttachQueryInitialized || _cachedMapIconAttachQueryEntityManager != em)
            {
                _mapIconAttachQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<MapIconData>(),
                    ComponentType.ReadOnly<Attach>());
                _cachedMapIconAttachQueryEntityManager = em;
                _mapIconAttachQueryInitialized = true;
            }

            return _mapIconAttachQuery;
        }
    }
}
