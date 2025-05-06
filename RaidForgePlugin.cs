using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using VampireCommandFramework;
using System;
using System.Collections.Generic;
using ProjectM;
using Il2CppInterop.Runtime.Injection;
using Unity.Entities;

namespace RaidForge
{
    [BepInPlugin("raidforge", "RaidForge Mod", "1.1.0")] 
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class RaidForgePlugin : BasePlugin
    {
        public static RaidForgePlugin Instance { get; private set; }
        public static ManualLogSource Logger { get; private set; }

        public static ConfigEntry<bool> EnableVerboseLogging { get; private set; }

        private static bool _isAutoRaidActive = false;
        private static int _lastGolemCheckDay = -1;
        private static SiegeWeaponHealth? _lastGolemHealthApplied = null;
        private static bool _initialCheckPerformed = false;

        private RaidForgeUpdater _updaterComponent;

        public static List<RaidScheduleEntry> GetSchedule()
        {
            return RaidConfig.Schedule == null ? null : new List<RaidScheduleEntry>(RaidConfig.Schedule);
        }

        public override void Load()
        {
            try
            {
                Instance = this;
                Logger = Log;

                EnableVerboseLogging = Config.Bind("General", "EnableVerboseLogging", false,
                    "Set to true to enable detailed informational logs for debugging.");

                Log.LogInfo($"Loading RaidForge plugin (v1.1.0)");
                if (EnableVerboseLogging.Value) Log.LogInfo("Verbose logging enabled.");

                RaidConfig.Initialize(this.Config, Logger);
                GolemAutomationConfig.Initialize(this.Config, Logger);

                RaidConfig.ParseSchedule();

                Config.SettingChanged += OnConfigChanged;

                try { CommandRegistry.RegisterAll(); } catch (Exception ex) { Log.LogError($"!!! Failed to register commands. Exception: {ex}"); }

                try
                {
                    if (!ClassInjector.IsTypeRegisteredInIl2Cpp<RaidForgeUpdater>())
                    { ClassInjector.RegisterTypeInIl2Cpp<RaidForgeUpdater>(); }
                    _updaterComponent = this.AddComponent<RaidForgeUpdater>();
                    if (_updaterComponent != null) { if (EnableVerboseLogging.Value) Log.LogInfo($"Updater component added. Checks run every 30 seconds once world is ready."); } else { Log.LogError("!!! Failed to add RaidForgeUpdater component!"); }
                }
                catch (Exception ex) { Log.LogError($"!!! CRITICAL: Failed to initialize MonoBehaviour updater: {ex}"); }

                Log.LogInfo("RaidForge plugin load sequence complete.");
            }
            catch (Exception ex) { Log.LogError($"!!! CRITICAL Error during RaidForge plugin load: {ex}"); }
        }

        private void OnConfigChanged(object sender, SettingChangedEventArgs e)
        {
            string section = e.ChangedSetting.Definition.Section;

            if (e.ChangedSetting.Definition.Key == EnableVerboseLogging.Definition.Key)
            {
                Logger.LogInfo($"Verbose logging {(EnableVerboseLogging.Value ? "enabled" : "disabled")}.");
            }

            if (section == "Daily Schedule")
            {
                if (EnableVerboseLogging.Value) Logger.LogInfo($"Raid config changed ({e.ChangedSetting.Definition.Key}), reloading schedule...");
                RaidConfig.ParseSchedule();
                if (EnableVerboseLogging.Value) Logger.LogInfo("Performing raid check after config change.");
                CheckScheduleAndToggleRaids(true);
            }
            else if (section == "GolemAutomation" || section == "GolemAutomation.Levels") 
            {
                if (EnableVerboseLogging.Value) Logger.LogInfo($"Golem Automation config changed ({e.ChangedSetting.Definition.Key}), reloading schedule...");
                GolemAutomationConfig.ReloadAndParseAll();
                _lastGolemCheckDay = -1;
                _lastGolemHealthApplied = null;
                if (EnableVerboseLogging.Value) Logger.LogInfo("Performing golem automation check after config change.");
                CheckGolemAutomation();
            }
        }

        public static void ReloadConfigAndSchedule()
        {
            if (Logger == null || Instance == null) { Console.WriteLine("[RaidForge] Reload requested but Logger or Instance is null!"); return; }
            try
            {
                Logger.LogInfo("Manual configuration reload triggered...");
                Instance.Config.Reload();
                Logger.LogInfo("Config file reloaded.");

                if (EnableVerboseLogging.Value) Logger.LogInfo("Reparsing Raid Schedule...");
                RaidConfig.ParseSchedule();
                if (EnableVerboseLogging.Value) Logger.LogInfo("Reparsing Golem Automation Schedule...");
                GolemAutomationConfig.ReloadAndParseAll();

                _lastGolemCheckDay = -1;
                _lastGolemHealthApplied = null;

                if (EnableVerboseLogging.Value) Logger.LogInfo("Performing checks after manual reload.");
                CheckScheduleAndToggleRaids(true);
                CheckGolemAutomation();

                Logger.LogInfo("Manual configuration reload and checks completed.");
            }
            catch (Exception ex) { Logger.LogError($"!!! Error during manual config reload: {ex}"); }
        }

        public static bool CheckGolemAutomation()
        {
            if (!GolemAutomationConfig.EnableGolemAutomation.Value) return true;
            if (!GolemAutomationConfig.ParsedStartDate.HasValue) return true;

            EntityManager entityManager;
            try { entityManager = VWorldUtils.EntityManager; if (!VWorldUtils.IsServerWorldReady()) { return false; } } catch { return false; }

            DateTime now = DateTime.Now;
            DateTime serverStart = GolemAutomationConfig.ParsedStartDate.Value;
            int currentDayCount = (int)Math.Floor((now - serverStart).TotalDays);
            if (currentDayCount < 0) currentDayCount = 0;

            if (currentDayCount == _lastGolemCheckDay) return true;

            if (EnableVerboseLogging.Value) Logger?.LogInfo($"Golem Automation: Checking for Day {currentDayCount} (since {serverStart:yyyy-MM-dd})...");

            SiegeWeaponHealth? targetHealth = GolemAutomationConfig.GetTargetHealthForDay(currentDayCount);

            if (targetHealth.HasValue)
            {
                if (_lastGolemHealthApplied.HasValue && _lastGolemHealthApplied.Value == targetHealth.Value)
                {
                    if (EnableVerboseLogging.Value) Logger?.LogInfo($"Golem Automation: Target health ({targetHealth.Value}) for Day {currentDayCount} is same as last applied. No change needed.");
                    _lastGolemCheckDay = currentDayCount;
                }
                else
                {
                    if (EnableVerboseLogging.Value) Logger?.LogInfo($"Golem Automation: Attempting to set Siege Golem Health to {targetHealth.Value} for Day {currentDayCount}...");
                    bool success = SiegeWeaponManager.SetSiegeWeaponHealth(targetHealth.Value, Logger); 
                    if (success)
                    {
                        _lastGolemCheckDay = currentDayCount;
                        _lastGolemHealthApplied = targetHealth.Value;
                    }
                    else
                    {
                        Logger?.LogError($"!!! Golem Automation: Failed to set health for Day {currentDayCount}. Will retry on next check cycle.");
                    }
                }
            }
            else
            {
                if (EnableVerboseLogging.Value) Logger?.LogInfo($"Golem Automation: No applicable health schedule found for Day {currentDayCount}. No change needed.");
                _lastGolemCheckDay = currentDayCount;
            }

            return true;
        }


        public static bool CheckScheduleAndToggleRaids(bool forceCheck = false)
        {
            bool isInitialOrManualCheck = forceCheck;
            EntityManager entityManager;

            try
            {
                entityManager = VWorldUtils.EntityManager;
                if (!VWorldUtils.IsServerWorldReady())
                {
                    if (isInitialOrManualCheck && !_initialCheckPerformed)
                    {
                        Logger?.LogWarning("Raid Schedule Check: Server world not ready yet.");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"Raid Schedule Check: Server world access error: {ex.Message}");
                return false;
            }

            var now = DateTime.Now;
            var currentSchedule = RaidConfig.Schedule;

            if (currentSchedule == null) { Logger?.LogError("!!! Raid Schedule Check FAILED: RaidConfig.Schedule is null."); return true; }

            bool shouldBeActive = false;
            string reason = "No matching schedule entry found.";

            foreach (var entry in currentSchedule) { if (now.DayOfWeek == entry.Day) { if (!entry.SpansMidnight) { if (now.TimeOfDay >= entry.StartTime && now.TimeOfDay < entry.EndTime) { shouldBeActive = true; reason = $"Matches {entry.Day} {entry.StartTime:hh\\:mm} - {entry.EndTime:hh\\:mm}"; break; } } else { if (now.TimeOfDay >= entry.StartTime) { shouldBeActive = true; reason = $"Matches start of midnight span: {entry.Day} {entry.StartTime:hh\\:mm} - {entry.EndTime:hh\\:mm} (next day)"; break; } } } else if (entry.SpansMidnight && now.DayOfWeek == (entry.Day + 1 == (DayOfWeek)7 ? DayOfWeek.Sunday : entry.Day + 1)) { if (now.TimeOfDay < entry.EndTime) { shouldBeActive = true; reason = $"Matches end of midnight span: Started {entry.Day} {entry.StartTime:hh\\:mm}, ending {entry.EndTime:hh\\:mm} today ({now.DayOfWeek})"; break; } } }

            bool stateNeedsChanging = shouldBeActive != _isAutoRaidActive;
            bool logDetails = EnableVerboseLogging.Value || stateNeedsChanging;

            if (logDetails)
            {
                if (isInitialOrManualCheck && EnableVerboseLogging.Value) Logger?.LogInfo($"Raid Check Triggered: Manual/Initial. Current Time: {now:yyyy-MM-dd HH:mm:ss}");
                if (EnableVerboseLogging.Value) Logger?.LogInfo($"Raid Schedule Evaluation: Raids should be {(shouldBeActive ? "ON" : "OFF")}. Reason: {reason}");
                if (stateNeedsChanging) Logger?.LogInfo($"Raid Current State: {_isAutoRaidActive}. State requires change."); 
            }

            bool toggleSuccess = true;
            if (stateNeedsChanging)
            {
                CastleDamageMode targetMode = shouldBeActive ? CastleDamageMode.Always : CastleDamageMode.TimeRestricted;
                Logger?.LogInfo(shouldBeActive ? ">>> Raids Enabling (Scheduled)..." : "<<< Raids Disabling (Scheduled)...");
                toggleSuccess = SetCastleDamageModeInternal(entityManager, targetMode, Logger);
                if (toggleSuccess) { _isAutoRaidActive = shouldBeActive; } else { Logger?.LogError($"!!! Failed to set CastleDamageMode to {targetMode}. Raid state unchanged."); }
            }

            if (isInitialOrManualCheck && !stateNeedsChanging && EnableVerboseLogging.Value) { Logger?.LogInfo($"=== Raid State matches desired state ({shouldBeActive}). No action needed on forced check."); }

            if (isInitialOrManualCheck)
            {
                if (!_initialCheckPerformed && EnableVerboseLogging.Value) Logger?.LogInfo("Initial check flag set.");
                _initialCheckPerformed = true;
            }

            return true;
        }

        private static bool SetCastleDamageModeInternal(EntityManager entityManager, CastleDamageMode newMode, ManualLogSource logger)
        {
            try
            {
                ComponentType[] queryComponents = { ComponentType.ReadWrite<ServerGameBalanceSettings>() };
                EntityQuery settingsQuery = entityManager.CreateEntityQuery(queryComponents);
                if (settingsQuery.IsEmptyIgnoreFilter) { logger?.LogError("!!! SetCastleDamageModeInternal: Could not find ServerGameBalanceSettings entity."); return false; }
                Entity settingsEntity = settingsQuery.GetSingletonEntity();
                ServerGameBalanceSettings balanceSettings = entityManager.GetComponentData<ServerGameBalanceSettings>(settingsEntity);
                if (balanceSettings.CastleDamageMode == newMode) { if (EnableVerboseLogging.Value) logger?.LogInfo($"SetCastleDamageModeInternal: Mode already set to {newMode}."); return true; }
                balanceSettings.CastleDamageMode = newMode;
                entityManager.SetComponentData(settingsEntity, balanceSettings);
                logger?.LogInfo($"SetCastleDamageModeInternal: Successfully set CastleDamageMode to {newMode}.");
                return true;
            }
            catch (Exception ex) { logger?.LogError($"!!! SetCastleDamageModeInternal Error: {ex}"); return false; }
        }

        public static void ResetGolemCheckDay()
        {
            _lastGolemCheckDay = -1;
            _lastGolemHealthApplied = null;
            if (EnableVerboseLogging.Value) Logger?.LogInfo("Golem automation check day reset.");
        }

        public override bool Unload()
        {
            try
            {
                Log.LogInfo("Unloading RaidForge plugin...");
                Config.SettingChanged -= OnConfigChanged;
                if (_updaterComponent != null) { UnityEngine.Object.Destroy(_updaterComponent); _updaterComponent = null; if (EnableVerboseLogging.Value) Log.LogInfo("Destroyed RaidForgeUpdater component."); }
                try { CommandRegistry.UnregisterAssembly(); } catch (Exception ex) { Log.LogError($"Failed to unregister commands. Exception: {ex}"); }
                Log.LogInfo("RaidForge plugin unloaded successfully!");
                return true;
            }
            catch (Exception ex) { Log.LogError($"!!! Error during RaidForge plugin unload: {ex}"); return false; }
        }
    }
}
