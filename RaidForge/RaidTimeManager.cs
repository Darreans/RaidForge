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

        // If .raidoff in the middle of today's on-window => skip the rest
        private static bool _skipWindow;
        private static DayOfWeek _skipDay;
        private static TimeSpan _skipStart;
        private static TimeSpan _skipEnd;

        // If _manualOverride != null, it takes priority for the rest of the day.
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

        // This is called every 5s from GameFrame_OnUpdate in your plugin
        public static void OnServerTick()
        {
            if (!_initialized)
            {
                _initialized = true;
                LoadConfig();
                RaidForgePlugin.Logger.LogInfo("[RaidtimeManager] Initialized. Using 5s checks via GameFrame.");
            }

            CheckAndToggleRaidMode();
        }

        public static void ReloadFromConfig(bool immediate = false)
        {
            LoadConfig();
            if (immediate)
            {
                // Force a re-check right now
                CheckAndToggleRaidMode();
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

            // Normal day-of-week logic, but first check manual overrides:
            var now = DateTime.Now;
            var day = now.DayOfWeek;
            var time = now.TimeOfDay;

            // 1) If day changed from the override day, drop the override
            if (_manualOverride.HasValue && day != _manualOverrideDay)
            {
                _manualOverride = null; // new day => reset override
            }

            // 2) If we have a manual override, it takes priority
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

            // If day changed, reset skip
            if (_skipWindow && day != _skipDay)
            {
                _skipWindow = false;
            }

            // If skipping remainder of the window => remain off
            if (_skipWindow && day == _skipDay && time >= _skipStart && time <= _skipEnd)
            {
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                return;
            }
            else if (_skipWindow)
            {
                // if we moved beyond that window or day changed, clear skip
                if (day != _skipDay || time > _skipEnd)
                {
                    _skipWindow = false;
                }
            }

            // day-of-week scheduling from Raidschedule.Windows:
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
                // If we have no window for that day, remain off
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
            }
        }

        /// <summary>
        /// If .raidoff is called while inside today's on-window => skip remainder.
        /// </summary>
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

        /// <summary>
        /// Called by .raidt => find next ON boundary
        /// </summary>
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

                // If today and we're already inside the window, skip it.
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

        // .raidon / .raidoff manual override for the rest of today.
        public static void SetManualOverride(bool forcedOn)
        {
            _manualOverride = forcedOn;
            _manualOverrideDay = DateTime.Now.DayOfWeek;
        }

        /// <summary>
        /// If the user changes the schedule for the day-of-week that is *today*,
        /// we can forcibly clear skip logic. This allows re-scheduling for later in the same day.
        /// </summary>
        public static void ClearSkipForTodayIfDayMatches(DayOfWeek changedDay)
        {
            var nowDay = DateTime.Now.DayOfWeek;
            if (_skipWindow && changedDay == _skipDay && changedDay == nowDay)
            {
                // We had a skip window set for today, 
                // but the user changed today's times => let's clear skip.
                _skipWindow = false;
            }
        }
    }
}
