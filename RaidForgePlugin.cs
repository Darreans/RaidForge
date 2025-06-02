using BepInEx;
using BepInEx.Logging;
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

namespace RaidForge
{
    [BepInPlugin("raidforge", "RaidForge", "v2.2.1")] 
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class Plugin : BasePlugin
    {
        private Harmony _harmony;
        public static Plugin Instance { get; private set; }
        public static ManualLogSource BepInLogger { get; private set; }
        private ModUpdater _updaterComponent;

        private static ConfigFile _raidScheduleConfigFile;
        private static ConfigFile _golemSettingsConfigFile;
        private static ConfigFile _offlineProtectionConfigFile;
        private static ConfigFile _raidInterferenceConfigFile;
        private static ConfigFile _troubleshootingConfigFile;

        public static bool SystemsInitialized { get; private set; } = false;
        public static bool ServerHasJustBooted { get; private set; } = true; 

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
                LoggingHelper.Info("[Plugin] First active player connection this session. 'ServerHasJustBooted' flag set to false.");
                ServerHasJustBooted = false;
            }
        }

        public override void Load()
        {
            Instance = this;
            BepInLogger = Log;
            ServerHasJustBooted = true; 

            LoggingHelper.Initialize(BepInLogger);
            LoggingHelper.Info($"RaidForge Plugin starting to load (v2.2.1)...");

            string configFolderPath = Path.Combine(Paths.ConfigPath, "RaidForge");
            Directory.CreateDirectory(configFolderPath);

            _troubleshootingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "Troubleshooting.cfg"), true);
            _raidScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidScheduleAndGeneral.cfg"), true);
            _golemSettingsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "GolemSettings.cfg"), true);
            _offlineProtectionConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OfflineProtection.cfg"), true);
            _raidInterferenceConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidInterference.cfg"), true);

            try
            {
                TroubleshootingConfig.Initialize(_troubleshootingConfigFile);
                RaidConfig.Initialize(_raidScheduleConfigFile, BepInLogger);
                GolemAutomationConfig.Initialize(_golemSettingsConfigFile, BepInLogger);
                OfflineRaidProtectionConfig.Initialize(_offlineProtectionConfigFile);
                RaidInterferenceConfig.Initialize(_raidInterferenceConfigFile, BepInLogger);

                RaidConfig.ParseSchedule();
                GolemAutomationConfig.ReloadAndParseAll();
                LoggingHelper.Debug("Core configurations initialized and parsed.");
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Failed to initialize or parse configurations during Load.", ex);
            }

            _troubleshootingConfigFile.SettingChanged += OnTroubleshootingConfigSettingChanged;
            _raidScheduleConfigFile.SettingChanged += OnRaidScheduleConfigSettingChanged;
            _golemSettingsConfigFile.SettingChanged += OnGolemSettingsConfigSettingChanged;
            _offlineProtectionConfigFile.SettingChanged += OnOfflineProtectionConfigSettingChanged;
            _raidInterferenceConfigFile.SettingChanged += OnRaidInterferenceConfigSettingChanged;

            try
            {
                _harmony = new Harmony("raidforge"); 
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                LoggingHelper.Info("Harmony patches applied.");
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("CRITICAL: Failed to apply Harmony patches.", ex);
            }

            OfflineGraceService.Initialize(); 
            RaidInterferenceService.Initialize(BepInLogger); 
            LoggingHelper.Debug("Basic services (OfflineGrace, RaidInterference) Initialize() called.");

            try
            {
                CommandRegistry.RegisterAll();
                LoggingHelper.Debug("Commands registered.");
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("CRITICAL: Failed to register commands.", ex);
            }

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ModUpdater>())
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModUpdater>();
            }
            _updaterComponent = AddComponent<ModUpdater>();
            if (_updaterComponent == null)
            {
                LoggingHelper.Error("Failed to add ModUpdater component! Core logic will not run.");
            }
            else
            {
                LoggingHelper.Debug("ModUpdater component added.");
            }

            LoggingHelper.Info($"RaidForge Plugin load sequence complete. Waiting for world to be ready to initialize core systems...");
        }

        public static void AttemptInitializeCoreSystems()
        {
            if (SystemsInitialized && _firstAttemptTimestampSet)
            {
                return;
            }

            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Debug("[Plugin] AttemptInitializeCoreSystems: VWorld not ready yet. Will retry.");
                return;
            }

            if (!_firstAttemptTimestampSet)
            {
                _firstInitAttemptTimestamp = Time.realtimeSinceStartup;
                _firstAttemptTimestampSet = true;
                LoggingHelper.Info($"[Plugin] VWorld is ready. First attempt to initialize core systems at game time {_firstInitAttemptTimestamp:F2}s.");
            }
            _initializationAttempts++;
            LoggingHelper.Info($"[Plugin] Attempting core system initialization (Attempt: {_initializationAttempts})");

            EntityManager em = VWorld.EntityManager;
            if (em == default)
            {
                LoggingHelper.Error("[Plugin] AttemptInitializeCoreSystems: EntityManager is default. Cannot initialize further.");
                return;
            }

            LoggingHelper.Info("[Plugin] Initializing ownership caches...");
            int heartsFound = OwnershipCacheService.InitializeHeartOwnershipCache(em);
            int usersForClanCacheFound = OwnershipCacheService.InitializeUserToClanCache(em);

            LoggingHelper.Info("[Plugin] Loading persisted offline states from OfflineGraceService...");
            OfflineGraceService.LoadOfflineStatesFromDisk(em);

            bool cacheInitializationConsideredDone = OwnershipCacheService.IsInitialScanAttemptedAndConsideredPopulated();
            bool canProceedWithMainInit = false;

            if (cacheInitializationConsideredDone && (_initializationAttempts > 1 || heartsFound > 0 || usersForClanCacheFound > 0))
            {
                LoggingHelper.Info($"[Plugin] Ownership cache and persisted offline states loading considered complete.");
                canProceedWithMainInit = true;
            }
            else if ((Time.realtimeSinceStartup - _firstInitAttemptTimestamp) > MAX_INIT_WAIT_SECONDS)
            {
                LoggingHelper.Warning($"[Plugin] Max initialization time ({MAX_INIT_WAIT_SECONDS}s) reached. Proceeding. Cache status: {cacheInitializationConsideredDone}");
                canProceedWithMainInit = true;
            }
            else if (_initializationAttempts >= MAX_INITIALIZATION_ATTEMPTS)
            {
                LoggingHelper.Warning($"[Plugin] Max initialization attempts ({_initializationAttempts}/{MAX_INITIALIZATION_ATTEMPTS}) reached. Proceeding. Cache status: {cacheInitializationConsideredDone}");
                canProceedWithMainInit = true;
            }

            if (canProceedWithMainInit)
            {
             
                SystemsInitialized = true;
                LoggingHelper.Info("[Plugin] Base data systems (Caches, Persisted Data Load) INITIALIZED.");
                LoggingHelper.Info("[Plugin] Performing follow-up initial system setups...");

               
                LoggingHelper.Info("[Plugin] Calling OfflineGraceService.EstablishInitialGracePeriodsOnBoot...");
                OfflineGraceService.EstablishInitialGracePeriodsOnBoot(em);

                RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                GolemAutomationSystem.CheckAutomation();

                LoggingHelper.Info("[Plugin] All core systems and initial checks are now complete. Plugin fully active.");
            }
            else
            {
                LoggingHelper.Info($"[Plugin] Initialization attempt {_initializationAttempts}: Core data services not yet considered fully ready. Will retry.");
            }
        }

        private void OnTroubleshootingConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Info($"Troubleshooting.cfg: [{e.ChangedSetting.Definition.Section}] {e.ChangedSetting.Definition.Key} = {e.ChangedSetting.BoxedValue}.");
            if (e.ChangedSetting.Definition.Key == TroubleshootingConfig.EnableVerboseLogging.Definition.Key)
                LoggingHelper.Info($"Verbose logging has been {(TroubleshootingConfig.EnableVerboseLogging.Value ? "ENABLED" : "DISABLED")}.");
        }

        private void OnRaidScheduleConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Info($"RaidScheduleAndGeneral.cfg changed. Reparsing raid schedule...");
            RaidConfig.ParseSchedule();
            if (SystemsInitialized) RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
            else LoggingHelper.Warning("Raid schedule changed, but systems not initialized. Check skipped.");
        }

        private void OnGolemSettingsConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Info($"GolemSettings.cfg changed. Reparsing golem settings...");
            GolemAutomationConfig.ReloadAndParseAll();
            if (SystemsInitialized)
            {
                GolemAutomationSystem.ResetAutomationState();
                GolemAutomationSystem.CheckAutomation();
            }
            else LoggingHelper.Warning("Golem settings changed, but systems not initialized. Check skipped.");
        }

        private void OnOfflineProtectionConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Info($"OfflineProtection.cfg changed: [{e.ChangedSetting.Definition.Section}] {e.ChangedSetting.Definition.Key} = {e.ChangedSetting.BoxedValue}.");
        }

        private void OnRaidInterferenceConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Info($"RaidInterference.cfg changed: [{e.ChangedSetting.Definition.Section}] {e.ChangedSetting.Definition.Key} = {e.ChangedSetting.BoxedValue}.");
        }

        public static void ReloadAllConfigsAndRefreshSystems()
        {
            LoggingHelper.Info("ReloadAllConfigsAndRefreshSystems called by command...");
            try
            {
                if (_troubleshootingConfigFile != null) _troubleshootingConfigFile.Reload();
                if (_raidScheduleConfigFile != null) _raidScheduleConfigFile.Reload();
                if (_golemSettingsConfigFile != null) _golemSettingsConfigFile.Reload();
                if (_offlineProtectionConfigFile != null) _offlineProtectionConfigFile.Reload();
                if (_raidInterferenceConfigFile != null) _raidInterferenceConfigFile.Reload();

                RaidConfig.ParseSchedule();
                GolemAutomationConfig.ReloadAndParseAll();
                LoggingHelper.Info("All configurations reloaded and reparsed.");

                if (SystemsInitialized)
                {
                    LoggingHelper.Info("Refreshing systems due to config reload...");
                    RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                    GolemAutomationSystem.ResetAutomationState();
                    GolemAutomationSystem.CheckAutomation();
                }
                else { LoggingHelper.Warning("Configs reloaded, but systems not initialized. System refresh skipped."); }
            }
            catch (Exception ex) { LoggingHelper.Error("Error during ReloadAllConfigsAndRefreshSystems: ", ex); }
        }
        public static void TriggerReloadFromCommand() => ReloadAllConfigsAndRefreshSystems();
        public static void TriggerGolemAutomationResetFromCommand()
        {
            if (SystemsInitialized)
            {
                GolemAutomationSystem.ResetAutomationState();
                LoggingHelper.Info("Golem automation state reset by command.");
            }
        }

        public override bool Unload()
        {
            LoggingHelper.Info("Unloading RaidForge Plugin...");
            try
            {
               

                if (_troubleshootingConfigFile != null) _troubleshootingConfigFile.SettingChanged -= OnTroubleshootingConfigSettingChanged;
                if (_raidScheduleConfigFile != null) _raidScheduleConfigFile.SettingChanged -= OnRaidScheduleConfigSettingChanged;
                if (_golemSettingsConfigFile != null) _golemSettingsConfigFile.SettingChanged -= OnGolemSettingsConfigSettingChanged;
                if (_offlineProtectionConfigFile != null) _offlineProtectionConfigFile.SettingChanged -= OnOfflineProtectionConfigSettingChanged;
                if (_raidInterferenceConfigFile != null) _raidInterferenceConfigFile.SettingChanged -= OnRaidInterferenceConfigSettingChanged;

                if (_updaterComponent != null) { UnityEngine.Object.Destroy(_updaterComponent); _updaterComponent = null; }
                try { CommandRegistry.UnregisterAssembly(); } catch { /* Ignore */ }
                RaidInterferenceService.StopService();
                OfflineGraceService.Initialize();
                OwnershipCacheService.ClearAllCaches();
                _harmony?.UnpatchSelf();

                SystemsInitialized = false;
                ServerHasJustBooted = true;
                _initializationAttempts = 0;
                _firstAttemptTimestampSet = false;

                LoggingHelper.Info($"RaidForge Plugin (v2.2.1) unloaded successfully!");
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error during RaidForge plugin unload.", ex);
                return false;
            }
            return true;
        }
    }
}