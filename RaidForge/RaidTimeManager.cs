using System;
using BepInEx;

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
        private static int _intervalSec = 30;

        private static DateTime _lastCheckTime = DateTime.MinValue;

        // If .raidoff in the middle of today's on-window => skip the rest
        private static bool _skipWindow;
        private static DayOfWeek _skipDay;
        private static TimeSpan _skipStart;
        private static TimeSpan _skipEnd;

        public static void Initialize()
        {
            _initialized = false;
        }

        public static void Dispose()
        {
            _initialized = false;
            _skipWindow = false;
        }

        public static void OnServerTick()
        {
            if (!_initialized)
            {
                _initialized = true;
                LoadConfig();
                _lastCheckTime = DateTime.Now;
                RaidForgePlugin.Logger.LogInfo($"[RaidtimeManager] Initialized. intervalSec={_intervalSec}");
            }

            var now = DateTime.Now;
            double delta = (now - _lastCheckTime).TotalSeconds;
            if (delta < _intervalSec) return;

            _lastCheckTime = now;
            CheckAndToggleRaidMode();
        }

        public static void ReloadFromConfig(bool immediate = false)
        {
            LoadConfig();
            if (immediate)
            {
                _lastCheckTime = DateTime.Now - TimeSpan.FromSeconds(_intervalSec);
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

            _intervalSec = Raidschedule.RaidCheckInterval.Value;
            if (_intervalSec < 1) _intervalSec = 1;
        }

        private static void CheckAndToggleRaidMode()
        {
            // If config override is ForceOn => always on
            if (_currentMode == RaidMode.ForceOn)
            {
                VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
                return;
            }
            // If config override is ForceOff => always off
            if (_currentMode == RaidMode.ForceOff)
            {
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                return;
            }

            // Normal day-of-week logic
            var now = DateTime.Now;
            var day = now.DayOfWeek;
            var time = now.TimeOfDay;

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

            // Now do single day-of-week scheduling
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

        /// <summary>
        /// If .raidoff is called while inside today's on-window => skip remainder
        /// </summary>
        public static void SkipCurrentWindowIfAny()
        {
            if (_currentMode != RaidMode.Normal) return;

            var now = DateTime.Now;
            var day = now.DayOfWeek;
            var time = now.TimeOfDay;

            if (!Raidschedule.Windows.TryGetValue(day, out var window)) return;

            // If in the window => skip the rest
            if (time >= window.Start && time <= window.End)
            {
                _skipWindow = true;
                _skipDay = day;
                _skipStart = window.Start;
                _skipEnd = window.End;
            }
        }

        /// <summary>
        /// Called by .raidt. 
        /// Finds the next time the schedule will turn ON in the next 7 days.
        /// If we're currently INSIDE today's window, we skip it and look to tomorrow's.
        /// If the window is ahead in the same day, we return that.
        /// Otherwise we keep scanning future days up to 7 days out.
        /// </summary>
        public static DateTime? GetNextOnTime(DateTime now)
        {
            for (int i = 0; i < 7; i++)
            {
                var checkDate = now.Date.AddDays(i);
                var dayOfWeek = checkDate.DayOfWeek;

                if (!Raidschedule.Windows.TryGetValue(dayOfWeek, out var window))
                {
                    // No window for that day => skip
                    continue;
                }

                var start = checkDate + window.Start;
                var end = checkDate + window.End;

                // If we are currently inside today's window => next on is tomorrow's
                // so we skip if i=0 & now >= start && now <= end
                if (i == 0 && now >= start && now <= end)
                {
                    // skip this window, look to next day
                    continue;
                }

                // If the window start is still in the future for that day
                if (start > now)
                {
                    // This is the next on boundary
                    return start;
                }
            }

            return null;
        }
    }
}
