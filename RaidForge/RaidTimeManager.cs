using System;
using static RaidForge.Raidschedule;

namespace RaidForge
{
    public static class RaidtimeManager
    {
        public enum RaidMode
        {
            ForceOn,
            ForceOff,
            Normal
        }

        private static bool _initialized;
        private static RaidMode _currentMode = RaidMode.Normal;

        private static bool _skipWindow;
        private static DayOfWeek _skipDay;
        private static TimeSpan _skipStart;
        private static TimeSpan _skipEnd;

        private static bool? _manualOverride = null;
        private static DayOfWeek _manualOverrideDay;

        public static void Initialize()
        {
            _initialized = false;
            _skipWindow = false;
            _manualOverride = null;
        }

        public static void Dispose()
        {
            _initialized = false;
            _skipWindow = false;
            _manualOverride = null;
        }

        // This is called every 5s from GameFrame_OnUpdate in the plugin
        public static void OnServerTick()
        {
            if (!_initialized)
            {
                _initialized = true;
                LoadConfig();
                RaidForgePlugin.Logger.LogInfo("[RaidtimeManager] Initialized. Using 5s checks via GameFrame.");

                // Also load the GolemAuto config if we haven't yet:
                GolemAutoManager.LoadConfig();
            }

            // Check & toggle raid mode based on schedule or overrides:
            CheckAndToggleRaidMode();

            // Now update day-based golem HP, if automation is on:
            GolemAutoManager.UpdateIfNeeded();
        }

        public static void ReloadFromConfig(bool immediate = false)
        {
            LoadConfig();
            if (immediate)
            {
                CheckAndToggleRaidMode();
                GolemAutoManager.LoadConfig(); // reload day->HP map in case it changed
                GolemAutoManager.UpdateIfNeeded();
            }
        }

        private static void LoadConfig()
        {
            var modeStr = Raidschedule.OverrideMode.Value;
            if (modeStr.Equals("ForceOn", StringComparison.OrdinalIgnoreCase)
                || modeStr.Equals("AlwaysOn", StringComparison.OrdinalIgnoreCase))
            {
                _currentMode = RaidMode.ForceOn;
            }
            else if (modeStr.Equals("ForceOff", StringComparison.OrdinalIgnoreCase))
            {
                _currentMode = RaidMode.ForceOff;
            }
            else
            {
                _currentMode = RaidMode.Normal;
            }
        }

        private static void CheckAndToggleRaidMode()
        {
            // If config override is ForceOn => always on.
            if (_currentMode == RaidMode.ForceOn)
            {
                VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
                return;
            }
            // If config override is ForceOff => always off.
            if (_currentMode == RaidMode.ForceOff)
            {
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                return;
            }

            // Normal mode => day-of-week logic unless there's a manual override
            var now = DateTime.Now;
            var day = now.DayOfWeek;
            var time = now.TimeOfDay;

            // If we changed days, reset the override
            if (_manualOverride.HasValue && day != _manualOverrideDay)
            {
                _manualOverride = null;
            }

            // Manual override => full priority
            if (_manualOverride.HasValue)
            {
                if (_manualOverride.Value)
                {
                    VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
                }
                else
                {
                    VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                }
                return;
            }

            // If skipping remainder of the day’s window
            if (_skipWindow && day != _skipDay)
            {
                _skipWindow = false;
            }

            if (_skipWindow && day == _skipDay && time >= _skipStart && time <= _skipEnd)
            {
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                return;
            }
            else if (_skipWindow)
            {
                if (day != _skipDay || time > _skipEnd)
                {
                    _skipWindow = false;
                }
            }

            // day-of-week scheduling
            if (Raidschedule.Windows.TryGetValue(day, out var window))
            {
                if (time >= window.Start && time <= window.End)
                {
                    VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
                }
                else
                {
                    VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                }
            }
            else
            {
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
            }
        }

        public static void SkipCurrentWindowIfAny()
        {
            if (_currentMode != RaidMode.Normal) return;

            var now = DateTime.Now;
            var day = now.DayOfWeek;
            var time = now.TimeOfDay;

            if (!Raidschedule.Windows.TryGetValue(day, out var window)) return;

            if (time >= window.Start && time <= window.End)
            {
                _skipWindow = true;
                _skipDay = day;
                _skipStart = window.Start;
                _skipEnd = window.End;
            }
        }

        public static DateTime? GetNextOnTime(DateTime now)
        {
            for (int i = 0; i < 7; i++)
            {
                var checkDate = now.Date.AddDays(i);
                var dayOfWeek = checkDate.DayOfWeek;

                if (!Raidschedule.Windows.TryGetValue(dayOfWeek, out var window))
                {
                    continue;
                }

                var start = checkDate + window.Start;
                var end = checkDate + window.End;

                if (i == 0 && now >= start && now <= end)
                {
                    continue;
                }

                if (start > now)
                {
                    return start;
                }
            }
            return null;
        }

        public static void SetManualOverride(bool forcedOn)
        {
            _manualOverride = forcedOn;
            _manualOverrideDay = DateTime.Now.DayOfWeek;
        }

        public static void ClearManualOverride()
        {
            _manualOverride = null;
            _skipWindow = false;
        }

        public static void ClearSkipForTodayIfDayMatches(DayOfWeek changedDay)
        {
            var nowDay = DateTime.Now.DayOfWeek;
            if (_skipWindow && changedDay == _skipDay && changedDay == nowDay)
            {
                _skipWindow = false;
            }
        }
    }
}
