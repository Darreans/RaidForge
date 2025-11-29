using System;
using ProjectM;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Systems
{
    public static class GolemAutomationSystem
    {
        private static int _lastDayCheckedForSchedule = -1;
        private static SiegeWeaponHealth? _lastHealthSuccessfullySet = null;

        public static bool CheckAutomation()
        {
            if (!VWorld.IsServerWorldReady())
            {
                if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                    LoggingHelper.Debug("GolemAutomationSystem: Server world not ready. Golem automation check skipped.");
                return false;
            }

            SiegeWeaponHealth? determinedTargetHealth = null;
            string determinedSource = "None";

            string manualLevelOverrideString = GolemAutomationConfig.ManualSiegeWeaponHealthOverride?.Value;

            if (!string.IsNullOrEmpty(manualLevelOverrideString))
            {
                if (Enum.TryParse<SiegeWeaponHealth>(manualLevelOverrideString, true, out var manualLevel)) 
                {
                    determinedTargetHealth = manualLevel;
                    determinedSource = $"Manual Level Override ('{manualLevelOverrideString}')";
                    LoggingHelper.Debug($"[GolemAutomationSystem] Manual SiegeWeaponHealth level override active: {manualLevelOverrideString}.");
                }
                else
                {
                    LoggingHelper.Warning($"[GolemAutomationSystem] Invalid ManualSiegeWeaponHealthOverride value in config: '{manualLevelOverrideString}'. It will be ignored.");
                }
            }

            if (!determinedTargetHealth.HasValue && (GolemAutomationConfig.EnableDayBasedAutomation?.Value ?? false))
            {
                if (!GolemAutomationConfig.ParsedStartDate.HasValue)
                {
                    if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
                        LoggingHelper.Debug("GolemAutomationSystem: Day-based automation enabled, but ServerStartDateForAutomation not parsed. Schedule skipped.");
                }
                else
                {
                    DateTime now = DateTime.Now;
                    DateTime serverStart = GolemAutomationConfig.ParsedStartDate.Value;
                    int currentDayCount = (int)Math.Floor((now - serverStart).TotalDays);
                    if (currentDayCount < 0) currentDayCount = 0;

                    LoggingHelper.Debug($"[GolemAutomationSystem] Evaluating day-based schedule for Day {currentDayCount}. Last schedule check day: {_lastDayCheckedForSchedule}.");

                    determinedTargetHealth = GolemAutomationConfig.GetScheduledHealthForDay(currentDayCount);
                    if (determinedTargetHealth.HasValue)
                    {
                        determinedSource = $"Day-Based Schedule (Day {currentDayCount} -> Level {determinedTargetHealth.Value})";
                        LoggingHelper.Debug($"[GolemAutomationSystem] Day-based schedule determined target: {determinedTargetHealth.Value}.");
                    }
                    else
                    {
                        LoggingHelper.Debug($"[GolemAutomationSystem] No day-based schedule entry found for Day {currentDayCount}.");
                    }
                    _lastDayCheckedForSchedule = currentDayCount;
                }
            }
            else if (!determinedTargetHealth.HasValue)
            {
                LoggingHelper.Debug("[GolemAutomationSystem] No manual override set and day-based automation is disabled.");
            }


            if (determinedTargetHealth.HasValue)
            {
                if (!_lastHealthSuccessfullySet.HasValue || _lastHealthSuccessfullySet.Value != determinedTargetHealth.Value)
                {
                    LoggingHelper.Info($"[GolemAutomationSystem] Attempting to set Golem Health to {determinedTargetHealth.Value} (Source: {determinedSource}).");
                    bool success = SiegeWeaponSystem.SetSiegeWeaponHealth(determinedTargetHealth.Value);
                    if (success)
                    {
                        _lastHealthSuccessfullySet = determinedTargetHealth.Value;
                        LoggingHelper.Info($"[GolemAutomationSystem] Successfully set Golem Health to {determinedTargetHealth.Value}.");
                    }
                    else
                    {
                        LoggingHelper.Error($"[GolemAutomationSystem] FAILED to set Golem Health to {determinedTargetHealth.Value}.");
                    }
                }
                else
                {
                    LoggingHelper.Debug($"[GolemAutomationSystem] Golem Health {determinedTargetHealth.Value} (Source: {determinedSource}) is already what was last successfully set. No change needed.");
                }
            }
            else
            {
                LoggingHelper.Debug("[GolemAutomationSystem] No golem health change determined by overrides or active day-based schedule for today.");
            }
            return true;
        }

        public static void ResetAutomationState()
        {
            _lastDayCheckedForSchedule = -1;
            _lastHealthSuccessfullySet = null;
            LoggingHelper.Info("GolemAutomationSystem: State (last check day and last applied health) has been reset.");
        }
    }
}