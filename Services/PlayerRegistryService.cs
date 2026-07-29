using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Utils;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Services
{
    public sealed class PlayerRegistryEntry
    {
        public string UserKey { get; set; }
        public ulong PlatformId { get; set; }
        public string CharacterName { get; set; }
        public string OwnerPersistentKey { get; set; }
        public string OwnerType { get; set; }
        public string ClanName { get; set; }
        public long LastSeenUtcTicks { get; set; }
        public bool IsConnected { get; set; }
    }

    public static class PlayerRegistryService
    {
        private static readonly object RegistryLock = new();
        private static readonly Dictionary<string, PlayerRegistryEntry> EntriesByUserKey = new();
        private static bool _loadedFromDisk = false;
        private static bool _dirty = false;
        private static DateTime _nextLockedRegistryWarningUtc = DateTime.MinValue;

        private const string DataSubfolder = "Data";
        private const string SaveFileName = "RaidForge_PlayerRegistry.csv";
        private const string PendingSaveFileName = "RaidForge_PlayerRegistry.pending.csv";

        private static string GetSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);
            return Path.Combine(dirPath, SaveFileName);
        }

        private static string GetPendingSaveFilePath()
        {
            string dirPath = Path.Combine(Paths.ConfigPath, "RaidForge", DataSubfolder);
            Directory.CreateDirectory(dirPath);
            return Path.Combine(dirPath, PendingSaveFileName);
        }

        public static void ResetRuntimeState()
        {
            lock (RegistryLock)
            {
                EntriesByUserKey.Clear();
                _loadedFromDisk = false;
                _dirty = false;
                _nextLockedRegistryWarningUtc = DateTime.MinValue;
            }
        }

        public static void RefreshFromWorld(EntityManager em)
        {
            if (em == default)
            {
                return;
            }

            LoadFromDisk();

            EntityQuery query = default;
            NativeArray<Entity> userEntities = default;
            int scanned = 0;
            int changed = 0;

            try
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
                if (query.IsEmptyIgnoreFilter)
                {
                    return;
                }

                userEntities = query.ToEntityArray(Allocator.Temp);

                lock (RegistryLock)
                {
                    foreach (Entity userEntity in userEntities)
                    {
                        scanned++;
                        if (TryBuildEntry(em, userEntity, out PlayerRegistryEntry entry))
                        {
                            if (UpsertEntryInMemory(entry))
                            {
                                changed++;
                            }
                        }
                    }
                }

                if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                {
                    LoggingHelper.Info($"[PlayerRegistry] World refresh scanned {scanned} user entr{(scanned == 1 ? "y" : "ies")}; changed {changed}.");
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[PlayerRegistry] Failed to refresh player registry from world.", ex);
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                if (query != default) query.Dispose();
            }
        }

        public static void UpsertUser(EntityManager em, Entity userEntity, bool? connectedOverride = null)
        {
            if (em == default || userEntity == Entity.Null || !em.Exists(userEntity))
            {
                return;
            }

            LoadFromDisk();

            if (!TryBuildEntry(em, userEntity, out PlayerRegistryEntry entry, connectedOverride))
            {
                return;
            }

            lock (RegistryLock)
            {
                UpsertEntryInMemory(entry);
            }
        }

        public static HashSet<string> GetVerifiedConnectedOwnerKeysAtLastSave(EntityManager em)
        {
            LoadFromDisk();
            var currentOwnerByUserKey = BuildCurrentOwnerByUserKey(em);
            var ownerKeys = new HashSet<string>();

            lock (RegistryLock)
            {
                foreach (PlayerRegistryEntry entry in EntriesByUserKey.Values)
                {
                    if (!entry.IsConnected ||
                        string.IsNullOrEmpty(entry.OwnerPersistentKey) ||
                        !currentOwnerByUserKey.TryGetValue(entry.UserKey, out string currentOwnerKey) ||
                        currentOwnerKey != entry.OwnerPersistentKey)
                    {
                        continue;
                    }

                    ownerKeys.Add(entry.OwnerPersistentKey);
                }
            }

            return ownerKeys;
        }

        public static bool TryGetEntryByUserKey(string userKey, out PlayerRegistryEntry entry)
        {
            entry = null;

            if (string.IsNullOrWhiteSpace(userKey))
            {
                return false;
            }

            LoadFromDisk();

            lock (RegistryLock)
            {
                if (!EntriesByUserKey.TryGetValue(userKey, out PlayerRegistryEntry storedEntry))
                {
                    return false;
                }

                entry = CloneEntry(storedEntry);
                return true;
            }
        }

        public static List<PlayerRegistryEntry> FindEntriesByCharacterName(string characterName)
        {
            LoadFromDisk();

            if (string.IsNullOrWhiteSpace(characterName))
            {
                return new List<PlayerRegistryEntry>();
            }

            string normalizedName = characterName.Trim();

            lock (RegistryLock)
            {
                var exactMatches = EntriesByUserKey.Values
                    .Where(entry => string.Equals(entry.CharacterName, normalizedName, StringComparison.OrdinalIgnoreCase))
                    .Select(CloneEntry)
                    .OrderBy(entry => entry.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (exactMatches.Count > 0)
                {
                    return exactMatches;
                }

                return EntriesByUserKey.Values
                    .Where(entry => (entry.CharacterName ?? string.Empty).IndexOf(normalizedName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(CloneEntry)
                    .OrderBy(entry => entry.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList();
            }
        }

        public static void MarkOwnerDisconnected(string ownerPersistentKey)
        {
            if (string.IsNullOrEmpty(ownerPersistentKey))
            {
                return;
            }

            LoadFromDisk();

            long nowTicks = DateTime.UtcNow.Ticks;

            lock (RegistryLock)
            {
                foreach (PlayerRegistryEntry entry in EntriesByUserKey.Values.Where(entry => entry.OwnerPersistentKey == ownerPersistentKey))
                {
                    if (entry.IsConnected)
                    {
                        entry.IsConnected = false;
                        entry.LastSeenUtcTicks = nowTicks;
                        _dirty = true;
                    }
                }
            }
        }

        private static bool TryBuildEntry(EntityManager em, Entity userEntity, out PlayerRegistryEntry entry, bool? connectedOverride = null)
        {
            entry = null;

            if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                em,
                userEntity,
                out OwnerIdentity owner,
                out _))
            {
                return false;
            }

            entry = new PlayerRegistryEntry
            {
                UserKey = owner.UserKey,
                PlatformId = owner.PlatformId,
                CharacterName = owner.CharacterName,
                OwnerPersistentKey = owner.PersistentKey,
                OwnerType = owner.IsClan ? "Clan" : "Player",
                ClanName = owner.ClanName ?? string.Empty,
                LastSeenUtcTicks = DateTime.UtcNow.Ticks,
                IsConnected = connectedOverride ?? owner.UserData.IsConnected
            };

            return !string.IsNullOrEmpty(entry.UserKey);
        }

        private static Dictionary<string, string> BuildCurrentOwnerByUserKey(EntityManager em)
        {
            var ownerByUserKey = new Dictionary<string, string>();

            EntityQuery query = default;
            NativeArray<Entity> userEntities = default;

            try
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
                if (query.IsEmptyIgnoreFilter)
                {
                    return ownerByUserKey;
                }

                userEntities = query.ToEntityArray(Allocator.Temp);

                foreach (Entity userEntity in userEntities)
                {
                    if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                    {
                        continue;
                    }

                    if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                        em,
                        userEntity,
                        out OwnerIdentity currentOwner,
                        out _))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(currentOwner.UserKey) &&
                        !string.IsNullOrEmpty(currentOwner.PersistentKey))
                    {
                        ownerByUserKey[currentOwner.UserKey] = currentOwner.PersistentKey;
                    }
                }
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                if (query != default) query.Dispose();
            }

            return ownerByUserKey;
        }

        public static void SaveIfDirty()
        {
            bool shouldSave;
            lock (RegistryLock)
            {
                shouldSave = _dirty;
            }

            if (shouldSave)
            {
                SaveToDisk();
            }
        }

        private static bool UpsertEntryInMemory(PlayerRegistryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.UserKey))
            {
                return false;
            }

            if (EntriesByUserKey.TryGetValue(entry.UserKey, out PlayerRegistryEntry existing) &&
                EntryMateriallyEquals(existing, entry))
            {
                return false;
            }

            EntriesByUserKey[entry.UserKey] = entry;
            _dirty = true;
            return true;
        }

        private static bool EntryMateriallyEquals(PlayerRegistryEntry left, PlayerRegistryEntry right)
        {
            return left.PlatformId == right.PlatformId &&
                left.CharacterName == right.CharacterName &&
                left.OwnerPersistentKey == right.OwnerPersistentKey &&
                left.OwnerType == right.OwnerType &&
                left.ClanName == right.ClanName &&
                left.IsConnected == right.IsConnected;
        }

        private static PlayerRegistryEntry CloneEntry(PlayerRegistryEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new PlayerRegistryEntry
            {
                UserKey = entry.UserKey,
                PlatformId = entry.PlatformId,
                CharacterName = entry.CharacterName,
                OwnerPersistentKey = entry.OwnerPersistentKey,
                OwnerType = entry.OwnerType,
                ClanName = entry.ClanName,
                LastSeenUtcTicks = entry.LastSeenUtcTicks,
                IsConnected = entry.IsConnected
            };
        }

        private static void LoadFromDisk()
        {
            if (_loadedFromDisk)
            {
                return;
            }

            string filePath = GetSaveFilePath();
            string pendingFilePath = GetPendingSaveFilePath();
            string loadFilePath = GetNewestExistingRegistryFile(filePath, pendingFilePath);

            if (string.IsNullOrEmpty(loadFilePath))
            {
                _loadedFromDisk = true;
                return;
            }

            try
            {
                string[] lines = SharedFileIO.ReadAllLinesShared(loadFilePath);

                lock (RegistryLock)
                {
                    foreach (string line in lines.Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        string[] parts = DelimitedText.ParseLine(line, ',');
                        if (parts.Length < 8 ||
                            !ulong.TryParse(parts[1], out ulong platformId) ||
                            !long.TryParse(parts[6], out long lastSeenTicks) ||
                            !bool.TryParse(parts[7], out bool isConnected))
                        {
                            continue;
                        }

                        string userKey = parts[0].Trim();
                        if (string.IsNullOrEmpty(userKey))
                        {
                            continue;
                        }

                        EntriesByUserKey[userKey] = new PlayerRegistryEntry
                        {
                            UserKey = userKey,
                            PlatformId = platformId,
                            CharacterName = parts[2].Trim(),
                            OwnerPersistentKey = parts[3].Trim(),
                            OwnerType = parts[4].Trim(),
                            ClanName = parts[5].Trim(),
                            LastSeenUtcTicks = lastSeenTicks,
                            IsConnected = isConnected
                        };
                    }

                    _loadedFromDisk = true;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[PlayerRegistry] Failed to load player registry from disk.", ex);
            }
        }

        private static void SaveToDisk()
        {
            string contentToSave = BuildRegistryCsv();

            try
            {
                SharedFileIO.WriteAllTextWithRetry(GetSaveFilePath(), contentToSave);
                TryDeletePendingFile();

                lock (RegistryLock)
                {
                    _loadedFromDisk = true;
                    _dirty = false;
                }
            }
            catch (IOException ex)
            {
                if (WritePendingRegistrySnapshot(contentToSave))
                {
                    LogRegistryLockWarning("[PlayerRegistry] Main registry CSV is locked. Wrote pending registry snapshot and will retry later.");
                    return;
                }

                LogRegistryLockWarning("[PlayerRegistry] Main registry CSV is locked and pending snapshot could not be written. Registry changes remain queued.", ex);
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning("[PlayerRegistry] Failed to save player registry to disk. Registry changes remain queued.", ex);
            }
        }

        private static string BuildRegistryCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("UserKey,PlatformId,CharacterName,OwnerPersistentKey,OwnerType,ClanName,LastSeenUtcTicks,IsConnected");

            List<PlayerRegistryEntry> entries;
            lock (RegistryLock)
            {
                entries = EntriesByUserKey.Values
                    .OrderBy(entry => entry.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            foreach (PlayerRegistryEntry entry in entries)
            {
                sb.AppendLine(
                    $"{DelimitedText.Escape(entry.UserKey, ',')}," +
                    $"{entry.PlatformId}," +
                    $"{DelimitedText.Escape(entry.CharacterName ?? string.Empty, ',')}," +
                    $"{DelimitedText.Escape(entry.OwnerPersistentKey ?? string.Empty, ',')}," +
                    $"{DelimitedText.Escape(entry.OwnerType ?? string.Empty, ',')}," +
                    $"{DelimitedText.Escape(entry.ClanName ?? string.Empty, ',')}," +
                    $"{entry.LastSeenUtcTicks}," +
                    $"{entry.IsConnected}");
            }

            return sb.ToString();
        }

        private static string GetNewestExistingRegistryFile(string mainFilePath, string pendingFilePath)
        {
            bool mainExists = File.Exists(mainFilePath);
            bool pendingExists = File.Exists(pendingFilePath);

            if (!mainExists && !pendingExists)
            {
                return null;
            }

            if (!mainExists)
            {
                return pendingFilePath;
            }

            if (!pendingExists)
            {
                return mainFilePath;
            }

            return File.GetLastWriteTimeUtc(pendingFilePath) > File.GetLastWriteTimeUtc(mainFilePath)
                ? pendingFilePath
                : mainFilePath;
        }

        private static bool WritePendingRegistrySnapshot(string content)
        {
            try
            {
                SharedFileIO.WriteAllTextWithRetry(GetPendingSaveFilePath(), content);
                lock (RegistryLock)
                {
                    _loadedFromDisk = true;
                    _dirty = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[PlayerRegistry] Pending registry write failed: {ex.Message}");
                return false;
            }
        }

        private static void LogRegistryLockWarning(string message, Exception ex = null)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextLockedRegistryWarningUtc)
            {
                LoggingHelper.Debug($"{message} {ex?.Message}");
                return;
            }

            _nextLockedRegistryWarningUtc = nowUtc.AddMinutes(1);
            LoggingHelper.Warning(message, ex);
        }

        private static void TryDeletePendingFile()
        {
            try
            {
                string pendingFilePath = GetPendingSaveFilePath();
                if (File.Exists(pendingFilePath))
                {
                    File.Delete(pendingFilePath);
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[PlayerRegistry] Could not delete pending registry file: {ex.Message}");
            }
        }
    }
}
