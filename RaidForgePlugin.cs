using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using VampireCommandFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using HarmonyLib;
using RaidForge.Config;
using RaidForge.Core;
using RaidForge.Patches;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;
using Unity.Entities;
using Unity.Collections;
using ProjectM;
using ProjectM.Network;
using HookDOTS.API;
using Stunlock.Core;

namespace RaidForge
{
    [BepInPlugin("raidforge", "RaidForge", "3.2.2")]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class Plugin : BasePlugin
    {
        private Harmony _harmony;
        private HookDOTS.API.HookDOTS _hookDOTS;

        public static Plugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Logger { get; private set; }

        private static ConfigFile _raidScheduleConfigFile;
        private static ConfigFile _golemSettingsConfigFile;
        private static ConfigFile _offlineProtectionConfigFile;
        private static ConfigFile _raidInterferenceConfigFile;
        private static ConfigFile _troubleshootingConfigFile;
        private static ConfigFile _optInRaidingConfigFile;
        private static ConfigFile _optInScheduleConfigFile;
        private static ConfigFile _weaponRaidingConfigFile;
        private static ConfigFile _tntRaidingConfigFile;
        private static ConfigFile _commandSettingsConfigFile;
        private static ConfigFile _shardConfigFile;
        private static ConfigFile _mapIconsConfigFile;
        private static ConfigFile _purchasedOrpConfigFile;
        private static ConfigFile _servantLimitsConfigFile;

        public static bool SystemsInitialized { get; private set; } = false;
        public static bool ServerHasJustBooted { get; private set; } = true;

        private static bool _onBootShardScanHasRun = false;

        public static List<RaidScheduleEntry> GetSchedule() =>
            RaidConfig.Schedule == null ? new List<RaidScheduleEntry>() : new List<RaidScheduleEntry>(RaidConfig.Schedule);

        public static bool IsAutoRaidCurrentlyActive => RaidSchedulingSystem.IsAutoRaidActive;

        public override void Load()
        {
            Instance = this;
            ServerHasJustBooted = true;
            SystemsInitialized = false;
            _onBootShardScanHasRun = false;

            Logger = Log;
            LoggingHelper.Initialize(Logger);

            ResetRuntimeStateForFreshLoad();
            LoadConfigs();

            try
            {
                _harmony = new Harmony("raidforge");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                RaidForgeScheduler.Initialize();

                _hookDOTS = new HookDOTS.API.HookDOTS("raidforge", Logger);
                _hookDOTS.RegisterAnnotatedHooks();

                Logger.LogInfo("[RaidForge] HookDOTS registered. Damage policy hook active.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[RaidForge] Patching failed: {ex}");
            }

            RaidInterferenceService.Initialize();

            try
            {
                CommandRegistry.RegisterAll();
                CommandRegistryCustomizationService.ApplyStartupConfiguration();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[RaidForge] Failed to register commands: {ex}");
            }

            Logger.LogInfo("[RaidForge] Loaded. Waiting for server world update to initialize systems...");
        }

        public static void AttemptInitializeCoreSystems()
        {
            if (SystemsInitialized)
            {
                return;
            }

            if (!VWorld.IsServerWorldReady())
            {
                return;
            }

            Logger.LogInfo("[RaidForge] Server world detected. Initializing systems...");

            EntityManager em = VWorld.EntityManager;

            PrefabGuidResolver.ValidateKnownPrefabs();
            ServantLimitService.ReloadConfiguredLimits(em);
            SystemsInitialized = true;

            bool offlineProtectionEnabled = OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value;
            bool optInRaidingEnabled = OptInRaidingConfig.EnableOptInRaiding.Value && !offlineProtectionEnabled;
            bool purchasedOrpEnabled = PurchasedOrpConfig.IsPurchasedOrpActive();

            if (offlineProtectionEnabled)
            {
                OfflineGraceService.LoadOfflineStatesFromDisk(em);
            }

            if (optInRaidingEnabled)
            {
                OptInRaidService.LoadStateFromDisk();
            }

            if (purchasedOrpEnabled)
            {
                PurchasedOrpService.LoadStateFromDisk();
            }

            if (offlineProtectionEnabled)
            {
                ShardVulnerabilityService.LoadStateFromDisk();
            }

            int heartsFound = OwnershipCacheService.InitializeHeartOwnershipCache(em);
            int usersFound = OwnershipCacheService.InitializeUserToClanCache(em);

            Logger.LogInfo($"[RaidForge] World Scan Complete: {heartsFound} Hearts, {usersFound} Users.");

            RaidMapIconService.ClearAllRaidForgeIconEntities(em);
            RunOnBootShardScanIfNeeded();

            if (ServerHasJustBooted)
            {
                OfflineGraceService.EstablishInitialGracePeriodsOnBoot(em);
                ServerHasJustBooted = false;
            }

            PlayerRegistryService.RefreshFromWorld(em);
            RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
            GolemAutomationSystem.CheckAutomation();

            RegisterRecurringTask("raid schedule check", () => RaidSchedulingSystem.CheckScheduleAndToggleRaids(), 30.0);
            RegisterRecurringTask("Golem automation check", () => GolemAutomationSystem.CheckAutomation(), 3600.0);
            RegisterRecurringTask("raid interference check", RaidInterferenceService.ProcessInterference, 2.0);
            RegisterRecurringTask("raid map icon cleanup", RaidMapIconService.ProcessCleanup, 5.0);
            RegisterRecurringTask("opt-in cooldown check", ProcessOptInCooldowns, 300.0);
            RegisterRecurringTask("purchased ORP raid-day accounting", PurchasedOrpService.ProcessDueRaidDayConsumption, 300.0);

            Logger.LogInfo("[RaidForge] Initialization Complete. Mod is Active.");
        }

        private static void RegisterRecurringTask(string taskName, Action action, double intervalSeconds)
        {
            RaidForgeScheduler.RunEvery(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[RaidForge] Error in scheduled {taskName}: {ex}");
                }
            }, intervalSeconds);
        }

        private static void ProcessOptInCooldowns()
        {
            if (OptInRaidingConfig.EnableOptInRaiding.Value &&
                !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value &&
                OptInRaidingConfig.AutoOptOutAfterCooldown.Value)
            {
                OptInRaidService.ProcessAutoOptOut(OptInRaidingConfig.OptInLockDurationHours.Value);
            }
        }

        public static void RunOnBootShardScanIfNeeded()
        {
            if (_onBootShardScanHasRun || !SystemsInitialized)
            {
                return;
            }

            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value ||
                !ShardConfig.DisableOrpForShardHolders.Value)
            {
                _onBootShardScanHasRun = true;
                return;
            }

            if (!_onBootShardScanHasRun)
            {
                PerformOnBootShardVulnerabilityScan(VWorld.EntityManager);
                _onBootShardScanHasRun = true;
            }
        }

        private static void PerformOnBootShardVulnerabilityScan(EntityManager em)
        {
            var userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
            var userEntities = userQuery.ToEntityArray(Allocator.Temp);
            var pedestalOwnersScanned = new HashSet<string>();

            try
            {
                foreach (var userEntity in userEntities)
                {
                    if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                        em,
                        userEntity,
                        out OwnerIdentity owner,
                        out string ownerError))
                    {
                        Logger?.LogWarning($"[RaidForge] Skipping on-boot shard scan owner resolution: {ownerError}");
                        continue;
                    }

                    var foundShards = new HashSet<PrefabGUID>();

                    Entity characterEntity = owner.UserData.LocalCharacter._Entity;
                    if (characterEntity.Exists())
                    {
                        if (UserHelper.TryGetSoulShardsOnPerson(em, characterEntity, out var personShards, out _))
                        {
                            foreach (var shard in personShards)
                            {
                                foundShards.Add(shard);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(owner.PersistentKey) &&
                        pedestalOwnersScanned.Add(owner.PersistentKey) &&
                        UserHelper.TryGetSoulShardsInOwnedPedestals(em, userEntity, out var pedestalShards, out _))
                    {
                        foreach (var shard in pedestalShards)
                        {
                            foundShards.Add(shard);
                        }
                    }

                    if (foundShards.Count == 0)
                    {
                        continue;
                    }

                    int maxCopies = ShardConfig.MaxAllowedShardsPerType?.Value ?? 1;

                    foreach (var shard in foundShards)
                    {
                        ShardVulnerabilityService.SetVulnerable(
                            owner.PersistentKey,
                            shard.GuidHash,
                            owner.UserKey,
                            owner.ContextualName,
                            maxCopies,
                            deferSave: true
                        );
                    }
                }

                ShardVulnerabilityService.Flush();
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error during on-boot shard scan", ex);
            }
            finally
            {
                if (userEntities.IsCreated)
                {
                    userEntities.Dispose();
                }

                userQuery.Dispose();
            }
        }

        private void OnConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (sender == _raidScheduleConfigFile)
            {
                RaidConfig.ParseSchedule();
                PurchasedOrpService.ProcessDueRaidDayConsumption(force: true);

                if (SystemsInitialized)
                {
                    RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                }
            }
            else if (sender == _golemSettingsConfigFile)
            {
                GolemAutomationConfig.ReloadAndParseAll();

                if (SystemsInitialized)
                {
                    GolemAutomationSystem.ResetAutomationState();
                    GolemAutomationSystem.CheckAutomation();
                }
            }
            else if (sender == _mapIconsConfigFile)
            {
                if (SystemsInitialized)
                {
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                    RaidMapIconService.ClearAllRaidForgeIconEntities(VWorld.EntityManager);
                    RaidMapIconService.ProcessCleanup();
                }

                Logger?.LogInfo("[RaidForge] MapIcons.cfg changed. New values will be used by map icon services.");
            }
            else if (sender == _optInRaidingConfigFile)
            {
                if (SystemsInitialized)
                {
                    OptInRaidService.ReloadStateFromDisk();
                    PurchasedOrpService.ReloadStateFromDisk();
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                    RaidMapIconService.ClearAllRaidForgeIconEntities(VWorld.EntityManager);
                    RaidMapIconService.ProcessCleanup();
                }

                Logger?.LogInfo("[RaidForge] OptInRaiding.cfg changed. Opt-in mode and map icons were refreshed.");
            }
            else if (sender == _purchasedOrpConfigFile)
            {
                if (SystemsInitialized)
                {
                    PurchasedOrpService.ReloadStateFromDisk();
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                    RaidMapIconService.ClearAllRaidForgeIconEntities(VWorld.EntityManager);
                    RaidMapIconService.ProcessCleanup();
                }

                Logger?.LogInfo("[RaidForge] PurchasedORP.cfg changed. Purchased ORP state and map icons were refreshed.");
            }
            else if (sender == _raidInterferenceConfigFile)
            {
                RaidInterferenceService.ResetRuntimeState();
                RaidInterferenceService.Initialize();
                Logger?.LogInfo("[RaidForge] RaidInterference.cfg changed. Runtime raid interference state was refreshed.");
            }
            else if (sender == _servantLimitsConfigFile)
            {
                if (SystemsInitialized)
                {
                    ServantLimitService.ReloadConfiguredLimits(VWorld.EntityManager);
                }

                Logger?.LogInfo("[RaidForge] ServantLimits.cfg changed. Servant prefab limits were refreshed.");
            }
            else if (sender == _weaponRaidingConfigFile)
            {
                Logger?.LogInfo("[RaidForge] WeaponRaiding.cfg changed. New weapon structure damage settings are active.");
            }
            else if (sender == _tntRaidingConfigFile)
            {
                Logger?.LogInfo("[RaidForge] TntDamageAndRaiding.cfg changed. New TNT damage settings are active.");
            }
        }

        public static void ReloadAllConfigsAndRefreshSystems()
        {
            try
            {
                if (_troubleshootingConfigFile != null) _troubleshootingConfigFile.Reload();
                if (_raidScheduleConfigFile != null) _raidScheduleConfigFile.Reload();
                if (_golemSettingsConfigFile != null) _golemSettingsConfigFile.Reload();
                if (_offlineProtectionConfigFile != null) _offlineProtectionConfigFile.Reload();
                if (_raidInterferenceConfigFile != null) _raidInterferenceConfigFile.Reload();
                if (_optInRaidingConfigFile != null) _optInRaidingConfigFile.Reload();
                if (_optInScheduleConfigFile != null) _optInScheduleConfigFile.Reload();
                if (_weaponRaidingConfigFile != null) _weaponRaidingConfigFile.Reload();
                if (_tntRaidingConfigFile != null) _tntRaidingConfigFile.Reload();
                if (_shardConfigFile != null) _shardConfigFile.Reload();
                if (_mapIconsConfigFile != null) _mapIconsConfigFile.Reload();
                if (_purchasedOrpConfigFile != null) _purchasedOrpConfigFile.Reload();
                if (_servantLimitsConfigFile != null) _servantLimitsConfigFile.Reload();
                RaidConfig.ParseSchedule();
                PurchasedOrpService.ProcessDueRaidDayConsumption(force: true);
                GolemAutomationConfig.ReloadAndParseAll();

                if (SystemsInitialized)
                {
                    OfflineGraceService.ReloadOfflineStatesFromDisk(VWorld.EntityManager);
                    ShardVulnerabilityService.ReloadStateFromDisk();
                    OptInRaidService.ReloadStateFromDisk();
                    PurchasedOrpService.ReloadStateFromDisk();
                    ServantLimitService.ReloadConfiguredLimits(VWorld.EntityManager);

                    EntityManager em = VWorld.EntityManager;

                    int hearts = OwnershipCacheService.InitializeHeartOwnershipCache(em);
                    int users = OwnershipCacheService.InitializeUserToClanCache(em);

                    Logger.LogInfo($"[Reload] Cache refreshed: {hearts} Hearts, {users} Users.");

                    PlayerRegistryService.RefreshFromWorld(em);
                    RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);

                    GolemAutomationSystem.ResetAutomationState();
                    GolemAutomationSystem.CheckAutomation();
                    RaidMapIconService.MarkPersistentStateIconsDirty();
                    RaidMapIconService.ClearAllRaidForgeIconEntities(em);
                    RaidMapIconService.ProcessCleanup();

                    RaidInterferenceService.ResetRuntimeState();
                    RaidInterferenceService.Initialize();
                }

                Logger?.LogInfo("[RaidForge] All config files were reloaded.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[RaidForge] Error reloading configs: {ex}");
            }
        }

        public static void TriggerReloadFromCommand() => ReloadAllConfigsAndRefreshSystems();

        private static void ResetRuntimeStateForFreshLoad()
        {
            ServerWorldReadyPatch.ResetRuntimeState();
            DamageInterceptorPatch.ResetRuntimeState();
            OfflineGraceService.ResetRuntimeState();
            OptInRaidService.ResetRuntimeState();
            PurchasedOrpService.ResetRuntimeState();
            ShardVulnerabilityService.ResetRuntimeState();
            RaidInterferenceService.ResetRuntimeState();
            RaidMapIconService.ResetRuntimeState();
            PlayerRegistryService.ResetRuntimeState();
            ServantLimitService.ResetRuntimeState();
            OwnershipCacheService.ClearAllCaches();

            RaidSchedulingSystem.ResetRuntimeState();
            GolemAutomationSystem.ResetRuntimeState();
        }

        public static void TriggerGolemAutomationResetFromCommand()
        {
            if (SystemsInitialized)
            {
                GolemAutomationSystem.ResetAutomationState();
            }
        }

        private void LoadConfigs()
        {
            string configFolderPath = Path.Combine(Paths.ConfigPath, "RaidForge");
            Directory.CreateDirectory(configFolderPath);
            string tntConfigPath = Path.Combine(configFolderPath, "TntDamageAndRaiding.cfg");
            bool tntConfigExistedBeforeLoad = File.Exists(tntConfigPath);

            _troubleshootingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "Troubleshooting.cfg"), true);
            _raidScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidScheduleAndGeneral.cfg"), true);
            _golemSettingsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "GolemSettings.cfg"), true);
            _offlineProtectionConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OfflineProtection.cfg"), true);
            _raidInterferenceConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidInterference.cfg"), true);
            _optInRaidingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OptInRaiding.cfg"), true);
            _optInScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OptInSchedule.cfg"), true);
            _weaponRaidingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "WeaponRaiding.cfg"), true);
            _tntRaidingConfigFile = new ConfigFile(tntConfigPath, true);
            _commandSettingsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "CommandSettings.cfg"), true);
            _shardConfigFile = new ConfigFile(Path.Combine(configFolderPath, "SoulShards.cfg"), true);
            _mapIconsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "MapIcons.cfg"), true);
            _purchasedOrpConfigFile = new ConfigFile(Path.Combine(configFolderPath, "PurchasedORP.cfg"), true);
            _servantLimitsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "ServantLimits.cfg"), true);
            TroubleshootingConfig.Initialize(_troubleshootingConfigFile);
            RaidConfig.Initialize(_raidScheduleConfigFile, Logger);
            GolemAutomationConfig.Initialize(_golemSettingsConfigFile, Logger);
            OfflineRaidProtectionConfig.Initialize(_offlineProtectionConfigFile);
            RaidInterferenceConfig.Initialize(_raidInterferenceConfigFile);
            OptInRaidingConfig.Initialize(_optInRaidingConfigFile);
            OptInScheduleConfig.Initialize(_optInScheduleConfigFile);
            WeaponRaidingConfig.Initialize(_weaponRaidingConfigFile, Logger);
            TntRaidingConfig.Initialize(_tntRaidingConfigFile, Logger);
            TntRaidingConfig.MigrateLegacySettingsIfNeeded(
                _weaponRaidingConfigFile,
                tntConfigExistedBeforeLoad,
                Logger);
            CommandSettingsConfig.Initialize(_commandSettingsConfigFile, Logger);
            ShardConfig.Initialize(_shardConfigFile);
            MapIconsConfig.Initialize(_mapIconsConfigFile);
            PurchasedOrpConfig.Initialize(_purchasedOrpConfigFile);
            ServantLimitsConfig.Initialize(_servantLimitsConfigFile);

            RaidConfig.ParseSchedule();
            GolemAutomationConfig.ReloadAndParseAll();

            _troubleshootingConfigFile.SettingChanged += OnConfigSettingChanged;
            _raidScheduleConfigFile.SettingChanged += OnConfigSettingChanged;
            _golemSettingsConfigFile.SettingChanged += OnConfigSettingChanged;
            _offlineProtectionConfigFile.SettingChanged += OnConfigSettingChanged;
            _raidInterferenceConfigFile.SettingChanged += OnConfigSettingChanged;
            _optInRaidingConfigFile.SettingChanged += OnConfigSettingChanged;
            _optInScheduleConfigFile.SettingChanged += OnConfigSettingChanged;
            _weaponRaidingConfigFile.SettingChanged += OnConfigSettingChanged;
            _tntRaidingConfigFile.SettingChanged += OnConfigSettingChanged;
            _shardConfigFile.SettingChanged += OnConfigSettingChanged;
            _mapIconsConfigFile.SettingChanged += OnConfigSettingChanged;
            _purchasedOrpConfigFile.SettingChanged += OnConfigSettingChanged;
            _servantLimitsConfigFile.SettingChanged += OnConfigSettingChanged;

            Logger?.LogInfo("[RaidForge] Config files loaded, including CommandSettings.cfg and separate weapon/TNT raiding files.");
        }

        public override bool Unload()
        {
            try
            {
                if (_troubleshootingConfigFile != null) _troubleshootingConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_raidScheduleConfigFile != null) _raidScheduleConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_golemSettingsConfigFile != null) _golemSettingsConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_offlineProtectionConfigFile != null) _offlineProtectionConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_raidInterferenceConfigFile != null) _raidInterferenceConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_optInRaidingConfigFile != null) _optInRaidingConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_optInScheduleConfigFile != null) _optInScheduleConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_weaponRaidingConfigFile != null) _weaponRaidingConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_tntRaidingConfigFile != null) _tntRaidingConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_shardConfigFile != null) _shardConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_mapIconsConfigFile != null) _mapIconsConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_purchasedOrpConfigFile != null) _purchasedOrpConfigFile.SettingChanged -= OnConfigSettingChanged;
                if (_servantLimitsConfigFile != null) _servantLimitsConfigFile.SettingChanged -= OnConfigSettingChanged;

                RaidForgeScheduler.Dispose();

                _hookDOTS?.Dispose();

                try
                {
                    CommandRegistry.UnregisterAssembly();
                }
                catch (Exception ex)
                {
                    Logger?.LogError($"[RaidForge] Failed to unregister commands: {ex}");
                }

                RaidInterferenceService.StopService();

                SharedFileIO.FlushPendingWrites();

                RaidMapIconService.ResetRuntimeState();
                DamageInterceptorPatch.ResetRuntimeState();
                ServerWorldReadyPatch.ResetRuntimeState();
                OfflineGraceService.ResetRuntimeState();
                OptInRaidService.ResetRuntimeState();
                PurchasedOrpService.ResetRuntimeState();
                ShardVulnerabilityService.ResetRuntimeState();
                RaidSchedulingSystem.ResetRuntimeState();
                GolemAutomationSystem.ResetRuntimeState();
                OwnershipCacheService.ClearAllCaches();

                _harmony?.UnpatchSelf();

                SystemsInitialized = false;
                ServerHasJustBooted = true;
                _onBootShardScanHasRun = false;

                Logger?.LogInfo("[RaidForge] Unloaded.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[RaidForge] Error during unload: {ex}");
                return false;
            }

            return true;
        }
    }
}
