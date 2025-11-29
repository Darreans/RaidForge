using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VampireCommandFramework;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Config;
using RaidForge.Utils;
using ProjectM.CastleBuilding;

namespace RaidForge.Commands
{
    public class RaidCommands
    {
        [Command("reloadraidforge", "Reloads all RaidForge configuration files.", adminOnly: true)]
        public void ReloadConfig(ChatCommandContext ctx)
        {
            if (Plugin.Instance == null)
            {
                ctx.Reply(ChatColors.ErrorText("Error: Plugin instance not found."));
                return;
            }
            Plugin.TriggerReloadFromCommand();
            ctx.Reply(ChatColors.SuccessText("All RaidForge configurations reload requested."));
        }

        [Command("raidon", "Manually turn raids ON.", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            try
            {
                RaidToggleSystem.EnableRaids();
                ctx.Reply(ChatColors.SuccessText("Raids manually turned ON."));
            }
            catch (Exception)
            {
                ctx.Reply(ChatColors.ErrorText("Error enabling raids!"));
            }
        }

        [Command("raidoff", "Manually turn raids OFF.", adminOnly: true)]
        public void Raidoff(ChatCommandContext ctx)
        {
            try
            {
                RaidToggleSystem.DisableRaids();
                ctx.Reply(ChatColors.WarningText("Raids manually turned OFF."));
            }
            catch (Exception)
            {
                ctx.Reply(ChatColors.ErrorText("Error disabling raids!"));
            }
        }

        [Command("raidt", "Shows time until the next scheduled raid window starts.", adminOnly: false)]
        public void RaidTime(ChatCommandContext ctx)
        {
            var schedule = Plugin.GetSchedule();
            if (schedule == null)
            {
                ctx.Reply(ChatColors.ErrorText("Error retrieving schedule configuration."));
                return;
            }
            if (!schedule.Any())
            {
                ctx.Reply(ChatColors.InfoText("No raid schedule is configured."));
                return;
            }

            DateTime now = DateTime.Now;
            DateTime nextRaidStart = DateTime.MaxValue;

            if (Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.SuccessText("Castle Raiding is currently ACTIVE according to schedule."));
            }

            for (int daysAhead = 0; daysAhead <= 7; daysAhead++)
            {
                DateTime checkDate = now.AddDays(daysAhead);
                DayOfWeek checkDay = checkDate.DayOfWeek;

                foreach (var entry in schedule)
                {
                    DateTime potentialStart;
                    if (entry.Day == checkDay)
                    {
                        potentialStart = checkDate.Date + entry.StartTime;
                        if (potentialStart > now && potentialStart < nextRaidStart)
                        {
                            nextRaidStart = potentialStart;
                        }
                    }
                    else if (entry.SpansMidnight && (DayOfWeek)(((int)entry.Day + 1) % 7) == checkDay)
                    {
                        potentialStart = checkDate.Date.AddDays(-1) + entry.StartTime;
                        if (potentialStart > now && potentialStart < nextRaidStart)
                        {
                            nextRaidStart = potentialStart;
                        }
                    }
                }
            }

            if (nextRaidStart != DateTime.MaxValue)
            {
                TimeSpan timeUntil = nextRaidStart - now;
                string timeString;
                if (timeUntil.TotalSeconds < 1 && timeUntil.TotalSeconds > -60) { timeString = "imminently (within the minute)"; }
                else if (timeUntil.TotalSeconds < 60) { timeString = $"{(int)timeUntil.TotalSeconds} second{(timeUntil.TotalSeconds != 1 ? "s" : "")}"; }
                else if (timeUntil.TotalMinutes < 60) { timeString = $"{(int)timeUntil.TotalMinutes} minute{((int)timeUntil.TotalMinutes != 1 ? "s" : "")}"; }
                else if (timeUntil.TotalHours < 24) { timeString = $"{(int)timeUntil.TotalHours} hour{((int)timeUntil.TotalHours != 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}"; }
                else { timeString = $"{(int)timeUntil.TotalDays} day{((int)timeUntil.TotalDays != 1 ? "s" : "")}, {timeUntil.Hours} hour{(timeUntil.Hours != 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}"; }

                ctx.Reply(ChatColors.InfoText($"Next scheduled raid window starts in: {ChatColors.AccentText(timeString)}"));
                ctx.Reply(ChatColors.MutedText($"({nextRaidStart:f})"));
            }
            else if (!Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.InfoText("No upcoming raids found in the schedule within the next 7 days."));
            }
        }

        [Command("raiddays", "Shows the configured raid schedule for the week.", adminOnly: false)]
        public void RaidDays(ChatCommandContext ctx)
        {
            var schedule = Plugin.GetSchedule();
            if (schedule == null) { ctx.Reply(ChatColors.ErrorText("Error retrieving schedule.")); return; }

            string configuredTimeZone = RaidConfig.RaidScheduleTimeZoneDisplayString?.Value?.Trim() ?? string.Empty;
            string headerTimeZoneSuffix = !string.IsNullOrWhiteSpace(configuredTimeZone) ? $" ({configuredTimeZone})" : "";
            ctx.Reply(ChatColors.HighlightText($"Raid Schedule{headerTimeZoneSuffix}:"));

            var culture = CultureInfo.InvariantCulture;
            var scheduleByDay = schedule
                                    .GroupBy(e => e.Day)
                                    .ToDictionary(
                                        g => g.Key,
                                        g => g.OrderBy(e => e.StartTime).ToList()
                                    );
            var daysOfWeekOrdered = Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>()
                                .OrderBy(d => (d == DayOfWeek.Sunday ? 7 : (int)d)).ToList();

            foreach (DayOfWeek currentDay in daysOfWeekOrdered)
            {
                string dayLineContent;
                if (scheduleByDay.TryGetValue(currentDay, out var entriesForDay) && entriesForDay.Any())
                {
                    List<string> dayScheduleStrings = new List<string>();
                    foreach (var entry in entriesForDay)
                    {
                        string startTimeStr = (DateTime.MinValue + entry.StartTime).ToString("h:mm tt", culture);
                        string endTimeStr;
                        if (entry.SpansMidnight)
                        {
                            DayOfWeek endDayAbbreviationDay = (DayOfWeek)(((int)entry.Day + 1) % 7);
                            endTimeStr = (entry.EndTime == TimeSpan.Zero) ?
                                ChatColors.SuccessText("Midnight") + ChatColors.MutedText($" (End of {entry.Day.ToString().Substring(0, 3)})") :
                                ChatColors.SuccessText((DateTime.MinValue + entry.EndTime).ToString("h:mm tt", culture)) + ChatColors.MutedText($" ({endDayAbbreviationDay.ToString().Substring(0, 3)})");
                        }
                        else
                        {
                            endTimeStr = ChatColors.SuccessText((DateTime.MinValue + entry.EndTime).ToString("h:mm tt", culture));
                        }
                        dayScheduleStrings.Add(ChatColors.SuccessText(startTimeStr) + ChatColors.MutedText(" - ") + endTimeStr);
                    }
                    dayLineContent = string.Join(ChatColors.MutedText(", "), dayScheduleStrings);
                }
                else
                {
                    dayLineContent = ChatColors.WarningText("No Raids Scheduled");
                }
                ctx.Reply(ChatColors.InfoText(currentDay.ToString()) + ": " + dayLineContent);
            }
        }

        [Command("raidstatus", "Shows raid vulnerability status for a player's clan/base. Usage: .raidstatus <PlayerName>", adminOnly: false)]
        public void DisplayRaidStatus(ChatCommandContext ctx, string playerName)
        {
            try
            {
                EntityManager em = VWorld.EntityManager;

                if (!UserHelper.FindUserEntity(em, playerName, out Entity targetUserEntity, out User targetUserData, out string foundCharacterName))
                {
                    ctx.Reply(ChatColors.Format($"Player '{playerName}' not found.", ChatColors.Warning));
                    return;
                }

                if (!Plugin.SystemsInitialized)
                {
                    ctx.Reply(ChatColors.ErrorText("Systems not fully initialized. Please try again shortly."));
                    return;
                }

                Entity targetUserClanEntity = targetUserData.ClanEntity._Entity;
                string persistentKey;
                string ownerContextualName;

                if (targetUserClanEntity.Exists() && em.HasComponent<ClanTeam>(targetUserClanEntity))
                {
                    persistentKey = PersistentKeyHelper.GetClanKey(em, targetUserClanEntity);
                    ownerContextualName = em.GetComponentData<ClanTeam>(targetUserClanEntity).Name.ToString() + " (Clan)";
                }
                else
                {
                    persistentKey = PersistentKeyHelper.GetUserKey(targetUserData.PlatformId);
                    ownerContextualName = foundCharacterName;
                }

                var ownedHearts = GetOwnedHearts(em, targetUserEntity, targetUserClanEntity, ownerContextualName);
                if (!ownedHearts.Any())
                {
                    ctx.Reply(ChatColors.InfoText($"No Castle Hearts found for {ownerContextualName}."));
                    return;
                }


                foreach (var (heartEntity, baseName) in ownedHearts)
                {
                    if (em.GetComponentData<CastleHeart>(heartEntity).IsSieged() || OfflineProtectionService.IsBaseDecaying(heartEntity, em))
                    {
                        ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.WarningText("RAIDABLE")}");
                        continue;
                    }

                    if (OptInRaidingConfig.EnableOptInRaiding.Value && !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                    {
                        if (OptInRaidService.IsOptedIn(persistentKey))
                        {
                            ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.WarningText("RAIDABLE")}");
                        }
                        else
                        {
                            ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.SuccessText("PROTECTED")}");
                        }
                        continue;
                    }

                    if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                    {
                        if (OfflineGraceService.TryGetOfflineStartTime(persistentKey, out DateTime offlineStartTime))
                        {
                            TimeSpan timeOffline = DateTime.UtcNow - offlineStartTime;
                            float graceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;

                            if (graceMinutes > 0 && timeOffline.TotalMinutes < graceMinutes)
                            {
                                ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.WarningText("RAIDABLE")}");
                            }
                            else if (OfflineProtectionService.AreAllDefendersActuallyOffline(heartEntity, em))
                            {
                                ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.SuccessText("PROTECTED")}");
                            }
                            else
                            {
                                ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.WarningText("RAIDABLE")}");
                            }
                        }
                        else if (OfflineGraceService.IsUnderDefaultBootORP(persistentKey) && OfflineProtectionService.AreAllDefendersActuallyOffline(heartEntity, em))
                        {
                            ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.SuccessText("PROTECTED")}");
                        }
                        else
                        {
                            ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.WarningText("RAIDABLE")}");
                        }
                        continue;
                    }

                    ctx.Reply($"{ChatColors.InfoText(baseName)}: {ChatColors.WarningText("RAIDABLE")}");
                }
            }
            catch (Exception)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred processing the command. Check server logs."));
            }
        }

        private List<(Entity HeartEntity, string CastleName)> GetOwnedHearts(EntityManager em, Entity user, Entity clan, string ownerName)
        {
            var ownedHearts = new List<(Entity HeartEntity, string CastleName)>();
            var allHearts = OwnershipCacheService.GetHeartToOwnerCacheView();

            foreach (var pair in allHearts)
            {
                if (!em.Exists(pair.Key) || !em.Exists(pair.Value)) continue;

                bool isMatch = false;
                if (pair.Value == user) isMatch = true;
                if (!isMatch && clan.Exists())
                {
                    if (OwnershipCacheService.TryGetUserClan(pair.Value, out Entity heartOwnerClan) && heartOwnerClan == clan)
                    {
                        isMatch = true;
                    }
                }

                if (isMatch)
                {
                    ownedHearts.Add((pair.Key, $"{ownerName}'s base"));
                }
            }

            if (ownedHearts.Count > 1)
            {
                for (int i = 0; i < ownedHearts.Count; i++)
                {
                    var heart = ownedHearts[i];
                    ownedHearts[i] = (heart.HeartEntity, $"{heart.CastleName} #{i + 1}");
                }
            }
            return ownedHearts;
        }
    }
}