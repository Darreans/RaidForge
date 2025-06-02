using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using RaidForge.Config;
using RaidForge.Utils;
using BepInEx.Logging;
using BepInEx;

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
        private static HashSet<string> _defaultOrpAtBootKeys = new HashSet<string>();

        private static readonly string DataSubfolder = "Data";
        private static readonly string OfflineStatesSaveFileName = "RaidForge_OfflineStates.json";
        private static bool _dataLoadedFromDisk = false;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly object _saveLock = new object();

        private static string GetSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);
            return Path.Combine(dirPath, OfflineStatesSaveFileName);
        }

        public static void LoadOfflineStatesFromDisk(EntityManager entityManager)
        {
            if (_dataLoadedFromDisk) { return; }
            lock (_saveLock)
            {
                string filePath = GetSaveFilePath();
                _persistedOfflineStartTimes.Clear();
                _persistedOfflineEntityNames.Clear();
                _defaultOrpAtBootKeys.Clear();

                if (!File.Exists(filePath))
                {
                    _dataLoadedFromDisk = true;
                    return;
                }
                try
                {
                    string json = File.ReadAllText(filePath);
                    if (string.IsNullOrWhiteSpace(json)) { _dataLoadedFromDisk = true; return; }

                    var loadedEntries = JsonSerializer.Deserialize<List<PersistedOfflineStateEntry>>(json);
                    if (loadedEntries == null || !loadedEntries.Any()) { _dataLoadedFromDisk = true; return; }

                    foreach (var entry in loadedEntries)
                    {
                        if (!string.IsNullOrEmpty(entry.PersistentKey))
                        {
                            _persistedOfflineStartTimes[entry.PersistentKey] = new DateTime(entry.OfflineStartTimeTicks, DateTimeKind.Utc);
                            _persistedOfflineEntityNames[entry.PersistentKey] = entry.ContextualName ?? "UnknownNameFromSave";
                        }
                    }
                }
                catch (Exception) { }
                _dataLoadedFromDisk = true;
            }
        }

        private static void SaveTimedOfflineStatesToDiskInternal()
        {
            if (!Plugin.SystemsInitialized && !_dataLoadedFromDisk) { return; }
            lock (_saveLock)
            {
                string filePath = GetSaveFilePath();
                string dirPath = Path.GetDirectoryName(filePath);
                try
                {
                    Directory.CreateDirectory(dirPath);
                    var entriesToSave = new List<PersistedOfflineStateEntry>();
                    foreach (var kvp in _persistedOfflineStartTimes)
                    {
                        entriesToSave.Add(new PersistedOfflineStateEntry
                        {
                            PersistentKey = kvp.Key,
                            OfflineStartTimeTicks = kvp.Value.Ticks,
                            ContextualName = _persistedOfflineEntityNames.TryGetValue(kvp.Key, out var name) ? name : "NameNotFoundOnSave"
                        });
                    }
                    string json = JsonSerializer.Serialize(entriesToSave, _jsonOptions);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception) { }
            }
        }

        public static void Initialize()
        {
        }

        public static void MarkAsOffline(string persistentKey, DateTime offlineTimeUtc, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey))
            {
                return;
            }

            _persistedOfflineStartTimes[persistentKey] = offlineTimeUtc;
            _persistedOfflineEntityNames[persistentKey] = contextualName;
            _defaultOrpAtBootKeys.Remove(persistentKey);
            SaveTimedOfflineStatesToDiskInternal();
        }

        public static void MarkAsOnline(string persistentKey, string contextualName)
        {
            if (string.IsNullOrEmpty(persistentKey))
            {
                return;
            }

            bool removedTimed = _persistedOfflineStartTimes.Remove(persistentKey);
            _persistedOfflineEntityNames.Remove(persistentKey);
            bool removedDefault = _defaultOrpAtBootKeys.Remove(persistentKey);

            if (removedTimed || removedDefault)
            {
                if (removedTimed)
                {
                    SaveTimedOfflineStatesToDiskInternal();
                }
            }
        }

        public static bool TryGetOfflineStartTime(string persistentKey, out DateTime offlineStartTimeUtc)
        {
            offlineStartTimeUtc = default;
            if (string.IsNullOrEmpty(persistentKey)) return false;
            if (!_dataLoadedFromDisk && Plugin.SystemsInitialized && VWorld.IsServerWorldReady())
            {
                LoadOfflineStatesFromDisk(VWorld.EntityManager);
            }
            return _persistedOfflineStartTimes.TryGetValue(persistentKey, out offlineStartTimeUtc);
        }

        public static bool IsUnderDefaultBootORP(string persistentKey)
        {
            if (string.IsNullOrEmpty(persistentKey)) return false;
            return _defaultOrpAtBootKeys.Contains(persistentKey) && !_persistedOfflineStartTimes.ContainsKey(persistentKey);
        }

        public static void HandleUserDisconnected(EntityManager entityManager, Entity disconnectedUserEntity, bool isFromPersistenceLoadEvent_IGNORED)
        {
            if (!Plugin.SystemsInitialized) { return; }
            if (!(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false)) { return; }
            if (!entityManager.Exists(disconnectedUserEntity) || !entityManager.HasComponent<User>(disconnectedUserEntity)) { return; }

            User disconnectedUserData = entityManager.GetComponentData<User>(disconnectedUserEntity);
            FixedString64Bytes charNameFs = disconnectedUserData.CharacterName;
            string charNameStr = charNameFs.ToString();
            ulong platformId = disconnectedUserData.PlatformId;
            string userPersistentKey = PersistentKeyHelper.GetUserKey(platformId);

            Entity currentClanEntity = disconnectedUserData.ClanEntity._Entity;
            DateTime disconnectTime = DateTime.UtcNow;

            if (currentClanEntity != Entity.Null && entityManager.Exists(currentClanEntity) && entityManager.HasComponent<ClanTeam>(currentClanEntity))
            {
                string clanPersistentKey = PersistentKeyHelper.GetClanKey(entityManager, currentClanEntity);
                if (string.IsNullOrEmpty(clanPersistentKey))
                {
                    MarkAsOffline(userPersistentKey, disconnectTime, charNameStr);
                    return;
                }
                if (!UserHelper.IsAnyClanMemberOnline(entityManager, currentClanEntity, disconnectedUserEntity))
                {
                    string clanName = entityManager.GetComponentData<ClanTeam>(currentClanEntity).Name.ToString();
                    MarkAsOffline(clanPersistentKey, disconnectTime, clanName + $" (last online: {charNameStr})");
                }
            }
            else
            {
                MarkAsOffline(userPersistentKey, disconnectTime, charNameStr);
            }
        }

        public static void HandleClanMemberDeparted(EntityManager entityManager, Entity userWhoLeft, Entity clanThatWasLeft, FixedString64Bytes userWhoLeftCharacterNameFs)
        {
            string userWhoLeftCharacterNameStr = userWhoLeftCharacterNameFs.ToString();

            if (!Plugin.SystemsInitialized) { return; }
            if (!(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false)) { return; }

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
            if (!_dataLoadedFromDisk)
            {
                return;
            }

            if (!Plugin.SystemsInitialized)
            {
                return;
            }

            _defaultOrpAtBootKeys.Clear();

            EntityQuery userQuery = default;
            NativeArray<Entity> allUserEntities = default;
            int newDefaultOrpUserEntries = 0;

            EntityQuery clanQuery = default;
            NativeArray<Entity> allClanEntities = default;
            int newDefaultOrpClanEntries = 0;

            try
            {
                userQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>());
                allUserEntities = userQuery.ToEntityArray(Allocator.TempJob);

                foreach (Entity userEntityInLoop in allUserEntities)
                {
                    if (!entityManager.HasComponent<User>(userEntityInLoop)) continue;
                    User userData = entityManager.GetComponentData<User>(userEntityInLoop);
                    string userPersistentKey = PersistentKeyHelper.GetUserKey(userData.PlatformId);
                    string contextualUserName = userData.CharacterName.ToString();

                    if (!_persistedOfflineStartTimes.ContainsKey(userPersistentKey))
                    {
                        if (_defaultOrpAtBootKeys.Add(userPersistentKey))
                        {
                            _persistedOfflineEntityNames[userPersistentKey] = contextualUserName + " (Default Boot ORP - User)";
                            newDefaultOrpUserEntries++;
                        }
                    }
                }
            }
            catch (Exception) { }
            finally { if (allUserEntities.IsCreated) allUserEntities.Dispose(); }

            clanQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ClanTeam>());
            allClanEntities = clanQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                foreach (Entity clanEntityInLoop in allClanEntities)
                {
                    string clanPersistentKey = PersistentKeyHelper.GetClanKey(entityManager, clanEntityInLoop);
                    if (!string.IsNullOrEmpty(clanPersistentKey))
                    {
                        if (!_persistedOfflineStartTimes.ContainsKey(clanPersistentKey))
                        {
                            if (!UserHelper.IsAnyClanMemberOnline(entityManager, clanEntityInLoop))
                            {
                                if (_defaultOrpAtBootKeys.Add(clanPersistentKey))
                                {
                                    string clanName = entityManager.GetComponentData<ClanTeam>(clanEntityInLoop).Name.ToString();
                                    _persistedOfflineEntityNames[clanPersistentKey] = clanName + " (Default Boot ORP - Clan)";
                                    newDefaultOrpClanEntries++;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            finally { if (allClanEntities.IsCreated) allClanEntities.Dispose(); }
        }
    }
}