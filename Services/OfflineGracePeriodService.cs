using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using ProjectM;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Utils;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Services
{
    public class PersistedOfflineStateEntry
    {
        public string PersistentKey { get; set; }
        public long OfflineStartTimeTicks { get; set; }
        public string ContextualName { get; set; }
    }

    public static class PersistentKeyHelper
    {
        public static string GetUserKey(ulong platformId)
        {
            return $"user_{platformId}";
        }

        public static string GetClanKey(EntityManager em, Entity clanEntity)
        {
            if (clanEntity == Entity.Null || !em.Exists(clanEntity))
            {
                return null;
            }

            if (em.HasComponent<NetworkId>(clanEntity))
            {
                NetworkId clanNetId = em.GetComponentData<NetworkId>(clanEntity);

                if (!clanNetId.Equals(default(NetworkId)))
                {
                    string clanNetIdString = clanNetId.ToString();
                    return $"clan_nid_{clanNetIdString}";
                }
            }

            return null;
        }
    }

    public static class OfflineGraceService
    {
        private static Dictionary<string, DateTime> _persistedOfflineStartTimes = new Dictionary<string, DateTime>();
        private static Dictionary<string, string> _persistedOfflineEntityNames = new Dictionary<string, string>();
        private static readonly string DataSubfolder = "Data";
        private static readonly string OfflineStatesSaveFileName = "RaidForge_OfflineStates.csv";

        private static bool _dataLoadedFromDisk = false;

        private static readonly object _saveLock = new object();

        private static string GetSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);

            return Path.Combine(dirPath, OfflineStatesSaveFileName);
        }

        public static void ResetRuntimeState()
        {
            lock (_saveLock)
            {
                _persistedOfflineStartTimes.Clear();
                _persistedOfflineEntityNames.Clear();
                _dataLoadedFromDisk = false;
            }
        }

        public static void ReloadOfflineStatesFromDisk(EntityManager entityManager) => LoadOfflineStatesFromDisk(entityManager, true);

        public static void LoadOfflineStatesFromDisk(EntityManager entityManager, bool forceReload = false)
        {
            if (_dataLoadedFromDisk && !forceReload)
            {
                return;
            }

            lock (_saveLock)
            {
                string filePath = GetSaveFilePath();

                if (_dataLoadedFromDisk && !forceReload)
                {
                    return;
                }

                _persistedOfflineStartTimes.Clear();
                _persistedOfflineEntityNames.Clear();

                if (!File.Exists(filePath))
                {
                    _dataLoadedFromDisk = true;
                    return;
                }

                try
                {
                    var lines = SharedFileIO.ReadAllLinesShared(filePath);

                    foreach (var line in lines.Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var parts = DelimitedText.ParseLine(line, ',');

                        if (parts.Length >= 3)
                        {
                            var key = parts[0].Trim();
                            var name = parts[2].Trim();

                            if (!string.IsNullOrEmpty(key) && long.TryParse(parts[1], out long ticks))
                            {
                                _persistedOfflineStartTimes[key] = new DateTime(ticks, DateTimeKind.Utc);
                                _persistedOfflineEntityNames[key] = name;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error("Failed to load offline states from CSV.", ex);
                }

                _dataLoadedFromDisk = true;
            }
        }

        private static void SaveTimedOfflineStatesToDiskInternal()
        {
            if (!Plugin.SystemsInitialized && !_dataLoadedFromDisk)
            {
                return;
            }

            lock (_saveLock)
            {
                string filePath = GetSaveFilePath();

                try
                {
                    var sb = new StringBuilder();

                    sb.AppendLine("PersistentKey,OfflineStartTimeTicks,ContextualName");

                    foreach (var kvp in _persistedOfflineStartTimes)
                    {
                        var key = kvp.Key;
                        var ticks = kvp.Value.Ticks;
                        var name = _persistedOfflineEntityNames.TryGetValue(key, out var n) ? n : "NameNotFoundOnSave";

                        sb.AppendLine($"{DelimitedText.Escape(key, ',')},{ticks},{DelimitedText.Escape(name, ',')}");
                    }

                    SharedFileIO.QueueWriteAllTextWithRetry(filePath, sb.ToString());
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error("Failed to save offline states to CSV.", ex);
                }
            }
        }

        public static void MarkAsOffline(string persistentKey, DateTime offlineTimeUtc, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey))
            {
                return;
            }

            SetOfflineStateInMemory(persistentKey, offlineTimeUtc, contextualName);

            SaveTimedOfflineStatesToDiskInternal();
        }

        public static void MarkAsOnline(string persistentKey, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey))
            {
                return;
            }

            bool removedTimed;

            lock (_saveLock)
            {
                removedTimed = _persistedOfflineStartTimes.Remove(persistentKey);
                _persistedOfflineEntityNames.Remove(persistentKey);
            }

            if (removedTimed)
            {
                SaveTimedOfflineStatesToDiskInternal();
            }
        }

        public static bool AdminForceRemoveProtection(string persistentKey)
        {
            if (string.IsNullOrEmpty(persistentKey))
            {
                return false;
            }

            bool removedTimed;

            lock (_saveLock)
            {
                removedTimed = _persistedOfflineStartTimes.Remove(persistentKey);
                _persistedOfflineEntityNames.Remove(persistentKey);
            }

            if (removedTimed)
            {
                SaveTimedOfflineStatesToDiskInternal();
                LoggingHelper.Info($"[Admin] Forced removal of ORP for key: {persistentKey}");

                return true;
            }

            return false;
        }

        private static void SetOfflineStateInMemory(string persistentKey, DateTime offlineTimeUtc, string contextualName)
        {
            lock (_saveLock)
            {
                _persistedOfflineStartTimes[persistentKey] = offlineTimeUtc;
                _persistedOfflineEntityNames[persistentKey] = contextualName;
            }
        }

        private static bool ClearOfflineStateInMemory(string persistentKey)
        {
            lock (_saveLock)
            {
                bool removedTimed = _persistedOfflineStartTimes.Remove(persistentKey);
                bool removedName = _persistedOfflineEntityNames.Remove(persistentKey);

                return removedTimed || removedName;
            }
        }

        public static bool TryGetOfflineStartTime(string persistentKey, out DateTime offlineStartTimeUtc)
        {
            offlineStartTimeUtc = default;

            if (string.IsNullOrEmpty(persistentKey))
            {
                return false;
            }

            if (!_dataLoadedFromDisk && Plugin.SystemsInitialized && VWorld.IsServerWorldReady())
            {
                LoadOfflineStatesFromDisk(VWorld.EntityManager);
            }

            lock (_saveLock)
            {
                return _persistedOfflineStartTimes.TryGetValue(persistentKey, out offlineStartTimeUtc);
            }
        }

        public static void HandleUserDisconnected(EntityManager entityManager, Entity disconnectedUserEntity)
        {
            if (!Plugin.SystemsInitialized)
            {
                return;
            }

            if (!(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false))
            {
                return;
            }

            if (disconnectedUserEntity == Entity.Null || !entityManager.Exists(disconnectedUserEntity))
            {
                return;
            }

            if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                entityManager,
                disconnectedUserEntity,
                out OwnerIdentity owner,
                out string ownerError))
            {
                LoggingHelper.Debug($"[OfflineGraceService] Failed to resolve disconnected owner: {ownerError}");
                return;
            }

            string charNameStr = owner.CharacterName;
            string ownerKey = !string.IsNullOrEmpty(owner.PersistentKey) ? owner.PersistentKey : owner.UserKey;
            string latchedByUserKey = owner.UserKey;
            string contextualName = owner.ContextualName;

            bool hasValidClanOwner =
                owner.IsClan &&
                !string.IsNullOrEmpty(owner.PersistentKey) &&
                owner.ClanEntity != Entity.Null &&
                entityManager.Exists(owner.ClanEntity);

            if (string.IsNullOrEmpty(ownerKey))
            {
                LoggingHelper.Debug($"[OfflineGraceService] Skipping disconnect handling for '{charNameStr}' because owner key could not be resolved.");
                return;
            }

            if (ShardConfig.DisableOrpForShardHolders.Value &&
                UserHelper.TryGetShardPrefabsForUserOrClan(entityManager, disconnectedUserEntity, out var foundShards, out string reason))
            {
                LoggingHelper.Debug($"[ShardVulnerability] {reason} | Flagging '{contextualName}' as vulnerable, latched by '{charNameStr}'.");

                int maxCopies = ShardConfig.MaxAllowedShardsPerType?.Value ?? 1;

                foreach (var shard in foundShards)
                {
                    ShardVulnerabilityService.SetVulnerable(
                        ownerKey,
                        shard.GuidHash,
                        latchedByUserKey,
                        contextualName,
                        maxCopies,
                        deferSave: true
                    );
                }

                ShardVulnerabilityService.Flush();

                return;
            }

            if (hasValidClanOwner)
            {
                ShardVulnerabilityService.ClearAllShardsForOwnerIfLatchedBy(ownerKey, latchedByUserKey);

                if (ShardVulnerabilityService.IsVulnerable(ownerKey))
                {
                    LoggingHelper.Debug($"[OfflineGraceService] Clan '{contextualName}' still has an active shard vulnerability from another member. Skipping ORP.");
                    return;
                }

                if (!UserHelper.IsAnyClanMemberOnline(entityManager, owner.ClanEntity, disconnectedUserEntity))
                {
                    LoggingHelper.Debug($"[OfflineGraceService] All members offline for Clan '{contextualName}'. Applying ORP.");
                    MarkAsOffline(ownerKey, DateTime.UtcNow, contextualName + $" (last online: {charNameStr})");
                }
                else
                {
                    LoggingHelper.Debug($"[OfflineGraceService] Clan '{contextualName}' still has members online. No ORP applied yet.");
                }

                return;
            }

            ShardVulnerabilityService.ClearAllForOwner(ownerKey);

            LoggingHelper.Debug($"[OfflineGraceService] Solo player '{charNameStr}' offline. Applying ORP.");
            MarkAsOffline(ownerKey, DateTime.UtcNow, charNameStr);
        }

        public static void HandleClanMemberDeparted(EntityManager entityManager, Entity userWhoLeft, Entity clanThatWasLeft, FixedString64Bytes userWhoLeftCharacterNameFs)
        {
            string userWhoLeftCharacterNameStr = userWhoLeftCharacterNameFs.ToString();

            if (!Plugin.SystemsInitialized)
            {
                return;
            }

            if (!(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false))
            {
                return;
            }

            if (clanThatWasLeft == Entity.Null || !entityManager.Exists(clanThatWasLeft) || !entityManager.HasComponent<ClanTeam>(clanThatWasLeft))
            {
                return;
            }

            string clanPersistentKey = PersistentKeyHelper.GetClanKey(entityManager, clanThatWasLeft);

            if (string.IsNullOrEmpty(clanPersistentKey))
            {
                return;
            }

            if (!UserHelper.IsAnyClanMemberOnline(entityManager, clanThatWasLeft))
            {
                string clanName = entityManager.GetComponentData<ClanTeam>(clanThatWasLeft).Name.ToString();
                MarkAsOffline(clanPersistentKey, DateTime.UtcNow, clanName + $" (after {userWhoLeftCharacterNameStr} departed)");
            }
        }

        public static void EstablishInitialGracePeriodsOnBoot(EntityManager entityManager)
        {
            if (!Plugin.SystemsInitialized ||
                !Plugin.ServerHasJustBooted ||
                !(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false))
            {
                return;
            }

            if (!_dataLoadedFromDisk)
            {
                LoadOfflineStatesFromDisk(entityManager);
            }

            DateTime bootGraceStartUtc = DateTime.UtcNow;
            var processedOwnerKeys = new HashSet<string>();
            int restampedOwners = 0;
            int initializedUnknownOfflineOwners = 0;
            int preservedOfflineOwners = 0;
            int clearedOnlineOwners = 0;
            int skippedShardVulnerableOwners = 0;
            bool changed = false;
            HashSet<string> verifiedConnectedOwnersAtLastSave = PlayerRegistryService.GetVerifiedConnectedOwnerKeysAtLastSave(entityManager);

            foreach (var entry in OwnershipCacheService.GetHeartToOwnerCacheView().ToList())
            {
                Entity heartEntity = entry.Key;
                Entity ownerUserEntity = entry.Value;

                if (heartEntity == Entity.Null ||
                    ownerUserEntity == Entity.Null ||
                    !entityManager.Exists(heartEntity) ||
                    !entityManager.Exists(ownerUserEntity))
                {
                    continue;
                }

                if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                    entityManager,
                    ownerUserEntity,
                    out OwnerIdentity owner,
                    out string ownerError))
                {
                    LoggingHelper.Debug($"[OfflineGraceService] Boot ORP skipped owner resolution: {ownerError}");
                    continue;
                }

                if (string.IsNullOrEmpty(owner.PersistentKey) ||
                    !processedOwnerKeys.Add(owner.PersistentKey))
                {
                    continue;
                }

                if (ShardConfig.DisableOrpForShardHolders.Value &&
                    ShardVulnerabilityService.IsVulnerable(owner.PersistentKey))
                {
                    if (ClearOfflineStateInMemory(owner.PersistentKey))
                    {
                        changed = true;
                    }

                    skippedShardVulnerableOwners++;
                    continue;
                }

                bool allDefendersOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(heartEntity, entityManager);
                bool ownerWasConnectedAtLastSave = verifiedConnectedOwnersAtLastSave.Contains(owner.PersistentKey);
                bool hasExistingOfflineState = _persistedOfflineStartTimes.ContainsKey(owner.PersistentKey);

                if (allDefendersOffline)
                {
                    if (ownerWasConnectedAtLastSave)
                    {
                        SetOfflineStateInMemory(
                            owner.PersistentKey,
                            bootGraceStartUtc,
                            owner.ContextualName + " (server boot grace)"
                        );

                        PlayerRegistryService.MarkOwnerDisconnected(owner.PersistentKey);
                        restampedOwners++;
                        changed = true;
                    }
                    else if (!hasExistingOfflineState)
                    {
                        SetOfflineStateInMemory(
                            owner.PersistentKey,
                            bootGraceStartUtc,
                            owner.ContextualName + " (server boot initial grace)"
                        );

                        PlayerRegistryService.MarkOwnerDisconnected(owner.PersistentKey);
                        initializedUnknownOfflineOwners++;
                        changed = true;
                    }
                    else
                    {
                        preservedOfflineOwners++;
                    }

                    continue;
                }

                if (ClearOfflineStateInMemory(owner.PersistentKey))
                {
                    clearedOnlineOwners++;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveTimedOfflineStatesToDiskInternal();
            }

            LoggingHelper.Info($"[OfflineGraceService] Boot ORP grace refresh complete. Restamped {restampedOwners} previously connected owner(s), initialized {initializedUnknownOfflineOwners} unknown offline owner(s), preserved {preservedOfflineOwners} existing offline owner(s), cleared {clearedOnlineOwners} online owner(s), skipped {skippedShardVulnerableOwners} shard-vulnerable owner(s).");
        }
    }
}
