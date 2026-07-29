using System;
using System.Linq;
using ProjectM;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Systems
{
    public static class RaidSchedulingSystem
    {
        public static bool IsAutoRaidActive { get; private set; } = false;
        private static bool _initialCheckPerformed = false;
        private static bool? _manualRaidOverride = null;

        public static bool HasManualOverride => _manualRaidOverride.HasValue;
        public static bool? ManualOverrideValue => _manualRaidOverride;

        public static void ResetRuntimeState()
        {
            IsAutoRaidActive = false;
            _initialCheckPerformed = false;
            _manualRaidOverride = null;
        }

        public static bool SetManualOverride(bool raidsEnabled)
        {
            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Warning($"RaidSchedulingSystem: Could not set manual raid override to {raidsEnabled}; server world is not ready.");
                return false;
            }

            _manualRaidOverride = raidsEnabled;
            LoggingHelper.Info($"RaidSchedulingSystem: Manual raid override set to {(raidsEnabled ? "ON" : "OFF")}. Schedule automation will not change raids until the override is cleared.");
            return ApplyDesiredRaidState(raidsEnabled, "Manual override");
        }

        public static bool ClearManualOverride()
        {
            _manualRaidOverride = null;
            LoggingHelper.Info("RaidSchedulingSystem: Manual raid override cleared. Resuming schedule control.");
            return CheckScheduleAndToggleRaids(true);
        }

        public static bool CheckScheduleAndToggleRaids(bool forceCheck = false)
        {
            bool isInitialOrManualCheck = forceCheck || !_initialCheckPerformed;

            if (!VWorld.IsServerWorldReady())
            {
                if (isInitialOrManualCheck && (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false))
                {
                    LoggingHelper.Warning("RaidSchedulingSystem: Server world not ready yet for schedule check.");
                }
                return false;
            }

            if (_manualRaidOverride.HasValue)
            {
                bool desiredManualState = _manualRaidOverride.Value;
                bool currentActualState = RaidToggleSystem.AreRaidsEnabled();
                bool manualStateNeedsChanging = desiredManualState != currentActualState;

                if (manualStateNeedsChanging || (isInitialOrManualCheck && (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false)))
                {
                    LoggingHelper.Info($"RaidSchedulingSystem: Manual override active - Should be {(desiredManualState ? "ON" : "OFF")}. Current live state: {currentActualState}. Change needed: {manualStateNeedsChanging}");
                }

                if (manualStateNeedsChanging)
                {
                    ApplyDesiredRaidState(desiredManualState, "Manual override");
                }
                else
                {
                    IsAutoRaidActive = desiredManualState;
                }

                MarkInitialCheckPerformed();
                return true;
            }

            var serverNow = DateTime.Now;
            var now = RaidConfig.GetRaidScheduleNow();
            var currentSchedule = RaidConfig.Schedule;

            if (currentSchedule == null)
            {
                LoggingHelper.Error("RaidSchedulingSystem: Check FAILED - RaidConfig.Schedule is null. Raids will be treated as OFF by default.");
                if (RaidToggleSystem.AreRaidsEnabled())
                {
                    LoggingHelper.Info("RaidSchedulingSystem: Turning raids OFF due to null schedule.");
                    RaidToggleSystem.DisableRaids();
                }

                IsAutoRaidActive = false;
                return true;
            }

            bool shouldBeActive = false;
            string reason = "No matching schedule entry found.";

            foreach (var entry in currentSchedule)
            {
                DateTime raidStartDateTime;
                DateTime raidEndDateTime;

                if (entry.Day == now.DayOfWeek)
                {
                    raidStartDateTime = now.Date + entry.StartTime;

                    if (entry.EndTime == TimeSpan.Zero && entry.SpansMidnight)
                    {
                        raidEndDateTime = now.Date.AddDays(1);
                    }
                    else
                    {
                        raidEndDateTime = entry.SpansMidnight ? now.Date.AddDays(1) + entry.EndTime : now.Date + entry.EndTime;
                    }

                    if (now >= raidStartDateTime && now < raidEndDateTime)
                    {
                        shouldBeActive = true;
                        string endTimeDisplay = (entry.EndTime == TimeSpan.Zero && entry.SpansMidnight ? "24:00" : entry.EndTime.ToString("hh\\:mm"));
                        reason = $"Active window: {entry.Day} {entry.StartTime:hh\\:mm} - {endTimeDisplay}";
                        break;
                    }
                }
                else if (entry.SpansMidnight && (int)entry.Day == ((int)now.DayOfWeek - 1 + 7) % 7)
                {
                    raidStartDateTime = now.Date.AddDays(-1) + entry.StartTime;
                    raidEndDateTime = now.Date + entry.EndTime;

                    if (now >= raidStartDateTime && now < raidEndDateTime)
                    {
                        shouldBeActive = true;
                        reason = $"Active window (spanned from yesterday): {entry.Day} {entry.StartTime:hh\\:mm} - {entry.EndTime:hh\\:mm}";
                        break;
                    }
                }
            }

            bool currentLiveState = RaidToggleSystem.AreRaidsEnabled();
            bool stateNeedsChanging = shouldBeActive != currentLiveState;

            if (stateNeedsChanging || (isInitialOrManualCheck && (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false)))
            {
                LoggingHelper.Info($"RaidSchedulingSystem: Schedule check - Should be {(shouldBeActive ? "ON" : "OFF")}. Reason: {reason}. Schedule clock: {now:ddd HH:mm}. Server clock: {serverNow:ddd HH:mm}. Current live state: {currentLiveState}. Internal state: {IsAutoRaidActive}. Change needed: {stateNeedsChanging}");
            }

            if (stateNeedsChanging)
            {
                ApplyDesiredRaidState(shouldBeActive, "Scheduled");
            }
            else if (isInitialOrManualCheck && (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false))
            {
                IsAutoRaidActive = shouldBeActive;
                LoggingHelper.Info($"RaidSchedulingSystem: Raid state ({IsAutoRaidActive}) matches desired state. No action needed.");
            }

            MarkInitialCheckPerformed();
            return true;
        }

        private static bool ApplyDesiredRaidState(bool shouldBeActive, string source)
        {
            LoggingHelper.Info(shouldBeActive ? $"RaidSchedulingSystem: Enabling raids ({source})..." : $"RaidSchedulingSystem: Disabling raids ({source})...");
            bool toggleSuccess = shouldBeActive ? RaidToggleSystem.EnableRaids() : RaidToggleSystem.DisableRaids();

            if (toggleSuccess)
            {
                IsAutoRaidActive = shouldBeActive;
                LoggingHelper.Info($"RaidSchedulingSystem: Raid state successfully changed to: {IsAutoRaidActive} ({source})");
                return true;
            }

            LoggingHelper.Error($"RaidSchedulingSystem: FAILED to set CastleDamageMode via {source}. Raid state remains {IsAutoRaidActive}.");
            return false;
        }

        private static void MarkInitialCheckPerformed()
        {
            if (!_initialCheckPerformed && VWorld.IsServerWorldReady())
            {
                _initialCheckPerformed = true;
                if (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false) LoggingHelper.Info("RaidSchedulingSystem: Initial raid schedule check performed.");
            }
        }
    }
}
