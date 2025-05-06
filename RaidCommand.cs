using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using VampireCommandFramework;
using ProjectM;

namespace RaidForge
{
    public class RaidCommand
    {
        private static readonly Dictionary<SiegeWeaponHealth, int> GolemHpEstimates = new()
        {
            { SiegeWeaponHealth.VeryLow,    750  }, { SiegeWeaponHealth.Low,        1000 },
            { SiegeWeaponHealth.Normal,     1250 }, { SiegeWeaponHealth.High,       1750 },
            { SiegeWeaponHealth.VeryHigh,   2500 }, { SiegeWeaponHealth.MegaHigh,   3250 },
            { SiegeWeaponHealth.UltraHigh,  4000 }, { SiegeWeaponHealth.CrazyHigh,  5000 },
            { SiegeWeaponHealth.Max,        7500 },
        };

        [Command("reloadraidforge", "Reloads the RaidForge configuration from the config file.", adminOnly: true)]
        public void ReloadConfig(ChatCommandContext ctx)
        {
            if (RaidForgePlugin.Instance == null)
            {
                ctx.Reply("<color=#FF0000>Error: Plugin instance not found.</color>");
                return;
            }

            RaidForgePlugin.ReloadConfigAndSchedule();
            ctx.Reply("<color=#00FF00>Configuration reload requested for Raid & Golem Automation.</color>");
        }

        [Command("raidon", "Manually turn raids ON.", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            try
            {
                VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
                ctx.Reply("<color=#00FF00>Raids manually turned ON.</color>");
            }
            catch (Exception ex)
            {
                RaidForgePlugin.Logger?.LogError($"Error in RaidOn command: {ex.Message}\n{ex.StackTrace}");
                ctx.Reply("<color=#FF0000>Error enabling raids! Check logs.</color>");
            }
        }

        [Command("raidoff", "Manually turn raids OFF.", adminOnly: true)]
        public void Raidoff(ChatCommandContext ctx)
        {
            try
            {
                VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
                ctx.Reply("<color=#FF0000>Raids manually turned OFF.</color>");
            }
            catch (Exception ex)
            {
                RaidForgePlugin.Logger?.LogError($"Error in RaidOff command: {ex.Message}\n{ex.StackTrace}");
                ctx.Reply("<color=#FF0000>Error disabling raids! Check logs.</color>");
            }
        }

        [Command("raidt", "Shows time until the next scheduled raid window starts.", adminOnly: false)]
        public void RaidTime(ChatCommandContext ctx)
        {
            var schedule = RaidForgePlugin.GetSchedule();
            if (schedule == null) { ctx.Reply("<color=#FF0000>Error retrieving schedule configuration.</color>"); return; }
            if (!schedule.Any()) { ctx.Reply("<color=#FFFFFF>No raid schedule is configured.</color>"); return; }

            DateTime now = DateTime.Now;
            DateTime nextRaidStart = DateTime.MaxValue;
            bool currentlyActive = false;

            for (int daysAhead = 0; daysAhead <= 7; daysAhead++)
            {
                DateTime checkDate = now.AddDays(daysAhead); DayOfWeek checkDay = checkDate.DayOfWeek;
                foreach (var entry in schedule)
                {
                    DateTime potentialStart = checkDate.Date + entry.StartTime; DateTime potentialEnd = checkDate.Date + entry.EndTime;
                    if (entry.SpansMidnight) { if (entry.Day == checkDay) { potentialEnd = potentialEnd.AddDays(1); if (daysAhead == 0 && now >= potentialStart) currentlyActive = true; } else if ((entry.Day + 1 == (DayOfWeek)7 ? DayOfWeek.Sunday : entry.Day + 1) == checkDay) { potentialStart = potentialStart.AddDays(-1); if (daysAhead == 0 && now < potentialEnd) currentlyActive = true; } else { continue; } } else { if (entry.Day == checkDay) { if (daysAhead == 0 && now >= potentialStart && now < potentialEnd) currentlyActive = true; } else { continue; } }
                    if (potentialStart > now && potentialStart < nextRaidStart) { nextRaidStart = potentialStart; }
                }
            }

            if (currentlyActive) { ctx.Reply("<color=#00FF00>Raids are currently ACTIVE.</color>"); } else if (nextRaidStart != DateTime.MaxValue) { TimeSpan timeUntil = nextRaidStart - now; string timeString; if (timeUntil.TotalMinutes < 1) { timeString = "less than a minute"; } else if (timeUntil.TotalHours < 1) { timeString = $"{timeUntil.Minutes} minute{(timeUntil.Minutes > 1 ? "s" : "")}"; } else if (timeUntil.TotalDays < 1) { timeString = $"{timeUntil.Hours} hour{(timeUntil.Hours > 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes > 1 ? "s" : "")}"; } else { timeString = $"{timeUntil.Days} day{(timeUntil.Days > 1 ? "s" : "")}, {timeUntil.Hours} hour{(timeUntil.Hours > 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes > 1 ? "s" : "")}"; } ctx.Reply($"<color=#FFFFFF>Next raid starts in: {timeString}</color>"); ctx.Reply($"<color=#CCCCCC>({nextRaidStart:f})</color>"); } else { ctx.Reply("<color=#FFFFFF>No upcoming raids found in the schedule.</color>"); }
        }

        [Command("raiddays", "Shows the configured raid schedule for the week.", adminOnly: false)]
        public void RaidDays(ChatCommandContext ctx)
        {
            var schedule = RaidForgePlugin.GetSchedule();
            if (schedule == null) { ctx.Reply("<color=#FF0000>Error retrieving schedule.</color>"); return; }
            if (!schedule.Any()) { ctx.Reply("<color=#FFFFFF>No raid schedule is configured.</color>"); return; }

            var culture = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder("<color=#FFFF00>Weekly Schedule:</color>\n");
            var scheduleByDay = schedule.OrderBy(e => e.StartTime).GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.ToList());
            var daysOfWeek = Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToList();

            for (int i = 0; i < daysOfWeek.Count; i++)
            {
                DayOfWeek currentDay = daysOfWeek[i]; sb.Append($"<color=#FFFFFF>{currentDay.ToString().Substring(0, 3)}</color>: ");
                if (scheduleByDay.TryGetValue(currentDay, out var entriesForDay)) { List<string> dayScheduleStrings = new List<string>(); foreach (var entry in entriesForDay) { string startTimeStr = (DateTime.MinValue + entry.StartTime).ToString("h:mm tt", culture); string endTimeStr = (DateTime.MinValue + entry.EndTime).ToString("h:mm tt", culture); string endDayIndicator = ""; if (entry.SpansMidnight) { DayOfWeek endDay = (currentDay + 1 == (DayOfWeek)7 ? DayOfWeek.Sunday : currentDay + 1); endDayIndicator = $" ({endDay.ToString().Substring(0, 3)})"; } dayScheduleStrings.Add($"<color=#00FF00>{startTimeStr} - {endTimeStr}{endDayIndicator}</color>"); } sb.Append(string.Join(", ", dayScheduleStrings)); } else { sb.Append("<color=#FFA500>No Raids</color>"); }
                if (i < daysOfWeek.Count - 1) { sb.Append(" | "); }
            }
            ctx.Reply(sb.ToString());
        }

        [Command("golemstartdate", "Sets the Golem Automation start date to the current time.", adminOnly: true)]
        public void SetGolemStartDate(ChatCommandContext ctx)
        {
            if (GolemAutomationConfig.ServerStartDate == null || RaidForgePlugin.Instance?.Config == null)
            {
                ctx.Reply("<color=#FF0000>Error: Golem Automation config not initialized.</color>");
                return;
            }

            try
            {
                DateTime now = DateTime.Now;
                string formattedDate = now.ToString("yyyy-MM-dd HH:mm:ss");

                GolemAutomationConfig.ServerStartDate.Value = formattedDate;
                RaidForgePlugin.Instance.Config.Save();
                GolemAutomationConfig.ReloadAndParseAll();
                RaidForgePlugin.ResetGolemCheckDay();

                ctx.Reply($"<color=#00FF00>Golem Automation start date set to:</color> <color=#FFFFFF>{formattedDate}</color>");
                RaidForgePlugin.Logger?.LogInfo($"Golem Automation start date set to {formattedDate} by admin command.");
            }
            catch (Exception ex)
            {
                RaidForgePlugin.Logger?.LogError($"Error in GolemStartDate command: {ex}");
                ctx.Reply("<color=#FF0000>An error occurred while setting the Golem start date. Check logs.</color>");
            }
        }

        [Command("golemcurrent", "Shows the current SiegeWeaponHealth.", adminOnly: true)]
        public void GolemCurrent(ChatCommandContext ctx)
        {
            var current = SiegeWeaponManager.GetSiegeWeaponHealth(RaidForgePlugin.Logger);
            if (current == null) { ctx.Reply("<color=#FF0000>Could not retrieve SiegeWeaponHealth!</color>"); return; }
            if (GolemHpEstimates.TryGetValue(current.Value, out int approxHp)) { ctx.Reply($"<color=#FFFFFF>Current Golem Health = {current.Value} (~{approxHp} HP)</color>"); } else { ctx.Reply($"<color=#FFFFFF>Current Golem Health = {current.Value}</color>"); }
        }

        [Command("golemset", "Manually sets SiegeWeaponHealth. Usage: .golemset <LevelName>", adminOnly: true)]
        public void GolemSet(ChatCommandContext ctx, string levelName)
        {
            if (Enum.TryParse<SiegeWeaponHealth>(levelName, true, out var healthValue))
            {
                ApplySiegeHealth(ctx, healthValue);
            }
            else
            {
                string validLevels = string.Join(", ", Enum.GetNames(typeof(SiegeWeaponHealth)));
                ctx.Reply($"<color=#FF0000>Invalid health level '{levelName}'. Valid levels: {validLevels}</color>");
            }
        }

        [Command("golemlist", "Lists available Siege Golem health levels and estimated HP.", adminOnly: true)] 
        public void GolemList(ChatCommandContext ctx)
        {
            ctx.Reply("<color=#FFFF00>Siege Golem Health Levels:</color>");

            foreach (var kvp in GolemHpEstimates.OrderBy(kv => kv.Value))
            {
                ctx.Reply($"<color=#FFFFFF>{kvp.Key}</color> = <color=#00FF00>~{kvp.Value} HP</color>");
            }
        }

        private void ApplySiegeHealth(ChatCommandContext ctx, SiegeWeaponHealth healthValue)
        {
            bool ok = SiegeWeaponManager.SetSiegeWeaponHealth(healthValue, RaidForgePlugin.Logger);
            if (!ok) { ctx.Reply($"<color=#FF0000>Failed to set SiegeWeaponHealth to {healthValue}!</color>"); return; }
            if (GolemHpEstimates.TryGetValue(healthValue, out int approxHp)) { ctx.Reply($"<color=#00FF00>Golems manually updated to ~{approxHp} HP ({healthValue}).</color>"); } else { ctx.Reply($"<color=#00FF00>Golems manually updated: {healthValue}.</color>"); }
        }
    }
}
