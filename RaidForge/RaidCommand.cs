using VampireCommandFramework;
using System;
using System.Collections.Generic;

namespace RaidForge
{
    public class RaidCommand
    {
        // I put these values here to give players/admins an idea of how much HP each setting has
        private static readonly Dictionary<ProjectM.SiegeWeaponHealth, int> GolemHpEstimates
            = new Dictionary<ProjectM.SiegeWeaponHealth, int>
            {
                { ProjectM.SiegeWeaponHealth.VeryLow,   750 },
                { ProjectM.SiegeWeaponHealth.Low,      1000 },
                { ProjectM.SiegeWeaponHealth.Normal,   1250 },
                { ProjectM.SiegeWeaponHealth.High,     1750 },
                { ProjectM.SiegeWeaponHealth.VeryHigh, 2500 },
                { ProjectM.SiegeWeaponHealth.MegaHigh, 3250 },
                { ProjectM.SiegeWeaponHealth.UltraHigh,4000 },
                { ProjectM.SiegeWeaponHealth.CrazyHigh,5000 },
                { ProjectM.SiegeWeaponHealth.Max,      7500 },
            };

        // ========================= RAID MODE SWITCHER =========================
        [Command("raidmode",
                 "Sets the override mode to ForceOn, ForceOff, or Normal. Example: .raidmode ForceOn",
                 adminOnly: true)]
        public void RaidMode(ChatCommandContext ctx, string newMode)
        {
            newMode = newMode.Trim();

            if (!newMode.Equals("ForceOn", StringComparison.OrdinalIgnoreCase) &&
                !newMode.Equals("ForceOff", StringComparison.OrdinalIgnoreCase) &&
                !newMode.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Invalid mode. Use ForceOn, ForceOff, or Normal.</color>");
                return;
            }

            // Store it in config
            Raidschedule.OverrideMode.Value = newMode;
            Raidschedule.LoadFromConfig();

            // Reload the manager so we pick up changes
            RaidtimeManager.ReloadFromConfig(true);

            ctx.Reply($"<color=#FFD700>[RaidForge]</color> OverrideMode set to <color=#00FFFF>{newMode}</color>. " +
                      $"<color=#FFFFFF>(If Normal, day-of-week scheduling is active.)</color>");
        }

        // ========================= RAID ON/OFF =========================

        [Command("raidon", "Immediately turn raids ON (one-time).", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            // Force raids on for the rest of today (unless .raidoff or .raidresume).
            RaidtimeManager.SetManualOverride(true);
            VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);

            ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#00FF00>Raids turned ON</color> now and will remain ON " +
                      "<color=#FFFFFF>(unless manually turned OFF or resumed schedule).</color>");
        }

        [Command("raidoff", "Immediately turn raids OFF if it was turned on manually.", adminOnly: true)]
        public void RaidOff(ChatCommandContext ctx)
        {
            // Force raids off for the rest of today (unless .raidon or .raidresume).
            RaidtimeManager.SetManualOverride(false);
            VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);

            ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Raids turned OFF.</color>");
        }

        // Clears the manual override and resumes the normal day-of-week schedule
        [Command("raidresume", "Reverts to normal day-of-week schedule right now.", adminOnly: true)]
        public void RaidResume(ChatCommandContext ctx)
        {
            RaidtimeManager.ClearManualOverride();
            RaidtimeManager.ReloadFromConfig(true);

            ctx.Reply("<color=#FFD700>[RaidForge]</color> " +
                      "<color=#FFFFFF>Resumed day-of-week schedule! Raids now follow the config again.</color>");
        }

        [Command("raidtime", "Shows current server date/time and day-of-week.", adminOnly: true)]
        public void RaidTime(ChatCommandContext ctx)
        {
            var now = DateTime.Now;
            ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Server time: {now:F} (DayOfWeek={now.DayOfWeek}).</color>");
        }

        [Command("raidt", "Shows next scheduled time raids will turn ON automatically.", adminOnly: false)]
        public void RaidNextOnTime(ChatCommandContext ctx)
        {
            var nextOn = RaidtimeManager.GetNextOnTime(DateTime.Now);
            if (nextOn == null)
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>No further raid-on time found in the next 7 days.</color>");
            }
            else
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Next scheduled ON time: {nextOn.Value:F}</color>");
            }
        }

        // ========================= GOLEM / SIEGEWEAPON HEALTH =========================

        [Command("golemcurrent", "Shows the current SiegeWeaponHealth (from SGB).", adminOnly: true)]
        public void GolemCurrent(ChatCommandContext ctx)
        {
            var current = SiegeWeaponManager.GetSiegeWeaponHealth(RaidForgePlugin.Logger);
            if (current == null)
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Could not retrieve SiegeWeaponHealth (no SGB entity?).</color>");
                return;
            }

            // Approx HP
            string hpText = "";
            if (GolemHpEstimates.TryGetValue(current.Value, out int approxHp))
            {
                hpText = $" (~{approxHp} HP)";
            }

            ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Current SiegeWeaponHealth = {current.Value}{hpText}.</color>");
        }

        [Command("golemverylow", "Sets SiegeWeaponHealth => VeryLow", adminOnly: true)]
        public void GolemVeryLow(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.VeryLow);
        }

        [Command("golemlow", "Sets SiegeWeaponHealth => Low", adminOnly: true)]
        public void GolemLow(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.Low);
        }

        [Command("golemnormal", "Sets SiegeWeaponHealth => Normal", adminOnly: true)]
        public void GolemNormal(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.Normal);
        }

        [Command("golemhigh", "Sets SiegeWeaponHealth => High", adminOnly: true)]
        public void GolemHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.High);
        }

        [Command("golemveryhigh", "Sets SiegeWeaponHealth => VeryHigh", adminOnly: true)]
        public void GolemVeryHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.VeryHigh);
        }

        [Command("golemmegahigh", "Sets SiegeWeaponHealth => MegaHigh", adminOnly: true)]
        public void GolemMegaHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.MegaHigh);
        }

        [Command("golemultrahigh", "Sets SiegeWeaponHealth => UltraHigh", adminOnly: true)]
        public void GolemUltraHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.UltraHigh);
        }

        [Command("golemcrazyhigh", "Sets SiegeWeaponHealth => CrazyHigh", adminOnly: true)]
        public void GolemCrazyHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.CrazyHigh);
        }

        [Command("golemmax", "Sets SiegeWeaponHealth => Max", adminOnly: true)]
        public void GolemMax(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.Max);
        }

        private void ApplySiegeHealth(ChatCommandContext ctx, ProjectM.SiegeWeaponHealth healthValue)
        {
            bool ok = SiegeWeaponManager.SetSiegeWeaponHealth(healthValue, RaidForgePlugin.Logger);
            if (!ok)
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FF0000>Failed to set SiegeWeaponHealth to {healthValue}!</color>");
                return;
            }

            string hpText = "";
            if (GolemHpEstimates.TryGetValue(healthValue, out int approxHp))
            {
                hpText = $" <color=red>{approxHp}</color=red> Health";
            }

            ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#00FF00>Golems updated to{hpText}.</color>");
        }

        // ========================= RAID DAY-OF-WEEK WINDOW COMMANDS (HH:mm) =========================

        [Command("raidmon", "Set Monday’s raid window (HH:mm). Example: .raidmon 3:10 3:15", adminOnly: true)]
        public void RaidMonday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Monday, startTimeStr, endTimeStr);
        }

        [Command("raidtue", "Set Tuesday’s raid window (HH:mm). Example: .raidtue 19:30 21:00", adminOnly: true)]
        public void RaidTuesday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Tuesday, startTimeStr, endTimeStr);
        }

        [Command("raidwed", "Set Wednesday’s raid window (HH:mm).", adminOnly: true)]
        public void RaidWednesday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Wednesday, startTimeStr, endTimeStr);
        }

        [Command("raidthu", "Set Thursday’s raid window (HH:mm).", adminOnly: true)]
        public void RaidThursday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Thursday, startTimeStr, endTimeStr);
        }

        [Command("raidfri", "Set Friday’s raid window (HH:mm).", adminOnly: true)]
        public void RaidFriday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Friday, startTimeStr, endTimeStr);
        }

        [Command("raidsat", "Set Saturday’s raid window (HH:mm).", adminOnly: true)]
        public void RaidSaturday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Saturday, startTimeStr, endTimeStr);
        }

        [Command("raidsun", "Set Sunday’s raid window (HH:mm).", adminOnly: true)]
        public void RaidSunday(ChatCommandContext ctx, string startTimeStr, string endTimeStr)
        {
            SetRaidWindow(ctx, DayOfWeek.Sunday, startTimeStr, endTimeStr);
        }

        [Command("raidsched", "Displays entire weekly schedule from your config.", adminOnly: true)]
        public void RaidSchedule(ChatCommandContext ctx)
        {
            ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Current Weekly Raid Schedule:</color>");
            foreach (var kvp in Raidschedule.Windows)
            {
                var day = kvp.Key;
                var window = kvp.Value;
                string startH = $"{window.Start.Hours:00}:{window.Start.Minutes:00}";
                string endH = $"{window.End.Hours:00}:{window.End.Minutes:00}";
                ctx.Reply($"  <color=#FFFFFF>- {day}: {startH} - {endH}</color>");
            }
        }

        private void SetRaidWindow(ChatCommandContext ctx, DayOfWeek day, string startTimeStr, string endTimeStr)
        {
            if (!TimeSpan.TryParse(startTimeStr, out var startSpan))
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FF0000>Invalid start time '{startTimeStr}'. Use HH:mm format.</color>");
                return;
            }
            if (!TimeSpan.TryParse(endTimeStr, out var endSpan))
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FF0000>Invalid end time '{endTimeStr}'. Use HH:mm format.</color>");
                return;
            }

            if (startSpan.TotalHours < 0 || startSpan.TotalHours > 24 ||
                endSpan.TotalHours   < 0 || endSpan.TotalHours   > 24)
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Times must be between 00:00 and 24:00!</color>");
                return;
            }
            if (endSpan < startSpan)
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>End time must be >= start time!</color>");
                return;
            }

            string startStr = $"{startSpan.Hours:00}:{startSpan.Minutes:00}:00";
            string endStr = $"{endSpan.Hours:00}:{endSpan.Minutes:00}:00";

            switch (day)
            {
                case DayOfWeek.Monday:
                    Raidschedule.MondayStart.Value = startStr;
                    Raidschedule.MondayEnd.Value   = endStr;
                    break;
                case DayOfWeek.Tuesday:
                    Raidschedule.TuesdayStart.Value = startStr;
                    Raidschedule.TuesdayEnd.Value   = endStr;
                    break;
                case DayOfWeek.Wednesday:
                    Raidschedule.WednesdayStart.Value = startStr;
                    Raidschedule.WednesdayEnd.Value   = endStr;
                    break;
                case DayOfWeek.Thursday:
                    Raidschedule.ThursdayStart.Value = startStr;
                    Raidschedule.ThursdayEnd.Value   = endStr;
                    break;
                case DayOfWeek.Friday:
                    Raidschedule.FridayStart.Value   = startStr;
                    Raidschedule.FridayEnd.Value     = endStr;
                    break;
                case DayOfWeek.Saturday:
                    Raidschedule.SaturdayStart.Value = startStr;
                    Raidschedule.SaturdayEnd.Value   = endStr;
                    break;
                case DayOfWeek.Sunday:
                    Raidschedule.SundayStart.Value   = startStr;
                    Raidschedule.SundayEnd.Value     = endStr;
                    break;
            }

            Raidschedule.LoadFromConfig();
            RaidtimeManager.ClearSkipForTodayIfDayMatches(day);
            RaidtimeManager.ReloadFromConfig(true);

            ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#00FF00>{day} window</color> set to " +
                      $"<color=#FFFF00>{startStr} - {endStr}</color>. <color=#FFFFFF>Updated schedule loaded.</color>");
        }

        // ========================= GOLEM AUTOMATION COMMANDS =========================

        [Command("golemauto", "Manage Golem HP automation (on/off/start/check/clear).", adminOnly: true)]
        public void GolemAutoCommand(ChatCommandContext ctx, string subCmd, string optionalDateStr = "")
        {
            subCmd = subCmd.ToLower().Trim();
            switch (subCmd)
            {
                case "on":
                    Raidschedule.GolemAutomationEnabled.Value = true;
                    Raidschedule.LoadFromConfig();
                    GolemAutoManager.LoadConfig();
                    ctx.Reply("<color=#FFD700>[GolemAuto]</color> <color=#00FF00>Automation enabled.</color>");
                    break;

                case "off":
                    Raidschedule.GolemAutomationEnabled.Value = false;
                    Raidschedule.LoadFromConfig();
                    ctx.Reply("<color=#FFD700>[GolemAuto]</color> <color=#FF0000>Automation disabled.</color>");
                    break;

                case "start":
                    if (string.IsNullOrWhiteSpace(optionalDateStr))
                    {
                        var now = DateTime.Now;
                        GolemAutoManager.SetStartDate(now);
                        ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Day-0 set to now: {now}");
                    }
                    else
                    {
                        if (!DateTime.TryParse(optionalDateStr, out var customDate))
                        {
                            ctx.Reply("<color=#FFD700>[GolemAuto]</color> <color=#FF0000>Invalid date/time format!</color>");
                            return;
                        }
                        GolemAutoManager.SetStartDate(customDate);
                        ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Day-0 set to {customDate}");
                    }
                    break;

                case "check":
                    {
                        bool enabled = Raidschedule.GolemAutomationEnabled.Value;
                        if (!enabled)
                        {
                            ctx.Reply("<color=#FFD700>[GolemAuto]</color> Automation is OFF.");
                            return;
                        }

                        string startVal = Raidschedule.GolemStartDateString.Value;
                        if (string.IsNullOrWhiteSpace(startVal))
                        {
                            ctx.Reply("<color=#FFD700>[GolemAuto]</color> Automation is ON, but no start date set. Use '.golemauto start'");
                            return;
                        }
                        int dayIndex = GolemAutoManager.GetCurrentDay();
                        ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Automation is ON. StartDate={startVal}, currentDay={dayIndex}");
                        GolemAutoManagerCheckMapping(ctx, dayIndex);
                        break;
                    }
                case "clear":
                    GolemAutoManager.ClearStartDate();
                    ctx.Reply("<color=#FFD700>[GolemAuto]</color> Start date cleared. Day-0 is now undefined.");
                    break;

                default:
                    ctx.Reply("<color=#FFD700>[GolemAuto]</color> Usage: '.golemauto on/off/start/check/clear' [optional date]");
                    break;
            }
        }

        private void GolemAutoManagerCheckMapping(ChatCommandContext ctx, int dayIndex)
        {
            var pairs = Raidschedule.GolemDayToHealthMap.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            int maxDay = -1;
            var localMap = new Dictionary<int, ProjectM.SiegeWeaponHealth>();

            foreach (var pair in pairs)
            {
                var trimmed = pair.Trim();
                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex < 0) continue;
                if (!int.TryParse(trimmed.Substring(0, eqIndex).Trim(), out int d)) continue;
                if (!Enum.TryParse(trimmed.Substring(eqIndex+1).Trim(), true, out ProjectM.SiegeWeaponHealth h)) continue;
                localMap[d] = h;
                if (d > maxDay) maxDay = d;
            }

            if (dayIndex < 0)
            {
                ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Day index < 0, no HP mapping.");
                return;
            }

            if (localMap.TryGetValue(dayIndex, out var directHealth))
            {
                ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Day {dayIndex} => {directHealth} (via config map)");
            }
            else if (dayIndex > maxDay && maxDay >= 0)
            {
                var final = localMap[maxDay];
                ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Day {dayIndex} not mapped; clamped to day {maxDay} => {final}");
            }
            else
            {
                ctx.Reply($"<color=#FFD700>[GolemAuto]</color> Day {dayIndex} is not mapped; no HP change would occur.");
            }
        }
    }
}
