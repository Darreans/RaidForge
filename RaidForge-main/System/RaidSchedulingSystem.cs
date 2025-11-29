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

            var now = DateTime.Now;
            var currentSchedule = RaidConfig.Schedule;

            if (currentSchedule == null)
            {
                LoggingHelper.Error("RaidSchedulingSystem: Check FAILED - RaidConfig.Schedule is null. Raids will be treated as OFF by default.");
                if (IsAutoRaidActive)
                {
                    LoggingHelper.Info("RaidSchedulingSystem: Turning raids OFF due to null schedule.");
                    RaidToggleSystem.DisableRaids();
                    IsAutoRaidActive = false;
                }
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

            bool stateNeedsChanging = shouldBeActive != IsAutoRaidActive;

            if (stateNeedsChanging || (isInitialOrManualCheck && (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false)))
            {
                LoggingHelper.Info($"RaidSchedulingSystem: Schedule check - Should be {(shouldBeActive ? "ON" : "OFF")}. Reason: {reason}. Current state: {IsAutoRaidActive}. Change needed: {stateNeedsChanging}");
            }

            if (stateNeedsChanging)
            {
                LoggingHelper.Info(shouldBeActive ? "RaidSchedulingSystem: Enabling raids (Scheduled)..." : "RaidSchedulingSystem: Disabling raids (Scheduled)...");
                bool toggleSuccess = shouldBeActive ? RaidToggleSystem.EnableRaids() : RaidToggleSystem.DisableRaids();

                if (toggleSuccess)
                {
                    IsAutoRaidActive = shouldBeActive;
                    LoggingHelper.Info($"RaidSchedulingSystem: Raid state successfully changed to: {IsAutoRaidActive}");
                }
                else
                {
                    LoggingHelper.Error($"RaidSchedulingSystem: FAILED to set CastleDamageMode. Raid state remains {IsAutoRaidActive}.");
                }
            }
            else if (isInitialOrManualCheck && (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false))
            {
                LoggingHelper.Info($"RaidSchedulingSystem: Raid state ({IsAutoRaidActive}) matches desired state. No action needed.");
            }

            if (!_initialCheckPerformed && VWorld.IsServerWorldReady())
            {
                _initialCheckPerformed = true;
                if (TroubleshootingConfig.EnableVerboseLogging?.Value ?? false) LoggingHelper.Info("RaidSchedulingSystem: Initial raid schedule check performed.");
            }
            return true;
        }
    }
}