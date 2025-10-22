using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using VampireCommandFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using RaidForge.Config;
using RaidForge.Core;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using ProjectM;
using ProjectM.Network;

namespace RaidForge
{
    [BepInPlugin("raidforge", "RaidForge", "3.0.0")]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class Plugin : BasePlugin
    {
        private Harmony _harmony;
        public static Plugin Instance { get; private set; }

        public static BepInEx.Logging.ManualLogSource Logger { get; private set; }

        private ModUpdater _updaterComponent;

        private static ConfigFile _raidScheduleConfigFile;
        private static ConfigFile _golemSettingsConfigFile;
        private static ConfigFile _offlineProtectionConfigFile;
        private static ConfigFile _raidInterferenceConfigFile;
        private static ConfigFile _troubleshootingConfigFile;
        private static ConfigFile _optInRaidingConfigFile;
        private static ConfigFile _optInScheduleConfigFile;

        public static bool SystemsInitialized { get; private set; } = false;
        public static bool ServerHasJustBooted { get; private set; } = true;

        private static bool _onBootShardScanHasRun = false;

        private static int _initializationAttempts = 0;
        private const int MAX_INITIALIZATION_ATTEMPTS = 120;
        private static float _firstInitAttemptTimestamp = 0f;
        private const float MAX_INIT_WAIT_SECONDS = 600.0f;
        private static bool _firstAttemptTimestampSet = false;

        public static List<RaidScheduleEntry> GetSchedule() =>
            RaidConfig.Schedule == null ? new List<RaidScheduleEntry>() : new List<RaidScheduleEntry>(RaidConfig.Schedule);

        public static bool IsAutoRaidCurrentlyActive => RaidSchedulingSystem.IsAutoRaidActive;

        public static void NotifyPlayerHasConnectedThisSession()
        {
            if (ServerHasJustBooted)
            {
                ServerHasJustBooted = false;
            }
        }

        public override void Load()
        {
            Instance = this;
            ServerHasJustBooted = true;

            Logger = Log;
            LoggingHelper.Initialize(Logger); 


            string configFolderPath = Path.Combine(Paths.ConfigPath, "RaidForge");
            Directory.CreateDirectory(configFolderPath);

            _troubleshootingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "Troubleshooting.cfg"), true);
            _raidScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidScheduleAndGeneral.cfg"), true);
            _golemSettingsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "GolemSettings.cfg"), true);
            _offlineProtectionConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OfflineProtection.cfg"), true);
            _raidInterferenceConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidInterference.cfg"), true);
            _optInRaidingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OptInRaiding.cfg"), true);
            _optInScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OptInSchedule.cfg"), true);

            try
            {
                TroubleshootingConfig.Initialize(_troubleshootingConfigFile);
                RaidConfig.Initialize(_raidScheduleConfigFile, Logger); 
                GolemAutomationConfig.Initialize(_golemSettingsConfigFile, Logger); 
                OfflineRaidProtectionConfig.Initialize(_offlineProtectionConfigFile);
                RaidInterferenceConfig.Initialize(_raidInterferenceConfigFile); 
                OptInRaidingConfig.Initialize(_optInRaidingConfigFile);
                OptInScheduleConfig.Initialize(_optInScheduleConfigFile);

                RaidConfig.ParseSchedule();
                GolemAutomationConfig.ReloadAndParseAll();
            }
            catch (Exception e)
            {
                Logger.LogError($"Error loading configs: {e.Message}");
            }

            _troubleshootingConfigFile.SettingChanged += OnConfigSettingChanged;
            _raidScheduleConfigFile.SettingChanged += OnConfigSettingChanged;
            _golemSettingsConfigFile.SettingChanged += OnConfigSettingChanged;
            _offlineProtectionConfigFile.SettingChanged += OnConfigSettingChanged;
            _raidInterferenceConfigFile.SettingChanged += OnConfigSettingChanged;
            _optInRaidingConfigFile.SettingChanged += OnConfigSettingChanged;
            _optInScheduleConfigFile.SettingChanged += OnConfigSettingChanged;

            try
            {
                _harmony = new Harmony("raidforge");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                Logger.LogError($"Harmony patching failed: {e.Message}");
            }

            OfflineGraceService.Initialize();
            RaidInterferenceService.Initialize();
            try
            {
                CommandRegistry.RegisterAll();
            }
            catch (Exception e)
            {
                Logger.LogError($"Command registration failed: {e.Message}");
            }

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ModUpdater>())
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModUpdater>();
            }
            _updaterComponent = AddComponent<ModUpdater>();
        }

        public static void AttemptInitializeCoreSystems()
        {
            if (SystemsInitialized && _firstAttemptTimestampSet) return;
            if (!VWorld.IsServerWorldReady()) return;

            if (!_firstAttemptTimestampSet)
            {
                _firstInitAttemptTimestamp = Time.realtimeSinceStartup;
                _firstAttemptTimestampSet = true;
            }
            _initializationAttempts++;

            EntityManager em = VWorld.EntityManager;
            if (em == default) return;

            OwnershipCacheService.InitializeHeartOwnershipCache(em);
            OwnershipCacheService.InitializeUserToClanCache(em);
            OfflineGraceService.LoadOfflineStatesFromDisk(em);

            bool cacheInitializationConsideredDone = OwnershipCacheService.IsInitialScanAttemptedAndConsideredPopulated();
            bool canProceedWithMainInit = false;

            if (cacheInitializationConsideredDone && (_initializationAttempts > 1)) canProceedWithMainInit = true;
            else if ((Time.realtimeSinceStartup - _firstInitAttemptTimestamp) > MAX_INIT_WAIT_SECONDS) canProceedWithMainInit = true;
            else if (_initializationAttempts >= MAX_INITIALIZATION_ATTEMPTS) canProceedWithMainInit = true;

            if (canProceedWithMainInit)
            {
                if (OptInRaidingConfig.EnableOptInRaiding.Value && !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                {
                    OptInRaidService.LoadStateFromDisk();
                }

                if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                {
                    ShardVulnerabilityService.LoadStateFromDisk();
                }

                SystemsInitialized = true;
                OfflineGraceService.EstablishInitialGracePeriodsOnBoot(em);

                RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                GolemAutomationSystem.CheckAutomation();
            }
        }

        public static void RunOnBootShardScanIfNeeded()
        {
            if (!_onBootShardScanHasRun && SystemsInitialized)
            {
                PerformOnBootShardVulnerabilityScan(VWorld.EntityManager);
                _onBootShardScanHasRun = true;
            }
        }

        private static void PerformOnBootShardVulnerabilityScan(EntityManager em)
        {
            int vulnerableCount = 0;

            var userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
            var userEntities = userQuery.ToEntityArray(Allocator.Temp);

            try
            {
                foreach (var userEntity in userEntities)
                {
                    if (UserHelper.IsVulnerableDueToShard(em, userEntity, out _))
                    {
                        var user = em.GetComponentData<User>(userEntity);
                        Entity clanEntity = user.ClanEntity._Entity;
                        string persistentKey;
                        string contextualName;

                        if (clanEntity.Exists() && em.HasComponent<ClanTeam>(clanEntity))
                        {
                            persistentKey = PersistentKeyHelper.GetClanKey(em, clanEntity);
                            contextualName = em.GetComponentData<ClanTeam>(clanEntity).Name.ToString();
                        }
                        else
                        {
                            persistentKey = PersistentKeyHelper.GetUserKey(user.PlatformId);
                            contextualName = user.CharacterName.ToString();
                        }

                        if (!ShardVulnerabilityService.IsVulnerable(persistentKey))
                        {
                            ShardVulnerabilityService.SetVulnerable(persistentKey, contextualName);
                            vulnerableCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("An error occurred during the on-boot shard vulnerability scan.", ex); 
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                userQuery.Dispose();
            }

          
        }

        private void OnConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (sender == _raidScheduleConfigFile)
            {
                RaidConfig.ParseSchedule();
                if (SystemsInitialized) RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
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

                if (SystemsInitialized)
                {
                    ShardVulnerabilityService.LoadStateFromDisk();
                    OptInRaidService.LoadStateFromDisk();
                }

                RaidConfig.ParseSchedule();
                GolemAutomationConfig.ReloadAndParseAll();

                if (SystemsInitialized)
                {
                    RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                    GolemAutomationSystem.ResetAutomationState();
                    GolemAutomationSystem.CheckAutomation();
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("An error occurred during ReloadAllConfigsAndRefreshSystems.", ex); 
            }
        }

        public static void TriggerReloadFromCommand() => ReloadAllConfigsAndRefreshSystems();

        public static void TriggerGolemAutomationResetFromCommand()
        {
            if (SystemsInitialized)
            {
                GolemAutomationSystem.ResetAutomationState();
            }
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

                if (_updaterComponent != null) { UnityEngine.Object.Destroy(_updaterComponent); _updaterComponent = null; }
                try { CommandRegistry.UnregisterAssembly(); } catch { }
                RaidInterferenceService.StopService();
                OfflineGraceService.Initialize();
                OwnershipCacheService.ClearAllCaches();
                _harmony?.UnpatchSelf();

                SystemsInitialized = false;
                ServerHasJustBooted = true;
                _initializationAttempts = 0;
                _firstAttemptTimestampSet = false;

                _onBootShardScanHasRun = false;
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}