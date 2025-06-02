using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VampireCommandFramework;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Collections;
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
            LoggingHelper.Info($"User {ctx.User.CharacterName} used .reloadraidforge command.");
            Plugin.TriggerReloadFromCommand();
            ctx.Reply(ChatColors.SuccessText("All RaidForge configurations reload requested."));
        }

        [Command("raidon", "Manually turn raids ON.", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            try
            {
                LoggingHelper.Info($"User {ctx.User.CharacterName} used .raidon command.");
                RaidToggleSystem.EnableRaids(Plugin.BepInLogger);
                ctx.Reply(ChatColors.SuccessText("Raids manually turned ON."));
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error in RaidOn command", ex);
                ctx.Reply(ChatColors.ErrorText("Error enabling raids! Check logs."));
            }
        }

        [Command("raidoff", "Manually turn raids OFF.", adminOnly: true)]
        public void Raidoff(ChatCommandContext ctx)
        {
            try
            {
                LoggingHelper.Info($"User {ctx.User.CharacterName} used .raidoff command.");
                RaidToggleSystem.DisableRaids(Plugin.BepInLogger);
                ctx.Reply(ChatColors.WarningText("Raids manually turned OFF."));
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error in RaidOff command", ex);
                ctx.Reply(ChatColors.ErrorText("Error disabling raids! Check logs."));
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

        [Command("golemstartdate", "Sets the Golem Automation start date to the current time.", adminOnly: true)]
        public void SetGolemStartDate(ChatCommandContext ctx)
        {
            if (Plugin.Instance == null) { ctx.Reply(ChatColors.ErrorText("Error: Plugin instance not found.")); return; }
            try
            {
                LoggingHelper.Info($"User {ctx.User.CharacterName} used .golemstartdate command.");
                DateTime now = DateTime.Now;
                string formattedDate = now.ToString("yyyy-MM-dd HH:mm:ss");

                if (GolemAutomationConfig.SetServerStartDateAndSave(formattedDate))
                {
                    Plugin.TriggerGolemAutomationResetFromCommand();
                    GolemAutomationSystem.CheckAutomation();
                    ctx.Reply(ChatColors.SuccessText("Golem Automation start date set to: ") + ChatColors.InfoText(formattedDate));
                }
                else
                {
                    ctx.Reply(ChatColors.ErrorText("Failed to set and save Golem start date. Config may not be initialized. Check logs."));
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error in GolemStartDate command", ex);
                ctx.Reply(ChatColors.ErrorText("An error occurred while setting the Golem start date. Check logs."));
            }
        }

        [Command("golemcurrent", "Shows the current Golem health settings.", adminOnly: true)]
        public void GolemCurrent(ChatCommandContext ctx)
        {
            var currentActualHealthEnum = SiegeWeaponSystem.GetSiegeWeaponHealth(Plugin.BepInLogger);
            string actualHpStr = "N/A";
            if (currentActualHealthEnum.HasValue && GolemAutomationConfig.GolemHpEstimates.TryGetValue(currentActualHealthEnum.Value, out int approxHp))
            {
                actualHpStr = $"~{approxHp} HP ({currentActualHealthEnum.Value})";
            }
            else if (currentActualHealthEnum.HasValue)
            {
                actualHpStr = currentActualHealthEnum.Value.ToString();
            }
            ctx.Reply(ChatColors.InfoText($"Current Live Golem Health Setting: {ChatColors.AccentText(actualHpStr)}"));

            string manualLevelOverrideVal = string.IsNullOrEmpty(GolemAutomationConfig.ManualSiegeWeaponHealthOverride?.Value)
                                            ? "Not set"
                                            : GolemAutomationConfig.ManualSiegeWeaponHealthOverride.Value;

            ctx.Reply(ChatColors.InfoText($"Config - Manual Level Override: {ChatColors.HighlightText(manualLevelOverrideVal)}"));
            ctx.Reply(ChatColors.InfoText($"Config - Day-Based Automation Enabled: {ChatColors.HighlightText(GolemAutomationConfig.EnableDayBasedAutomation.Value.ToString())}"));

            if (!string.IsNullOrEmpty(manualLevelOverrideVal) && manualLevelOverrideVal.ToLowerInvariant() != "not set")
            {
                ctx.Reply(ChatColors.WarningText("Manual Level Override is active and takes precedence over day-based automation. Use '.golemauto' to clear."));
            }
            else if (!GolemAutomationConfig.EnableDayBasedAutomation.Value)
            {
                ctx.Reply(ChatColors.WarningText("Day-based automation is disabled and no manual level is set. Golem HP may use server default or last known setting."));
            }
        }

        [Command("golemsethp", "Manually sets and persists a Siege Golem health level. Usage: .golemsethp <LevelName>", adminOnly: true)]
        public void GolemSetHpByLevelName(ChatCommandContext ctx, string levelName)
        {
            LoggingHelper.Info($"User {ctx.User.CharacterName} used .golemsethp {levelName} command.");
            if (Enum.TryParse<SiegeWeaponHealth>(levelName, true, out var healthValue))
            {
                GolemAutomationConfig.SetManualSiegeWeaponHealthOverrideAndSave(healthValue);
                Plugin.TriggerGolemAutomationResetFromCommand();
                GolemAutomationSystem.CheckAutomation();

                string hpEstimate = "";
                if (GolemAutomationConfig.GolemHpEstimates.TryGetValue(healthValue, out int approxHpVal))
                {
                    hpEstimate = $" (~{approxHpVal} HP)";
                }
                ctx.Reply(ChatColors.SuccessText($"Persistent Golem health override set to Level: {ChatColors.AccentText(healthValue.ToString())}{hpEstimate}. Day-based automation is now overridden. Use '.golemauto' to clear."));
            }
            else
            {
                string validLevels = string.Join(", ", Enum.GetNames(typeof(SiegeWeaponHealth)));
                ctx.Reply(ChatColors.ErrorText($"Invalid health level name '{levelName}'. Valid levels are: {validLevels}"));
            }
        }

        [Command("golemauto", "Clears manual Golem health override. Day-based automation will apply if enabled.", adminOnly: true)]
        public void GolemSetAuto(ChatCommandContext ctx)
        {
            LoggingHelper.Info($"User {ctx.User.CharacterName} used .golemauto command.");
            GolemAutomationConfig.ClearManualSiegeWeaponHealthOverrideAndSave();
            Plugin.TriggerGolemAutomationResetFromCommand();
            GolemAutomationSystem.CheckAutomation();
            ctx.Reply(ChatColors.SuccessText("Manual Golem health override cleared. Automation will apply if enabled."));
        }

        [Command("golemlist", "Lists available Siege Golem health levels and estimated HP.", adminOnly: true)]
        public void GolemList(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.HighlightText("Siege Golem Health Levels (Estimates from Config):"));
            if (GolemAutomationConfig.GolemHpEstimates != null && GolemAutomationConfig.GolemHpEstimates.Any())
            {
                foreach (var kvp in GolemAutomationConfig.GolemHpEstimates.OrderBy(kv => kv.Value))
                { ctx.Reply(ChatColors.InfoText($"{kvp.Key}") + " = " + ChatColors.SuccessText($"~{kvp.Value} HP")); }
            }
            else
            {
                ctx.Reply(ChatColors.WarningText("HP estimates not available in GolemAutomationConfig."));
            }
        }


        [Command("raidtimer", "Shows raid vulnerability status for a player's clan/base. Usage: .raidtimer <PlayerNameOrSteamID>", adminOnly: false)]
        public void DisplayRaidVulnerabilityTimer(ChatCommandContext ctx, string playerNameOrSteamId)
        {
            LoggingHelper.Debug($"[.raidtimer] Command invoked by {ctx.User.CharacterName} for target: {playerNameOrSteamId}");
            try
            {
                EntityManager em = VWorld.EntityManager;

                if (!UserHelper.FindUserEntity(em, playerNameOrSteamId, out Entity targetUserEntity, out User targetUserData, out string foundCharacterName))
                {
                    ctx.Reply(ChatColors.Format($"Player '{playerNameOrSteamId}' not found.", ChatColors.Warning)); // Using Warning for not found
                    return;
                }
                string characterName = foundCharacterName;

                Entity targetUserClanEntity = Entity.Null;
                OwnershipCacheService.TryGetUserClan(targetUserEntity, out targetUserClanEntity);

                if (!Plugin.SystemsInitialized)
                {
                    ctx.Reply(ChatColors.ErrorText("Systems (and cache) not fully initialized. Please try again shortly."));
                    return;
                }

                string primaryPersistentKey = null;
                string ownerContextualName = characterName;

                if (targetUserClanEntity != Entity.Null && em.Exists(targetUserClanEntity) && em.HasComponent<ClanTeam>(targetUserClanEntity))
                {
                    primaryPersistentKey = PersistentKeyHelper.GetClanKey(em, targetUserClanEntity);
                    if (em.TryGetComponentData<ClanTeam>(targetUserClanEntity, out ClanTeam clanTeamData))
                        ownerContextualName = clanTeamData.Name.ToString() + " (Clan)";
                    else
                        ownerContextualName = "Clan " + targetUserClanEntity.ToString();
                }
                else
                {
                    primaryPersistentKey = PersistentKeyHelper.GetUserKey(targetUserData.PlatformId);
                }

                if (string.IsNullOrEmpty(primaryPersistentKey))
                {
                    ctx.Reply(ChatColors.ErrorText($"Could not determine a stable key for {ownerContextualName} to check offline status."));
                    return;
                }

                bool orpSystemEnabled = OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value;
                DateTime? overallOfflineStartTime = null;
                bool isUnderTimedOfflineState = false;
                bool isUnderDefaultBootOrp = false;

                if (orpSystemEnabled)
                {
                    if (OfflineGraceService.TryGetOfflineStartTime(primaryPersistentKey, out DateTime ost))
                    {
                        overallOfflineStartTime = ost;
                        isUnderTimedOfflineState = true;
                    }
                    else if (OfflineGraceService.IsUnderDefaultBootORP(primaryPersistentKey))
                    {
                        isUnderDefaultBootOrp = true;
                    }
                }

                List<(Entity HeartEntity, string CastleName)> ownedHearts = new List<(Entity, string)>();
                var heartOwnerCacheView = OwnershipCacheService.GetHeartToOwnerCacheView();
                foreach (var heartOwnerPair in heartOwnerCacheView)
                {
                    Entity currentHeartEntity = heartOwnerPair.Key;
                    Entity ownerOfHeartInCache = heartOwnerPair.Value;
                    if (!em.Exists(currentHeartEntity) || !em.Exists(ownerOfHeartInCache)) continue;

                    bool foundMatch = false;
                    if (ownerOfHeartInCache == targetUserEntity) foundMatch = true;
                    else if (targetUserClanEntity != Entity.Null)
                    {
                        if (OwnershipCacheService.TryGetUserClan(ownerOfHeartInCache, out Entity actualOwnerClan) && actualOwnerClan == targetUserClanEntity)
                            foundMatch = true;
                    }
                    if (foundMatch)
                    {
                        FixedString64Bytes castleNameFs = new FixedString64Bytes("-");
                        if (em.HasComponent<NameableInteractable>(currentHeartEntity))
                            castleNameFs = em.GetComponentData<NameableInteractable>(currentHeartEntity).Name;
                        string cName = castleNameFs.ToString();
                        ownedHearts.Add((currentHeartEntity, string.IsNullOrWhiteSpace(cName) || cName == "-" ? $"{ownerContextualName}'s base ({currentHeartEntity.Index})" : $"'{cName}'"));
                    }
                }

                if (!ownedHearts.Any())
                {
                    ctx.Reply(ChatColors.InfoText($"No Castle Hearts found for {ownerContextualName}.")); 
                    return;
                }

                ctx.Reply(ChatColors.HighlightText($"Status for {ownerContextualName}'s base(s):"));
                if (!orpSystemEnabled)
                {
                
                }

                foreach (var heartInfo in ownedHearts)
                {
                    Entity relevantCastleHeartEntity = heartInfo.HeartEntity;
                    string playerBaseName = heartInfo.CastleName;

                    if (em.HasComponent<CastleHeart>(relevantCastleHeartEntity) && em.GetComponentData<CastleHeart>(relevantCastleHeartEntity).IsSieged())
                    {
                        ctx.Reply(ChatColors.InfoText($"is {ChatColors.WarningText("IN BREACH")}."));
                        continue;
                    }

                    bool isActuallyDecaying = OfflineProtectionService.IsBaseDecaying(relevantCastleHeartEntity, em);
                    if (orpSystemEnabled && isActuallyDecaying)
                    {
                        ctx.Reply(ChatColors.InfoText($"has a base {ChatColors.WarningText("IN DECAY")}."));
                        continue;
                    }

                    if (orpSystemEnabled)
                    {
                        bool effectivelyAllOffline;

                        if (isUnderTimedOfflineState && overallOfflineStartTime.HasValue)
                        {
                            TimeSpan timeOffline = DateTime.UtcNow - overallOfflineStartTime.Value;
                            float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;

                            if (configuredGraceMinutes > 0 && timeOffline.TotalMinutes < configuredGraceMinutes)
                            {
                                TimeSpan remainingGraceTime = TimeSpan.FromMinutes(configuredGraceMinutes) - timeOffline;
                                string timeString = $"{(int)remainingGraceTime.TotalMinutes}m {remainingGraceTime.Seconds}s";
                                ctx.Reply(ChatColors.InfoText($"is {ChatColors.WarningText("RAIDABLE")} for another {ChatColors.AccentText(timeString)}."));
                            }
                            else
                            {
                                if (Plugin.ServerHasJustBooted)
                                {
                                    effectivelyAllOffline = true;
                                }
                                else
                                {
                                    effectivelyAllOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(relevantCastleHeartEntity, em);
                                }

                                if (effectivelyAllOffline)
                                {
                                    ctx.Reply(ChatColors.InfoText($"is {ChatColors.AccentText("OFFLINE PROTECTED")}."));
                                }
                                else
                                {
                                    ctx.Reply(ChatColors.InfoText($"Base is {ChatColors.WarningText("RAIDABLE")}."));
                                }
                            }
                        }
                        else if (isUnderDefaultBootOrp)
                        {
                            if (Plugin.ServerHasJustBooted)
                            {
                                effectivelyAllOffline = true;
                            }
                            else
                            {
                                effectivelyAllOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(relevantCastleHeartEntity, em);
                            }

                            if (effectivelyAllOffline)
                            {
                                ctx.Reply(ChatColors.InfoText($"is {ChatColors.AccentText("OFFLINE PROTECTED")}."));
                            }
                            else
                            {
                                ctx.Reply(ChatColors.InfoText($"Base is {ChatColors.WarningText("RAIDABLE")}."));
                            }
                        }
                        else
                        {
                            ctx.Reply(ChatColors.InfoText($"Base is {ChatColors.WarningText("RAIDABLE")}."));
                        }
                    }
                    else
                    {
                        ctx.Reply(ChatColors.InfoText($"Base is {ChatColors.WarningText("RAIDABLE")} {(isActuallyDecaying ? ChatColors.MutedText(" and is decaying") : "")}."));
                    }
                }
            }
            catch (Exception e)
            {
                LoggingHelper.Error("Error in .raidtimer command", e);
                ctx.Reply(ChatColors.ErrorText("An error occurred processing the command. Check server logs."));
            }
        }
    }
}
