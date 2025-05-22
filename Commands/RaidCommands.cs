using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
            if (schedule == null) { ctx.Reply(ChatColors.ErrorText("Error retrieving schedule configuration.")); return; }
            if (!schedule.Any()) { ctx.Reply(ChatColors.InfoText("No raid schedule is configured.")); return; }

            DateTime now = DateTime.Now;
            DateTime nextRaidStart = DateTime.MaxValue;

            if (Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.SuccessText("Castle Raiding is currently ACTIVE according to schedule."));
            }

            for (int daysAhead = 0; daysAhead <= 7; daysAhead++)
            {
                DateTime checkDate = now.AddDays(daysAhead); DayOfWeek checkDay = checkDate.DayOfWeek;
                foreach (var entry in schedule)
                {
                    DateTime potentialStart = checkDate.Date + entry.StartTime;
                    if (entry.Day == checkDay)
                    {
                        if (potentialStart > now && potentialStart < nextRaidStart) { nextRaidStart = potentialStart; }
                    }
                    else if (entry.SpansMidnight && (int)(entry.Day + 1) % 7 == (int)checkDay)
                    {
                        DateTime actualStartYesterday = checkDate.AddDays(-1).Date + entry.StartTime;
                        if (actualStartYesterday > now && actualStartYesterday < nextRaidStart) { nextRaidStart = actualStartYesterday; }
                    }
                }
            }

            if (nextRaidStart != DateTime.MaxValue)
            {
                TimeSpan timeUntil = nextRaidStart - now;
                string timeString;
                if (timeUntil.TotalSeconds < 1 && timeUntil.TotalSeconds > -60) { timeString = "imminently (within the minute)"; }
                else if (timeUntil.TotalMinutes < 1) { timeString = $"{(int)timeUntil.TotalSeconds} second{(timeUntil.TotalSeconds != 1 ? "s" : "")}"; }
                else if (timeUntil.TotalHours < 1) { timeString = $"{timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}"; }
                else if (timeUntil.TotalDays < 1) { timeString = $"{timeUntil.Hours} hour{(timeUntil.Hours != 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}"; }
                else { timeString = $"{timeUntil.Days} day{(timeUntil.Days != 1 ? "s" : "")}, {timeUntil.Hours} hour{(timeUntil.Hours != 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}"; }
                ctx.Reply(ChatColors.InfoText($"Next scheduled raid window starts in: {ChatColors.AccentText(timeString)}"));
                ctx.Reply(ChatColors.MutedText($"({nextRaidStart:f})"));
            }
            else if (!Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.InfoText("No upcoming raids found in the schedule."));
            }
        }

        [Command("raiddays", "Shows the configured raid schedule for the week.", adminOnly: false)]
        public void RaidDays(ChatCommandContext ctx)
        {
            var schedule = Plugin.GetSchedule();
            if (schedule == null) { ctx.Reply(ChatColors.ErrorText("Error retrieving schedule.")); return; }

            var culture = CultureInfo.InvariantCulture;
            var scheduleByDay = schedule.OrderBy(e => e.StartTime).GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.ToList());

            var daysOfWeekOrdered = new List<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            };

            StringBuilder sbPart1 = new StringBuilder(ChatColors.HighlightText("Raid Schedule (Mon - Wed):") + "\n");
            StringBuilder sbPart2 = new StringBuilder(ChatColors.HighlightText("Raid Schedule (Thu - Sun):") + "\n");

            foreach (DayOfWeek currentDay in daysOfWeekOrdered)
            {
                string dayLineContent = "";
                if (scheduleByDay.TryGetValue(currentDay, out var entriesForDay) && entriesForDay.Any())
                {
                    List<string> dayScheduleStrings = new List<string>();
                    foreach (var entry in entriesForDay.OrderBy(e => e.StartTime))
                    {
                        string startTimeStr = (DateTime.MinValue + entry.StartTime).ToString("h:mm tt", culture);
                        string endTimeStr;

                        if (entry.SpansMidnight)
                        {
                            DayOfWeek endDay = (DayOfWeek)(((int)currentDay + 1) % 7);
                            if (entry.EndTime == TimeSpan.Zero)
                            {
                                endTimeStr = ChatColors.SuccessText("Midnight") + ChatColors.MutedText($" (End of {currentDay.ToString().Substring(0, 3)})");
                            }
                            else
                            {
                                endTimeStr = ChatColors.SuccessText((DateTime.MinValue + entry.EndTime).ToString("h:mm tt", culture)) + ChatColors.MutedText($" ({endDay.ToString().Substring(0, 3)})");
                            }
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

                if (currentDay >= DayOfWeek.Monday && currentDay <= DayOfWeek.Wednesday)
                {
                    sbPart1.Append(ChatColors.InfoText(currentDay.ToString()) + ": " + dayLineContent + "\n");
                }
                else
                {
                    sbPart2.Append(ChatColors.InfoText(currentDay.ToString()) + ": " + dayLineContent + "\n");
                }
            }

            string finalMonWed = sbPart1.ToString().TrimEnd('\n');
            string finalThuSun = sbPart2.ToString().TrimEnd('\n');

            ctx.Reply(finalMonWed);
            ctx.Reply(finalThuSun);
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
            if (currentActualHealthEnum != null && GolemAutomationConfig.GolemHpEstimates.TryGetValue(currentActualHealthEnum.Value, out int approxHp))
            {
                actualHpStr = $"~{approxHp} HP ({currentActualHealthEnum.Value})";
            }
            else if (currentActualHealthEnum != null)
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

        [Command("golemsethp", "Manually sets and persists a Siege Golem health level (e.g. Max, Low, Normal). Usage: .golemsethp <LevelName>", adminOnly: true)]
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
                ctx.Reply(ChatColors.SuccessText($"Persistent Golem health override set to Level: {ChatColors.AccentText(healthValue.ToString())}{hpEstimate}. Day-based automation is now overridden by this manual setting. Use '.golemauto' to clear."));
            }
            else
            {
                string validLevels = string.Join(", ", Enum.GetNames(typeof(SiegeWeaponHealth)));
                ctx.Reply(ChatColors.ErrorText($"Invalid health level name '{levelName}'. Valid levels are: {validLevels}"));
            }
        }

        [Command("golemauto", "Clears any manual Golem health level override. Day-based automation will apply if enabled in config.", adminOnly: true)]
        public void GolemSetAuto(ChatCommandContext ctx)
        {
            LoggingHelper.Info($"User {ctx.User.CharacterName} used .golemauto command.");
            GolemAutomationConfig.ClearManualSiegeWeaponHealthOverrideAndSave();
            Plugin.TriggerGolemAutomationResetFromCommand();
            GolemAutomationSystem.CheckAutomation();

            ctx.Reply(ChatColors.SuccessText("Manual Golem health level override cleared. Golem health will now follow day-based automation (if enabled in config)."));
        }

        [Command("golemlist", "Lists available Siege Golem health levels and their estimated HP.", adminOnly: true)]
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

        [Command("raidtimer", "Shows raid vulnerability status for a player's clan. Usage: .raidtimer <PlayerNameOrSteamID>", adminOnly: false)]
        public void DisplayRaidVulnerabilityTimer(ChatCommandContext ctx, string playerNameOrSteamId)
        {
            LoggingHelper.Debug($"[.raidtimer] Command invoked by {ctx.User.CharacterName} for target: {playerNameOrSteamId}");
            try
            {
                EntityManager em = VWorld.EntityManager;

                if (!UserHelper.FindUserEntity(em, playerNameOrSteamId, out Entity userEntity, out User userData, out string foundCharacterName))
                {
                    LoggingHelper.Warning($"[.raidtimer] Player '{playerNameOrSteamId}' not found using UserHelper.");
                    ctx.Reply(ChatColors.WarningText($"Player '{playerNameOrSteamId}' not found."));
                    return;
                }
                LoggingHelper.Debug($"[.raidtimer] Found user via UserHelper: {foundCharacterName} ({userEntity})");
                string characterName = foundCharacterName;
                Entity clanEntity = userData.ClanEntity._Entity;
                LoggingHelper.Debug($"[.raidtimer] Target User: {characterName} ({userEntity}), Initial ClanEntity from User: {clanEntity}");

                Entity relevantCastleHeartEntity = Entity.Null;
                FixedString64Bytes castleName = new FixedString64Bytes("-");

                NativeArray<Entity> allHearts = default;
                try
                {
                    EntityQuery heartsQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CastleHeart>(), ComponentType.ReadOnly<UserOwner>());
                    allHearts = heartsQuery.ToEntityArray(Allocator.Temp);
                    LoggingHelper.Debug($"[.raidtimer] Found {allHearts.Length} total CastleHearts with UserOwner to check for {characterName}.");

                    foreach (Entity heartEntity in allHearts)
                    {
                        UserOwner heartOwnerData = em.GetComponentData<UserOwner>(heartEntity);
                        Entity ownerOfHeartUserEntity = heartOwnerData.Owner._Entity;

                        if (!em.Exists(ownerOfHeartUserEntity))
                        {
                            if (TroubleshootingConfig.EnableVerboseLogging.Value) LoggingHelper.Debug($"[.raidtimer] Heart {heartEntity} owner UserEntity {ownerOfHeartUserEntity} does not exist. Skipping.");
                            continue;
                        }
                        if (TroubleshootingConfig.EnableVerboseLogging.Value) LoggingHelper.Debug($"[.raidtimer] Checking Heart {heartEntity}, owned by UserEntity {ownerOfHeartUserEntity}.");

                        bool foundMatch = false;
                        if (ownerOfHeartUserEntity == userEntity)
                        {
                            if (TroubleshootingConfig.EnableVerboseLogging.Value) LoggingHelper.Debug($"[.raidtimer] Direct ownership match for Heart {heartEntity} by User {userEntity} ({characterName}).");
                            foundMatch = true;
                        }
                        else if (em.Exists(clanEntity) && em.HasComponent<User>(ownerOfHeartUserEntity))
                        {
                            User ownerOfHeartUserData = em.GetComponentData<User>(ownerOfHeartUserEntity);
                            Entity ownerOfHeartClanEntity = ownerOfHeartUserData.ClanEntity._Entity;

                            if (TroubleshootingConfig.EnableVerboseLogging.Value) LoggingHelper.Debug($"[.raidtimer] Heart {heartEntity} owner's clan: {ownerOfHeartClanEntity}. Target player's clan: {clanEntity}.");
                            if (em.Exists(ownerOfHeartClanEntity) && ownerOfHeartClanEntity == clanEntity)
                            {
                                if (TroubleshootingConfig.EnableVerboseLogging.Value) LoggingHelper.Debug($"[.raidtimer] Heart {heartEntity} matches clan {clanEntity}.");
                                foundMatch = true;
                            }
                        }
                        if (foundMatch)
                        {
                            relevantCastleHeartEntity = heartEntity;
                            if (em.HasComponent<NameableInteractable>(heartEntity))
                            {
                                castleName = em.GetComponentData<NameableInteractable>(heartEntity).Name;
                            }
                            LoggingHelper.Debug($"[.raidtimer] Found relevant CH: {relevantCastleHeartEntity} Name: {castleName} for {characterName}.");
                            break;
                        }
                    }
                }
                finally { if (allHearts.IsCreated) allHearts.Dispose(); }

                string playerBaseName = string.IsNullOrWhiteSpace(castleName.ToString()) || castleName.ToString() == "-" ? $"{characterName}'s base" : $"'{castleName.ToString()}'";

                if (relevantCastleHeartEntity == Entity.Null)
                {
                    string clanStatusMessage = em.Exists(clanEntity) ? $"or their clan" : "(and they are not in a clan)";
                    LoggingHelper.Warning($"[.raidtimer] No Castle Heart found associated with {characterName} {clanStatusMessage}.");
                    ctx.Reply(ChatColors.WarningText($"No Castle Heart found for {characterName} {clanStatusMessage}."));
                    return;
                }

                LoggingHelper.Debug($"[.raidtimer] Checking status for {playerBaseName} (CH: {relevantCastleHeartEntity}).");

                if (em.HasComponent<CastleHeart>(relevantCastleHeartEntity))
                {
                    CastleHeart chComponent = em.GetComponentData<CastleHeart>(relevantCastleHeartEntity);
                    if (chComponent.IsSieged())
                    {
                        LoggingHelper.Debug($"[.raidtimer] {playerBaseName} is IN BREACH.");
                        ctx.Reply(ChatColors.Format($"{playerBaseName} is {ChatColors.ErrorText("IN BREACH")} and vulnerable until the siege ends.", ChatColors.Warning));
                        return;
                    }
                }
                else
                {
                    LoggingHelper.Error($"[.raidtimer] Error: Could not read CastleHeart component for {playerBaseName}.");
                    ctx.Reply(ChatColors.ErrorText($"Error reading data for {playerBaseName}."));
                    return;
                }

                Entity keyForGraceCheck = em.Exists(clanEntity) && em.HasComponent<ClanTeam>(clanEntity) ? clanEntity : userEntity;
                LoggingHelper.Debug($"[.raidtimer] Key for grace check: {keyForGraceCheck}");

                if (OfflineGraceService.GetClanGracePeriodInfo(em, keyForGraceCheck, out TimeSpan remainingGraceTime, out FixedString64Bytes lastLogoffName, out _))
                {
                    string graceMsg = $"{playerBaseName} is {ChatColors.WarningText("RAIDABLE")} for another {ChatColors.AccentText($"{remainingGraceTime.Minutes}m {remainingGraceTime.Seconds}s")} (grace period from {lastLogoffName.ToString()}'s logout).";
                    LoggingHelper.Debug($"[.raidtimer] {playerBaseName} is in GRACE PERIOD. {graceMsg}");
                    ctx.Reply(graceMsg);
                    return;
                }

                LoggingHelper.Debug($"[.raidtimer] {playerBaseName} not in breach and not in grace period. Checking protection status...");
                if (OfflineProtectionService.ShouldProtectBase(relevantCastleHeartEntity, em, Plugin.BepInLogger))
                {
                    LoggingHelper.Debug($"[.raidtimer] {playerBaseName} is OFFLINE PROTECTED.");
                    ctx.Reply(ChatColors.SuccessText($"{playerBaseName} is currently OFFLINE PROTECTED."));
                }
                else
                {
                    LoggingHelper.Debug($"[.raidtimer] {playerBaseName} is RAIDABLE (online or protection not applicable).");
                    ctx.Reply(ChatColors.HighlightText($"{playerBaseName} is currently RAIDABLE."));
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