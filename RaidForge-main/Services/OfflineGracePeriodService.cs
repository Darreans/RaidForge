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
        private static HashSet<string> _defaultOrpAtBootKeys = new HashSet<string>();

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
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(',');
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
            if (!Plugin.SystemsInitialized && !_dataLoadedFromDisk) { return; }
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
                        sb.AppendLine($"{key},{ticks},{name}");
                    }

                    File.WriteAllText(filePath, sb.ToString());
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error("Failed to save offline states to CSV.", ex);
                }
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
            LoggingHelper.Debug("[ShardVulnerability] OfflineGraceService.HandleUserDisconnected has been called.");

            if (!Plugin.SystemsInitialized) { return; }
            if (!(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false)) { return; }
            if (!entityManager.Exists(disconnectedUserEntity) || !entityManager.TryGetComponentData<User>(disconnectedUserEntity, out var disconnectedUserData)) { return; }

            string charNameStr = disconnectedUserData.CharacterName.ToString();

            if (UserHelper.IsVulnerableDueToShard(entityManager, disconnectedUserEntity, out string reason))
            {
                string clanPersistentKey = null;
                string contextualName = charNameStr;
                Entity clanEntity = disconnectedUserData.ClanEntity._Entity;

                if (clanEntity.Exists() && entityManager.TryGetComponentData<ClanTeam>(clanEntity, out var clanTeam))
                {
                    clanPersistentKey = PersistentKeyHelper.GetClanKey(entityManager, clanEntity);
                    contextualName = clanTeam.Name.ToString();
                }

                var finalKey = clanPersistentKey ?? PersistentKeyHelper.GetUserKey(disconnectedUserData.PlatformId);
                var finalName = clanPersistentKey != null ? contextualName : charNameStr;

                LoggingHelper.Debug($"[ShardVulnerability] Vulnerability reason for '{finalName}': {reason}");
                ShardVulnerabilityService.SetVulnerable(finalKey, finalName);
                return;
            }
            else
            {
                LoggingHelper.Debug($"[ShardVulnerability] No shard found for {charNameStr}'s team. Proceeding with normal ORP.");

                Entity currentClanEntity = disconnectedUserData.ClanEntity._Entity;
                DateTime disconnectTime = DateTime.UtcNow;

                if (currentClanEntity.Exists() && entityManager.HasComponent<ClanTeam>(currentClanEntity))
                {
                    string clanPersistentKey = PersistentKeyHelper.GetClanKey(entityManager, currentClanEntity);
                    if (!UserHelper.IsAnyClanMemberOnline(entityManager, currentClanEntity, disconnectedUserEntity))
                    {
                        string clanName = entityManager.GetComponentData<ClanTeam>(currentClanEntity).Name.ToString();
                        MarkAsOffline(clanPersistentKey, disconnectTime, clanName + $" (last online: {charNameStr})");
                    }
                }
                else
                {
                    string userPersistentKey = PersistentKeyHelper.GetUserKey(disconnectedUserData.PlatformId);
                    MarkAsOffline(userPersistentKey, disconnectTime, charNameStr);
                }
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
                        }
                    }
                }
            }
            catch (Exception) { }
            finally { if (allUserEntities.IsCreated) allUserEntities.Dispose(); }

            EntityQuery clanQuery = default;
            NativeArray<Entity> allClanEntities = default;
            try
            {
                clanQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ClanTeam>());
                allClanEntities = clanQuery.ToEntityArray(Allocator.TempJob);
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