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
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using ProjectM;
using ProjectM.Network;
using HookDOTS.API;

namespace RaidForge
{
    [BepInPlugin("raidforge", "RaidForge", "3.1.1")]
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
            Logger = Log;
            LoggingHelper.Initialize(Logger);

            LoadConfigs();

            try
            {
                _harmony = new Harmony("raidforge");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                RaidForgeScheduler.Initialize(_harmony);

                _hookDOTS = new HookDOTS.API.HookDOTS("raidforge", Logger);
                _hookDOTS.RegisterAnnotatedHooks();
                Logger.LogInfo("[RaidForge] HookDOTS Registered. Weapon Raiding hook active.");
            }
            catch (Exception e)
            {
                Logger.LogError($"Patching failed: {e.Message}");
            }

            OfflineGraceService.Initialize();
            RaidInterferenceService.Initialize();

            try { CommandRegistry.RegisterAll(); } catch { }

            Logger.LogInfo("[RaidForge] Loaded. Waiting for first player connection to Initialize Systems...");
        }


        public static void AttemptInitializeCoreSystems()
        {
            if (SystemsInitialized) return;
            if (!VWorld.IsServerWorldReady()) return;

            Logger.LogInfo("[RaidForge] First Player Detected. Initializing Systems...");

            EntityManager em = VWorld.EntityManager;

            SystemsInitialized = true;

            OfflineGraceService.LoadOfflineStatesFromDisk(em);

            if (OptInRaidingConfig.EnableOptInRaiding.Value && !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                OptInRaidService.LoadStateFromDisk();
            }

            if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                ShardVulnerabilityService.LoadStateFromDisk();
            }

            int heartsFound = OwnershipCacheService.InitializeHeartOwnershipCache(em);
            int usersFound = OwnershipCacheService.InitializeUserToClanCache(em);
            Logger.LogInfo($"[RaidForge] World Scan Complete: {heartsFound} Hearts, {usersFound} Users.");

            if (ServerHasJustBooted)
            {
                OfflineGraceService.EstablishInitialGracePeriodsOnBoot(em);
                ServerHasJustBooted = false;
            }

            RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
            GolemAutomationSystem.CheckAutomation();

          
            RaidForgeScheduler.RunEvery(() =>
            {
                try
                {
                    RaidSchedulingSystem.CheckScheduleAndToggleRaids();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[RaidForge] Error in scheduled raid check: {ex}");
                }
            }, 30.0);

            RaidForgeScheduler.RunEvery(() =>
            {
                try
                {
                    GolemAutomationSystem.CheckAutomation();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[RaidForge] Error in scheduled Golem check: {ex}");
                }
            }, 3600.0);


            Logger.LogInfo("[RaidForge] Initialization Complete. Mod is Active.");
        }

        public static void NotifyPlayerHasConnectedThisSession() { }

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
                        string persistentKey = clanEntity.Exists() && em.HasComponent<ClanTeam>(clanEntity)
                            ? PersistentKeyHelper.GetClanKey(em, clanEntity)
                            : PersistentKeyHelper.GetUserKey(user.PlatformId);

                        string contextName = clanEntity.Exists() && em.HasComponent<ClanTeam>(clanEntity)
                            ? em.GetComponentData<ClanTeam>(clanEntity).Name.ToString()
                            : user.CharacterName.ToString();

                        if (!ShardVulnerabilityService.IsVulnerable(persistentKey))
                        {
                            ShardVulnerabilityService.SetVulnerable(persistentKey, contextName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error during on-boot shard scan", ex);
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
                if (_weaponRaidingConfigFile != null) _weaponRaidingConfigFile.Reload();

                RaidConfig.ParseSchedule();
                GolemAutomationConfig.ReloadAndParseAll();

                if (SystemsInitialized)
                {
                    ShardVulnerabilityService.LoadStateFromDisk();
                    OptInRaidService.LoadStateFromDisk();

                    EntityManager em = VWorld.EntityManager;
                    int hearts = OwnershipCacheService.InitializeHeartOwnershipCache(em);
                    int users = OwnershipCacheService.InitializeUserToClanCache(em);
                    Logger.LogInfo($"[Reload] Cache refreshed: {hearts} Hearts, {users} Users.");

                    RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                    GolemAutomationSystem.ResetAutomationState();
                    GolemAutomationSystem.CheckAutomation();
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        }

        public static void TriggerReloadFromCommand() => ReloadAllConfigsAndRefreshSystems();
        public static void TriggerGolemAutomationResetFromCommand() { if (SystemsInitialized) GolemAutomationSystem.ResetAutomationState(); }

        private void LoadConfigs()
        {
            string configFolderPath = Path.Combine(Paths.ConfigPath, "RaidForge");
            Directory.CreateDirectory(configFolderPath);

            _troubleshootingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "Troubleshooting.cfg"), true);
            _raidScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidScheduleAndGeneral.cfg"), true);
            _golemSettingsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "GolemSettings.cfg"), true);
            _offlineProtectionConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OfflineProtection.cfg"), true);
            _raidInterferenceConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidInterference.cfg"), true);
            _optInRaidingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OptInRaiding.cfg"), true);
            _optInScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OptInSchedule.cfg"), true);
            _weaponRaidingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "WeaponRaiding.cfg"), true);

            TroubleshootingConfig.Initialize(_troubleshootingConfigFile);
            RaidConfig.Initialize(_raidScheduleConfigFile, Logger);
            GolemAutomationConfig.Initialize(_golemSettingsConfigFile, Logger);
            OfflineRaidProtectionConfig.Initialize(_offlineProtectionConfigFile);
            RaidInterferenceConfig.Initialize(_raidInterferenceConfigFile);
            OptInRaidingConfig.Initialize(_optInRaidingConfigFile);
            OptInScheduleConfig.Initialize(_optInScheduleConfigFile);
            WeaponRaidingConfig.Initialize(_weaponRaidingConfigFile, Logger);

            RaidConfig.ParseSchedule();
            GolemAutomationConfig.ReloadAndParseAll();

            _troubleshootingConfigFile.SettingChanged += OnConfigSettingChanged;
            _raidScheduleConfigFile.SettingChanged += OnConfigSettingChanged;
            _golemSettingsConfigFile.SettingChanged += OnConfigSettingChanged;
            _offlineProtectionConfigFile.SettingChanged += OnConfigSettingChanged;
            _raidInterferenceConfigFile.SettingChanged += OnConfigSettingChanged;
            _optInRaidingConfigFile.SettingChanged += OnConfigSettingChanged;
            _optInScheduleConfigFile.SettingChanged += OnConfigSettingChanged;
        }

        public override bool Unload()
        {
            try
            {
                if (_troubleshootingConfigFile != null) _troubleshootingConfigFile.SettingChanged -= OnConfigSettingChanged;

                RaidForgeScheduler.Dispose();

                _hookDOTS?.Dispose();

                try { CommandRegistry.UnregisterAssembly(); } catch { }
                RaidInterferenceService.StopService();
                OfflineGraceService.Initialize();
                OwnershipCacheService.ClearAllCaches();
                _harmony?.UnpatchSelf();

                SystemsInitialized = false;
                ServerHasJustBooted = true;
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