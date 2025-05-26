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

namespace RaidForge
{
    [BepInPlugin("raidforge", "RaidForge", "2.0.0")]
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

        public static List<RaidScheduleEntry> GetSchedule() => RaidConfig.Schedule == null ? new List<RaidScheduleEntry>() : new List<RaidScheduleEntry>(RaidConfig.Schedule);
        public static bool IsAutoRaidCurrentlyActive => RaidSchedulingSystem.IsAutoRaidActive;

        public override void Load()
        {
            Instance = this;
            BepInLogger = Log;

            LoggingHelper.Initialize(BepInLogger);
            LoggingHelper.Info($"RaidForge Plugin starting to load (v{MyPluginInfo.PLUGIN_VERSION})...");

            string configFolderPath = Path.Combine(Paths.ConfigPath, "RaidForge");
            Directory.CreateDirectory(configFolderPath);
            LoggingHelper.Debug($"Custom config folder ensured at: {configFolderPath}");

            _troubleshootingConfigFile = new ConfigFile(Path.Combine(configFolderPath, "Troubleshooting.cfg"), true);
            _raidScheduleConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidScheduleAndGeneral.cfg"), true);
            _golemSettingsConfigFile = new ConfigFile(Path.Combine(configFolderPath, "GolemSettings.cfg"), true);
            _offlineProtectionConfigFile = new ConfigFile(Path.Combine(configFolderPath, "OfflineProtection.cfg"), true);
            _raidInterferenceConfigFile = new ConfigFile(Path.Combine(configFolderPath, "RaidInterference.cfg"), true);
            LoggingHelper.Debug("ConfigFile instances created for all modules.");

            try
            {
                TroubleshootingConfig.Initialize(_troubleshootingConfigFile);
                LoggingHelper.Info($"[Plugin.Load] TroubleshootingConfig.EnableVerboseLogging is currently: {TroubleshootingConfig.EnableVerboseLogging.Value}");

                RaidConfig.Initialize(_raidScheduleConfigFile, BepInLogger);
                GolemAutomationConfig.Initialize(_golemSettingsConfigFile, BepInLogger);
                OfflineRaidProtectionConfig.Initialize(_offlineProtectionConfigFile);
                RaidInterferenceConfig.Initialize(_raidInterferenceConfigFile);

                RaidConfig.ParseSchedule();
                GolemAutomationConfig.ReloadAndParseAll();
                LoggingHelper.Debug("Core configurations initialized and parsed.");
            }
            catch (Exception ex) { LoggingHelper.Error("Failed to initialize configurations.", ex); }

            _troubleshootingConfigFile.SettingChanged += OnTroubleshootingConfigSettingChanged;
            _raidScheduleConfigFile.SettingChanged += OnRaidScheduleConfigSettingChanged;
            _golemSettingsConfigFile.SettingChanged += OnGolemSettingsConfigSettingChanged;
            _offlineProtectionConfigFile.SettingChanged += OnOfflineProtectionConfigSettingChanged;
            _raidInterferenceConfigFile.SettingChanged += OnRaidInterferenceConfigSettingChanged;
            LoggingHelper.Debug("Config SettingChanged event handlers registered.");

            try
            {
                _harmony = new Harmony("raidforge.mod.myvrisingserver.harmony");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                LoggingHelper.Info("Harmony patches applied.");
            }
            catch (Exception ex) { LoggingHelper.Error("Failed to apply Harmony patches.", ex); return; }

            OfflineGraceService.Initialize(BepInLogger);
            RaidInterferenceService.Initialize(BepInLogger);
            LoggingHelper.Debug("Services initialized.");

            CommandRegistry.RegisterAll();
            LoggingHelper.Debug("Commands registered.");

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ModUpdater>()) ClassInjector.RegisterTypeInIl2Cpp<ModUpdater>();
            _updaterComponent = AddComponent<ModUpdater>();
            if (_updaterComponent != null) LoggingHelper.Debug("ModUpdater component added.");
            else LoggingHelper.Error("Failed to add ModUpdater component!");

            if (VWorld.IsServerWorldReady())
            {
                LoggingHelper.Debug("Performing initial system checks as world is ready on load...");
                RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                GolemAutomationSystem.CheckAutomation();
            }
            else
                LoggingHelper.Info("Server world not ready at plugin load, initial system checks will be deferred.");

            LoggingHelper.Info($"RaidForge Plugin (v{MyPluginInfo.PLUGIN_VERSION}) successfully loaded.");
        }

        private void OnTroubleshootingConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Debug($"Troubleshooting.cfg setting changed: [{e.ChangedSetting.Definition.Section}] {e.ChangedSetting.Definition.Key} to {e.ChangedSetting.BoxedValue}.");
            if (e.ChangedSetting.Definition.Key == TroubleshootingConfig.EnableVerboseLogging.Definition.Key)
                LoggingHelper.Info($"Verbose logging has been {(TroubleshootingConfig.EnableVerboseLogging.Value ? "ENABLED" : "DISABLED")}.");
        }
        private void OnRaidScheduleConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Debug($"RaidScheduleAndGeneral.cfg setting changed. Reparsing raid schedule...");
            RaidConfig.ParseSchedule();
            RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
        }
        private void OnGolemSettingsConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Debug($"GolemSettings.cfg setting changed. Reparsing golem settings...");
            GolemAutomationConfig.ReloadAndParseAll();
            GolemAutomationSystem.ResetAutomationState();
            GolemAutomationSystem.CheckAutomation();
        }
        private void OnOfflineProtectionConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Debug($"OfflineProtection.cfg setting changed: [{e.ChangedSetting.Definition.Section}] {e.ChangedSetting.Definition.Key} to {e.ChangedSetting.BoxedValue}.");
            if (e.ChangedSetting.Definition.Key == OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Definition.Key)
                LoggingHelper.Info($"Offline Raid Protection has been {(OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value ? "ENABLED" : "DISABLED")}.");
            if (e.ChangedSetting.Definition.Key == OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Definition.Key)
                LoggingHelper.Info($"Offline Raid Protection Grace Period (minutes) has been changed to: {OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value}.");
        }
        private void OnRaidInterferenceConfigSettingChanged(object sender, SettingChangedEventArgs e)
        {
            LoggingHelper.Debug($"RaidInterference.cfg setting changed: [{e.ChangedSetting.Definition.Section}] {e.ChangedSetting.Definition.Key} to {e.ChangedSetting.BoxedValue}.");
            if (e.ChangedSetting.Definition.Key == RaidInterferenceConfig.EnableRaidInterference.Definition.Key)
                LoggingHelper.Info($"Raid Interference has been {(RaidInterferenceConfig.EnableRaidInterference.Value ? "ENABLED" : "DISABLED")}.");
        }

        public static void ReloadAllConfigsAndRefreshSystems()
        {
            if (Instance == null) return;
            try
            {
                LoggingHelper.Info("Manual configuration reload triggered for all RaidForge configs...");
                _troubleshootingConfigFile?.Reload();
                _raidScheduleConfigFile?.Reload();
                _golemSettingsConfigFile?.Reload();
                _offlineProtectionConfigFile?.Reload();
                _raidInterferenceConfigFile?.Reload();
                LoggingHelper.Info("All RaidForge .cfg files reloaded from disk.");

                RaidConfig.ParseSchedule();
                LoggingHelper.Debug("Raid schedule reparsed.");
                GolemAutomationConfig.ReloadAndParseAll();
                LoggingHelper.Debug("Golem settings reparsed.");

                GolemAutomationSystem.ResetAutomationState();
                LoggingHelper.Debug("Performing system checks after manual reload.");
                RaidSchedulingSystem.CheckScheduleAndToggleRaids(true);
                GolemAutomationSystem.CheckAutomation();
                LoggingHelper.Info("Manual configuration reload and system checks completed.");
            }
            catch (Exception ex) { LoggingHelper.Error("Error during manual configuration reload processing.", ex); }
        }

        public static void TriggerReloadFromCommand() => ReloadAllConfigsAndRefreshSystems();
        public static void TriggerGolemAutomationResetFromCommand() => GolemAutomationSystem.ResetAutomationState();

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

                if (_updaterComponent != null) { UnityEngine.Object.Destroy(_updaterComponent); _updaterComponent = null; LoggingHelper.Debug("ModUpdater component destroyed."); }
                try { CommandRegistry.UnregisterAssembly(); LoggingHelper.Debug("Commands unregistered."); }
                catch (Exception ex) { LoggingHelper.Error("Failed to unregister commands.", ex); }

                RaidInterferenceService.StopService();
                LoggingHelper.Info("RaidInterferenceService.StopService() called.");

                _harmony?.UnpatchSelf();
                LoggingHelper.Debug("Harmony patches unapplied.");
                LoggingHelper.Info($"RaidForge Plugin (v{MyPluginInfo.PLUGIN_VERSION}) unloaded successfully!");
            }
            catch (Exception ex) { LoggingHelper.Error("Error during RaidForge plugin unload.", ex); return false; }
            return true;
        }
    }
}