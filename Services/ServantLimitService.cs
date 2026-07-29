using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Services
{
    public static class ServantLimitService
    {
        private static readonly Dictionary<string, ConvertibleCharacter> _charactersBySourceName =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, ServantLimit> _limitsByServantHash = new();
        private static readonly Dictionary<Entity, PendingReservation> _pendingReservations = new();
        private static readonly Dictionary<Entity, DateTime> _nextRejectionNoticeUtc = new();
        private static readonly List<Entity> _expiredReservationCoffins = new();
        private static readonly List<Entity> _expiredRejectionNoticeUsers = new();
        private static readonly TimeSpan ReservationLifetime = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RejectionNoticeCooldown = TimeSpan.FromSeconds(5);

        private static bool _catalogInitialized;

        public static bool HasActiveLimits =>
            ServantLimitsConfig.EnableServantLimits?.Value == true &&
            _limitsByServantHash.Count > 0;

        public static void ResetRuntimeState()
        {
            _charactersBySourceName.Clear();
            _limitsByServantHash.Clear();
            _pendingReservations.Clear();
            _nextRejectionNoticeUtc.Clear();
            _expiredReservationCoffins.Clear();
            _expiredRejectionNoticeUsers.Clear();
            _catalogInitialized = false;
        }

        public static int ReloadConfiguredLimits(EntityManager entityManager)
        {
            _limitsByServantHash.Clear();
            _pendingReservations.Clear();
            _nextRejectionNoticeUtc.Clear();

            if (!_catalogInitialized &&
                !TryInitializeCharacterCatalog(entityManager))
            {
                LoggingHelper.Warning(
                    "[ServantLimits] Convertible character catalog is unavailable; no servant limits were loaded.");
                return 0;
            }

            if (ServantLimitsConfig.EnableServantLimits?.Value != true)
            {
                LoggingHelper.Info("[ServantLimits] Disabled.");
                return 0;
            }

            foreach (KeyValuePair<string, ConfigEntry<string>> configuredEntry
                in ServantLimitsConfig.CharacterLimitEntries)
            {
                string configuredMaximum = configuredEntry.Value?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(configuredMaximum))
                {
                    continue;
                }

                if (!int.TryParse(
                        configuredMaximum,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int maximumPerCastle) ||
                    maximumPerCastle < 0)
                {
                    LoggingHelper.Warning(
                        $"[ServantLimits] Ignoring {configuredEntry.Key}='{configuredMaximum}': " +
                        "the value must be blank or a non-negative integer.");
                    continue;
                }

                if (!_charactersBySourceName.TryGetValue(
                        configuredEntry.Key,
                        out ConvertibleCharacter character))
                {
                    LoggingHelper.Warning(
                        $"[ServantLimits] Ignoring unknown convertible character '{configuredEntry.Key}'.");
                    continue;
                }

                AddActiveLimit(character, maximumPerCastle);
            }

            LoggingHelper.Info(
                $"[ServantLimits] Loaded {_limitsByServantHash.Count} active limit(s) from " +
                $"{_charactersBySourceName.Count} convertible character prefab(s).");
            return _limitsByServantHash.Count;
        }

        public static bool TryAllowInsert(
            EntityManager entityManager,
            Entity coffinEntity,
            Entity userEntity,
            PrefabGUID servantPrefab,
            out int currentCount,
            out int limit,
            out string servantPrefabName)
        {
            currentCount = 0;
            limit = 0;
            servantPrefabName = null;

            if (ServantLimitsConfig.EnableServantLimits?.Value != true ||
                !_limitsByServantHash.TryGetValue(servantPrefab.GuidHash, out ServantLimit configuredLimit))
            {
                return true;
            }

            limit = configuredLimit.MaximumPerCastle;
            servantPrefabName = configuredLimit.ServantPrefabName;

            if (!TryGetConnectedCastleHeart(entityManager, coffinEntity, out Entity castleHeartEntity))
            {
                LoggingHelper.Warning(
                    $"[ServantLimits] Could not resolve a castle heart for coffin {DescribeEntity(coffinEntity)}; " +
                    $"the {servantPrefabName} insertion was blocked.");
                NotifyMissingCastleHeart(entityManager, userEntity);
                return false;
            }

            Trace(
                $"Evaluating user={DescribeEntity(userEntity)}, coffin={DescribeEntity(coffinEntity)}, " +
                $"heart={DescribeEntity(castleHeartEntity)}, servant={servantPrefabName} " +
                $"({servantPrefab.GuidHash}), maximum={limit}.");

            CleanupPendingReservations(entityManager);

            currentCount = CountExistingServants(
                entityManager,
                castleHeartEntity,
                servantPrefab.GuidHash,
                coffinEntity);
            currentCount += CountPendingReservations(
                castleHeartEntity,
                servantPrefab.GuidHash,
                coffinEntity);

            if (currentCount >= limit)
            {
                NotifyLimitReached(
                    entityManager,
                    userEntity,
                    servantPrefabName);

                Trace(
                    $"BLOCK servant={servantPrefabName}, heart={DescribeEntity(castleHeartEntity)}, " +
                    $"otherMatchingCoffins={currentCount}, maximum={limit}, " +
                    $"interactedCoffin={DescribeEntity(coffinEntity)}.");
                return false;
            }

            _pendingReservations[coffinEntity] = new PendingReservation(
                castleHeartEntity,
                servantPrefab.GuidHash,
                DateTime.UtcNow + ReservationLifetime);

            Trace(
                $"ALLOW servant={servantPrefabName}, heart={DescribeEntity(castleHeartEntity)}, " +
                $"otherMatchingCoffins={currentCount}, maximum={limit}; " +
                $"reserved coffin={DescribeEntity(coffinEntity)} for {ReservationLifetime.TotalSeconds:0.#}s.");
            return true;
        }

        public static void LogCountSnapshot(
            EntityManager entityManager,
            Entity coffinEntity,
            Entity userEntity,
            PrefabGUID servantPrefab,
            string action)
        {
            if (ServantLimitsConfig.EnableDetailedLogging?.Value != true)
            {
                return;
            }

            if (!_limitsByServantHash.TryGetValue(
                    servantPrefab.GuidHash,
                    out ServantLimit configuredLimit))
            {
                Trace(
                    $"ACTION action={action}, user={DescribeEntity(userEntity)}, " +
                    $"coffin={DescribeEntity(coffinEntity)}, targetHash={servantPrefab.GuidHash}; " +
                    "no active limit exists for this final servant prefab.");
                return;
            }

            if (!TryGetConnectedCastleHeart(
                    entityManager,
                    coffinEntity,
                    out Entity castleHeartEntity))
            {
                Trace(
                    $"ACTION action={action}, user={DescribeEntity(userEntity)}, " +
                    $"coffin={DescribeEntity(coffinEntity)}, servant={configuredLimit.ServantPrefabName}; " +
                    "the coffin has no valid castle-heart connection.");
                return;
            }

            CleanupPendingReservations(entityManager);

            int existingCount = CountExistingServants(
                entityManager,
                castleHeartEntity,
                servantPrefab.GuidHash,
                coffinEntity);
            int pendingCount = CountPendingReservations(
                castleHeartEntity,
                servantPrefab.GuidHash,
                coffinEntity);

            Trace(
                $"ACTION action={action}, user={DescribeEntity(userEntity)}, " +
                $"coffin={DescribeEntity(coffinEntity)}, heart={DescribeEntity(castleHeartEntity)}, " +
                $"servant={configuredLimit.ServantPrefabName} ({servantPrefab.GuidHash}), " +
                $"existing={existingCount}, pending={pendingCount}, total={existingCount + pendingCount}, " +
                $"maximum={configuredLimit.MaximumPerCastle}.");
        }

        private static bool TryInitializeCharacterCatalog(EntityManager entityManager)
        {
            PrefabCollectionSystem prefabCollection =
                VWorld.Server?.GetExistingSystemManaged<PrefabCollectionSystem>();

            if (entityManager == default ||
                prefabCollection == null ||
                !prefabCollection._PrefabGuidToEntityMap.IsCreated)
            {
                return false;
            }

            _charactersBySourceName.Clear();

            EntityQuery convertiblePrefabQuery = default;
            NativeArray<Entity> convertiblePrefabEntities = default;

            try
            {
                convertiblePrefabQuery = entityManager.CreateEntityQuery(
                    new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Prefab>(),
                            ComponentType.ReadOnly<PrefabGUID>(),
                            ComponentType.ReadOnly<ServantConvertable>()
                        },
                        Options = EntityQueryOptions.IncludePrefab |
                            EntityQueryOptions.IncludeDisabled
                    });

                convertiblePrefabEntities =
                    convertiblePrefabQuery.ToEntityArray(Allocator.Temp);

                foreach (Entity sourcePrefabEntity in convertiblePrefabEntities)
                {
                    if (!entityManager.TryGetComponentData(
                            sourcePrefabEntity,
                            out PrefabGUID sourcePrefabGuid) ||
                        !entityManager.TryGetComponentData(
                            sourcePrefabEntity,
                            out ServantConvertable convertable) ||
                        convertable.ConvertToUnit.GuidHash == 0)
                    {
                        continue;
                    }

                    if (!TryCreateConvertibleCharacter(
                            entityManager,
                            prefabCollection,
                            sourcePrefabGuid,
                            convertable.ConvertToUnit,
                            out ConvertibleCharacter character))
                    {
                        continue;
                    }

                    _charactersBySourceName[character.SourcePrefabName] = character;
                }
            }
            catch (Exception ex)
            {
                _charactersBySourceName.Clear();
                LoggingHelper.Warning(
                    "[ServantLimits] Failed while discovering convertible character prefabs.",
                    ex);
                return false;
            }
            finally
            {
                if (convertiblePrefabEntities.IsCreated)
                {
                    convertiblePrefabEntities.Dispose();
                }

                if (convertiblePrefabQuery != default)
                {
                    convertiblePrefabQuery.Dispose();
                }
            }

            var sortedCharacters =
                new List<KeyValuePair<string, string>>(_charactersBySourceName.Count);

            foreach (ConvertibleCharacter character in _charactersBySourceName.Values)
            {
                sortedCharacters.Add(
                    new KeyValuePair<string, string>(
                        character.SourcePrefabName,
                        character.ServantPrefabName));
            }

            sortedCharacters.Sort(
                (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

            ServantLimitsConfig.RegisterCharacterLimits(sortedCharacters);
            _catalogInitialized = true;

            LoggingHelper.Info(
                $"[ServantLimits] Generated {_charactersBySourceName.Count} regular CHAR_ to servant prefab entries.");

            Trace(
                $"Character catalog registration complete with {_charactersBySourceName.Count} convertible source prefab(s).");
            return true;
        }

        private static bool TryCreateConvertibleCharacter(
            EntityManager entityManager,
            PrefabCollectionSystem prefabCollection,
            PrefabGUID sourcePrefabGuid,
            PrefabGUID servantPrefabGuid,
            out ConvertibleCharacter character)
        {
            character = default;

            if (!PrefabGuidResolver.TryGetPrefabName(
                    sourcePrefabGuid,
                    out string sourcePrefabName) ||
                !sourcePrefabName.StartsWith("CHAR_", StringComparison.Ordinal) ||
                sourcePrefabName.EndsWith("_Servant", StringComparison.OrdinalIgnoreCase) ||
                !PrefabGuidResolver.TryGetPrefabName(
                    servantPrefabGuid,
                    out string servantPrefabName) ||
                !servantPrefabName.StartsWith("CHAR_", StringComparison.Ordinal) ||
                !servantPrefabName.EndsWith("_Servant", StringComparison.OrdinalIgnoreCase) ||
                !prefabCollection._PrefabGuidToEntityMap.TryGetValue(
                    servantPrefabGuid,
                    out Entity servantPrefabEntity) ||
                !entityManager.Exists(servantPrefabEntity) ||
                !entityManager.HasComponent<ServantData>(servantPrefabEntity))
            {
                return false;
            }

            character = new ConvertibleCharacter(
                sourcePrefabName,
                servantPrefabName,
                servantPrefabGuid);
            return true;
        }

        private static void AddActiveLimit(
            ConvertibleCharacter character,
            int maximumPerCastle)
        {
            if (_limitsByServantHash.TryGetValue(
                    character.ServantPrefabGuid.GuidHash,
                    out ServantLimit existingLimit))
            {
                int strictestMaximum =
                    Math.Min(existingLimit.MaximumPerCastle, maximumPerCastle);

                _limitsByServantHash[character.ServantPrefabGuid.GuidHash] =
                    new ServantLimit(
                        character.ServantPrefabName,
                        strictestMaximum);

                LoggingHelper.Warning(
                    $"[ServantLimits] Multiple regular characters resolve to {character.ServantPrefabName}; " +
                    $"using the strictest configured limit ({strictestMaximum}).");
                return;
            }

            _limitsByServantHash.Add(
                character.ServantPrefabGuid.GuidHash,
                new ServantLimit(
                    character.ServantPrefabName,
                    maximumPerCastle));

            LoggingHelper.Info(
                $"[ServantLimits] {character.SourcePrefabName} -> {character.ServantPrefabName} " +
                $"({character.ServantPrefabGuid.GuidHash}), maximum {maximumPerCastle} per castle.");

            Trace(
                $"Activated source={character.SourcePrefabName}, target={character.ServantPrefabName}, " +
                $"targetHash={character.ServantPrefabGuid.GuidHash}, maximum={maximumPerCastle}.");
        }

        private static bool TryGetConnectedCastleHeart(
            EntityManager entityManager,
            Entity coffinEntity,
            out Entity castleHeartEntity)
        {
            castleHeartEntity = Entity.Null;

            if (!entityManager.Exists(coffinEntity) ||
                !entityManager.TryGetComponentData(
                    coffinEntity,
                    out CastleHeartConnection connection))
            {
                return false;
            }

            Entity connectedHeart = connection.CastleHeartEntity._Entity;
            if (connectedHeart == Entity.Null ||
                !entityManager.Exists(connectedHeart) ||
                !entityManager.HasComponent<CastleHeart>(connectedHeart))
            {
                return false;
            }

            castleHeartEntity = connectedHeart;
            return true;
        }

        private static int CountExistingServants(
            EntityManager entityManager,
            Entity castleHeartEntity,
            int servantPrefabHash,
            Entity coffinToExclude)
        {
            EntityQuery query = default;
            NativeArray<Entity> coffins = default;
            int count = 0;

            try
            {
                query = entityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<ServantCoffinstation>(),
                        ComponentType.ReadOnly<CastleHeartConnection>()
                    },
                    Options = EntityQueryOptions.IncludeDisabled
                });

                if (query.IsEmptyIgnoreFilter)
                {
                    return 0;
                }

                coffins = query.ToEntityArray(Allocator.Temp);
                foreach (Entity coffin in coffins)
                {
                    if (coffin == coffinToExclude)
                    {
                        Trace(
                            $"Ignoring interacted coffin={DescribeEntity(coffin)} while counting existing servants.");
                        continue;
                    }

                    ServantCoffinstation station =
                        entityManager.GetComponentData<ServantCoffinstation>(coffin);

                    if (station.State == ServantCoffinState.Empty ||
                        station.ConvertToUnit.GuidHash != servantPrefabHash)
                    {
                        continue;
                    }

                    CastleHeartConnection connection =
                        entityManager.GetComponentData<CastleHeartConnection>(coffin);
                    Entity connectedHeart = connection.CastleHeartEntity._Entity;
                    bool matchesCastle = connectedHeart == castleHeartEntity;

                    Trace(
                        $"Matching occupied coffin={DescribeEntity(coffin)}, state={station.State}, " +
                        $"targetHash={station.ConvertToUnit.GuidHash}, heart={DescribeEntity(connectedHeart)}, " +
                        $"matchesInteractedHeart={matchesCastle}.");

                    if (matchesCastle)
                    {
                        count++;
                    }
                }
            }
            finally
            {
                if (coffins.IsCreated)
                {
                    coffins.Dispose();
                }

                if (query != default)
                {
                    query.Dispose();
                }
            }

            Trace(
                $"Existing count for heart={DescribeEntity(castleHeartEntity)}, " +
                $"targetHash={servantPrefabHash}, excluding={DescribeEntity(coffinToExclude)} is {count}.");
            return count;
        }

        private static void CleanupPendingReservations(EntityManager entityManager)
        {
            if (_pendingReservations.Count == 0)
            {
                return;
            }

            DateTime utcNow = DateTime.UtcNow;
            _expiredReservationCoffins.Clear();

            foreach (KeyValuePair<Entity, PendingReservation> pair in _pendingReservations)
            {
                bool expired = pair.Value.ExpiresUtc <= utcNow;
                bool missing = !entityManager.Exists(pair.Key);
                bool representedByCoffinState = false;
                bool remove = expired || missing;

                if (!remove &&
                    entityManager.TryGetComponentData(
                        pair.Key,
                        out ServantCoffinstation station) &&
                    station.State != ServantCoffinState.Empty)
                {
                    representedByCoffinState = true;
                    remove = true;
                }

                if (remove)
                {
                    Trace(
                        $"Removing reservation coffin={DescribeEntity(pair.Key)}, expired={expired}, " +
                        $"missing={missing}, representedByCoffinState={representedByCoffinState}.");
                    _expiredReservationCoffins.Add(pair.Key);
                }
            }

            foreach (Entity coffin in _expiredReservationCoffins)
            {
                _pendingReservations.Remove(coffin);
            }

            _expiredReservationCoffins.Clear();
        }

        private static int CountPendingReservations(
            Entity castleHeartEntity,
            int servantPrefabHash,
            Entity coffinToExclude)
        {
            int count = 0;

            foreach (KeyValuePair<Entity, PendingReservation> pair in _pendingReservations)
            {
                if (pair.Key == coffinToExclude)
                {
                    Trace(
                        $"Ignoring the interacted coffin's own pending reservation: {DescribeEntity(pair.Key)}.");
                    continue;
                }

                PendingReservation reservation = pair.Value;
                if (reservation.CastleHeartEntity == castleHeartEntity &&
                    reservation.ServantPrefabHash == servantPrefabHash)
                {
                    Trace(
                        $"Counting pending reservation coffin={DescribeEntity(pair.Key)}, " +
                        $"heart={DescribeEntity(reservation.CastleHeartEntity)}, " +
                        $"targetHash={reservation.ServantPrefabHash}, expiresUtc={reservation.ExpiresUtc:O}.");
                    count++;
                }
            }

            Trace(
                $"Pending count for heart={DescribeEntity(castleHeartEntity)}, " +
                $"targetHash={servantPrefabHash}, excluding={DescribeEntity(coffinToExclude)} is {count}.");
            return count;
        }

        private static void NotifyLimitReached(
            EntityManager entityManager,
            Entity userEntity,
            string servantPrefabName)
        {
            if (!entityManager.Exists(userEntity) ||
                !entityManager.TryGetComponentData(userEntity, out User user) ||
                !ShouldSendRejectionNotice(userEntity))
            {
                return;
            }

            string servantDisplayName = GetServantDisplayName(servantPrefabName);
            var message = new FixedString512Bytes(
                ChatColors.WarningText(
                    $"This castle has reached the maximum number of {servantDisplayName} servants."));
            ServerChatUtils.SendSystemMessageToClient(entityManager, user, ref message);
        }

        private static void NotifyMissingCastleHeart(
            EntityManager entityManager,
            Entity userEntity)
        {
            if (!entityManager.Exists(userEntity) ||
                !entityManager.TryGetComponentData(userEntity, out User user) ||
                !ShouldSendRejectionNotice(userEntity))
            {
                return;
            }

            var message = new FixedString512Bytes(
                ChatColors.WarningText(
                    "This servant cannot be converted because the coffin is not connected to a valid castle heart."));
            ServerChatUtils.SendSystemMessageToClient(entityManager, user, ref message);
        }

        private static bool ShouldSendRejectionNotice(Entity userEntity)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (_nextRejectionNoticeUtc.TryGetValue(
                    userEntity,
                    out DateTime nextNoticeUtc) &&
                nextNoticeUtc > utcNow)
            {
                return false;
            }

            _nextRejectionNoticeUtc[userEntity] =
                utcNow + RejectionNoticeCooldown;

            if (_nextRejectionNoticeUtc.Count <= 256)
            {
                return true;
            }

            _expiredRejectionNoticeUsers.Clear();
            foreach (KeyValuePair<Entity, DateTime> notice in _nextRejectionNoticeUtc)
            {
                if (notice.Value <= utcNow)
                {
                    _expiredRejectionNoticeUsers.Add(notice.Key);
                }
            }

            foreach (Entity expiredUser in _expiredRejectionNoticeUsers)
            {
                _nextRejectionNoticeUtc.Remove(expiredUser);
            }

            _expiredRejectionNoticeUsers.Clear();
            return true;
        }

        private static string GetServantDisplayName(string servantPrefabName)
        {
            if (string.IsNullOrWhiteSpace(servantPrefabName))
            {
                return "this type of";
            }

            string displayName = servantPrefabName;
            const string characterPrefix = "CHAR_";
            const string servantSuffix = "_Servant";

            if (displayName.StartsWith(
                    characterPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                displayName = displayName.Substring(characterPrefix.Length);
            }

            if (displayName.EndsWith(
                    servantSuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                displayName = displayName.Substring(
                    0,
                    displayName.Length - servantSuffix.Length);
            }

            return displayName.Replace('_', ' ');
        }

        private static string DescribeEntity(Entity entity)
        {
            return entity == Entity.Null
                ? "null"
                : $"{entity.Index}:{entity.Version}";
        }

        private static void Trace(string message)
        {
            if (ServantLimitsConfig.EnableDetailedLogging?.Value == true)
            {
                LoggingHelper.Info($"[ServantLimits:Trace] {message}");
            }
        }

        private readonly struct ConvertibleCharacter
        {
            public string SourcePrefabName { get; }
            public string ServantPrefabName { get; }
            public PrefabGUID ServantPrefabGuid { get; }

            public ConvertibleCharacter(
                string sourcePrefabName,
                string servantPrefabName,
                PrefabGUID servantPrefabGuid)
            {
                SourcePrefabName = sourcePrefabName;
                ServantPrefabName = servantPrefabName;
                ServantPrefabGuid = servantPrefabGuid;
            }
        }

        private readonly struct ServantLimit
        {
            public string ServantPrefabName { get; }
            public int MaximumPerCastle { get; }

            public ServantLimit(string servantPrefabName, int maximumPerCastle)
            {
                ServantPrefabName = servantPrefabName;
                MaximumPerCastle = maximumPerCastle;
            }
        }

        private readonly struct PendingReservation
        {
            public Entity CastleHeartEntity { get; }
            public int ServantPrefabHash { get; }
            public DateTime ExpiresUtc { get; }

            public PendingReservation(
                Entity castleHeartEntity,
                int servantPrefabHash,
                DateTime expiresUtc)
            {
                CastleHeartEntity = castleHeartEntity;
                ServantPrefabHash = servantPrefabHash;
                ExpiresUtc = expiresUtc;
            }
        }
    }
}
