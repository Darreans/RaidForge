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
using Unity.Collections;
using static RaidForge.Commands.CommandText;

namespace RaidForge.Commands
{
    public class RaidCommands
    {
        [Command("reloadraidforge", description: "Reloads RaidForge configuration files except startup-only command settings.", adminOnly: true)]
        public void ReloadConfig(ChatCommandContext ctx)
        {
            if (Plugin.Instance == null)
            {
                ctx.Reply(ChatColors.ErrorText("Error: Plugin instance not found."));
                return;
            }

            Plugin.TriggerReloadFromCommand();
            ctx.Reply(ChatColors.SuccessText(
                "RaidForge runtime configuration reload requested. Command settings require a full server restart."));
        }

        [Command("raidon", description: "Manually turn raids ON.", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            try
            {
                if (RaidSchedulingSystem.SetManualOverride(true))
                {
                    ctx.Reply(ChatColors.SuccessText("Raids manually forced ON."));
                    ctx.Reply(ChatColors.InfoText(
                        $"RaidForge schedule automation is paused until {CommandSettingsConfig.GetInvocation("raidauto")} or restart/reload."));
                }
                else
                {
                    ctx.Reply(ChatColors.ErrorText("Error enabling raids! Check server logs."));
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error enabling raids!"));
                LoggingHelper.Error("Error executing .raidon", ex);
            }
        }

        [Command("raidoff", description: "Manually turn raids OFF.", adminOnly: true)]
        public void Raidoff(ChatCommandContext ctx)
        {
            try
            {
                if (RaidSchedulingSystem.SetManualOverride(false))
                {
                    ctx.Reply(ChatColors.WarningText("Raids manually forced OFF."));
                    ctx.Reply(ChatColors.InfoText(
                        $"RaidForge schedule automation is paused until {CommandSettingsConfig.GetInvocation("raidauto")} or restart/reload."));
                }
                else
                {
                    ctx.Reply(ChatColors.ErrorText("Error disabling raids! Check server logs."));
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error disabling raids!"));
                LoggingHelper.Error("Error executing .raidoff", ex);
            }
        }

        [Command("raidauto", description: "Clears the manual raid override and resumes the RaidForge schedule.", adminOnly: true)]
        public void RaidAuto(ChatCommandContext ctx)
        {
            try
            {
                if (RaidSchedulingSystem.ClearManualOverride())
                {
                    ctx.Reply(ChatColors.SuccessText("Manual raid override cleared. RaidForge schedule automation resumed."));
                }
                else
                {
                    ctx.Reply(ChatColors.ErrorText("Error resuming RaidForge schedule automation! Check server logs."));
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error resuming RaidForge schedule automation!"));
                LoggingHelper.Error("Error executing .raidauto", ex);
            }
        }

        [Command("removeorp", description: "Removes offline protection from a player/clan until they log back in.", adminOnly: true)]
        public void RemoveOrp(ChatCommandContext ctx, string playerName)
        {
            if (!Plugin.SystemsInitialized || !VWorld.IsServerWorldReady())
            {
                ctx.Reply(ChatColors.ErrorText("Systems not fully initialized. Please try again shortly."));
                return;
            }

            var em = VWorld.EntityManager;

            if (!OwnerIdentityHelper.TryResolveFromPlayerName(
                em,
                playerName,
                out OwnerIdentity owner,
                out string error))
            {
                ctx.Reply(ChatColors.ErrorText(error));
                return;
            }

            string contextualName = owner.GetDisplayNameWithOwnerType();

            if (OfflineGraceService.AdminForceRemoveProtection(owner.PersistentKey))
            {
                ctx.Reply(ChatColors.SuccessText($"Offline Protection REMOVED for: {ChatColors.HighlightText(contextualName)}"));
                ctx.Reply(ChatColors.InfoText("They are now vulnerable until they log in and log back out."));
            }
            else
            {
                ctx.Reply(ChatColors.WarningText($"Could not find active offline protection for: {contextualName}. They may already be online or unprotected."));
            }
        }

        [Command("raidtime", shortHand: "raidt", description: "Shows time until the next scheduled raid window starts.", adminOnly: false)]
        public void RaidTimer(ChatCommandContext ctx)
        {
            ShowRaidTime(ctx);
        }

        private static void ShowRaidTime(ChatCommandContext ctx)
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

            DateTime now = RaidConfig.GetRaidScheduleNow();
            DateTime nextRaidStart = DateTime.MaxValue;

            if (Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.SuccessText("Castle Raiding is currently ACTIVE."));
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

                if (timeUntil.TotalSeconds < 1 && timeUntil.TotalSeconds > -60)
                {
                    timeString = "imminently (within the minute)";
                }
                else if (timeUntil.TotalSeconds < 60)
                {
                    timeString = $"{(int)timeUntil.TotalSeconds} second{(timeUntil.TotalSeconds != 1 ? "s" : "")}";
                }
                else if (timeUntil.TotalMinutes < 60)
                {
                    timeString = $"{(int)timeUntil.TotalMinutes} minute{((int)timeUntil.TotalMinutes != 1 ? "s" : "")}";
                }
                else if (timeUntil.TotalHours < 24)
                {
                    timeString = $"{(int)timeUntil.TotalHours} hour{((int)timeUntil.TotalHours != 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}";
                }
                else
                {
                    timeString = $"{(int)timeUntil.TotalDays} day{((int)timeUntil.TotalDays != 1 ? "s" : "")}, {timeUntil.Hours} hour{(timeUntil.Hours != 1 ? "s" : "")} and {timeUntil.Minutes} minute{(timeUntil.Minutes != 1 ? "s" : "")}";
                }

                ctx.Reply(ChatColors.InfoText($"Next scheduled raid window starts in: {ChatColors.AccentText(timeString)}"));
                ctx.Reply(ChatColors.InfoText(FormatRaidDisplayDateTime(nextRaidStart)));
            }
            else if (!Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.InfoText("No upcoming raids found in the schedule."));
            }
        }

        [Command("raiddays", shortHand: "raidd", description: "Shows the raid schedule for the week.", adminOnly: false)]
        public void RaidDays(ChatCommandContext ctx)
        {
            ShowRaidDays(ctx);
        }

        private static void ShowRaidDays(ChatCommandContext ctx)
        {
            var schedule = Plugin.GetSchedule();

            if (schedule == null)
            {
                ctx.Reply(ChatColors.ErrorText("Error retrieving schedule."));
                return;
            }

            string headerTimeZoneSuffix = GetRaidDisplaySuffix();

            ctx.Reply(ChatColors.HighlightText($"Raid Schedule{headerTimeZoneSuffix}:"));

            var culture = CultureInfo.InvariantCulture;
            TimeSpan displayOffset = TimeSpan.Zero;

            var scheduleByDay = schedule
                .Select(entry => BuildDisplayRaidScheduleEntry(entry, displayOffset))
                .GroupBy(e => e.DisplayStart.DayOfWeek)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(e => e.DisplayStart.TimeOfDay).ToList()
                );

            var daysOfWeekOrdered = Enum.GetValues(typeof(DayOfWeek))
                .Cast<DayOfWeek>()
                .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
                .ToList();

            foreach (DayOfWeek currentDay in daysOfWeekOrdered)
            {
                string dayLineContent;

                if (scheduleByDay.TryGetValue(currentDay, out var entriesForDay) && entriesForDay.Any())
                {
                    List<string> dayScheduleStrings = new List<string>();

                    foreach (var entry in entriesForDay)
                    {
                        string startTimeStr = entry.DisplayStart.ToString("h:mm tt", culture);
                        string endTimeStr = ChatColors.SuccessText(entry.DisplayEnd.ToString("h:mm tt", culture));

                        if (entry.DisplayEnd.DayOfWeek != entry.DisplayStart.DayOfWeek)
                        {
                            endTimeStr += ChatColors.MutedText($" ({entry.DisplayEnd.DayOfWeek.ToString().Substring(0, 3)})");
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

        private readonly struct DisplayRaidScheduleEntry
        {
            public readonly DateTime DisplayStart;
            public readonly DateTime DisplayEnd;

            public DisplayRaidScheduleEntry(DateTime displayStart, DateTime displayEnd)
            {
                DisplayStart = displayStart;
                DisplayEnd = displayEnd;
            }
        }

        private static DisplayRaidScheduleEntry BuildDisplayRaidScheduleEntry(RaidScheduleEntry entry, TimeSpan displayOffset)
        {
            DateTime weekStartSunday = new DateTime(2024, 1, 7);
            DateTime start = weekStartSunday.AddDays((int)entry.Day).Date + entry.StartTime;
            DateTime end = entry.SpansMidnight
                ? start.Date.AddDays(1) + entry.EndTime
                : start.Date + entry.EndTime;

            if (entry.EndTime == TimeSpan.Zero && entry.SpansMidnight)
            {
                end = start.Date.AddDays(1);
            }

            return new DisplayRaidScheduleEntry(start + displayOffset, end + displayOffset);
        }

        private static string FormatRaidDisplayDateTime(DateTime scheduleDateTime)
        {
            return $"{scheduleDateTime:f}{GetRaidDisplaySuffix()}";
        }

        private static TimeSpan GetRaidDisplayOffset()
        {
            return RaidConfig.GetRaidScheduleOffset();
        }

        private static string GetRaidDisplaySuffix()
        {
            string configuredTimeZone = RaidConfig.RaidScheduleTimeZoneDisplayString?.Value?.Trim() ?? string.Empty;
            double offsetHours = RaidConfig.RaidScheduleDisplayOffsetHours?.Value ?? 0.0;
            bool hasOffset = !double.IsNaN(offsetHours) &&
                !double.IsInfinity(offsetHours) &&
                Math.Abs(offsetHours) > 0.0001;

            if (!hasOffset)
            {
                return !string.IsNullOrWhiteSpace(configuredTimeZone) ? $" ({configuredTimeZone})" : "";
            }

            string offsetLabel = $"offset {offsetHours:+0.##;-0.##;0}h";
            return !string.IsNullOrWhiteSpace(configuredTimeZone)
                ? $" ({configuredTimeZone}, {offsetLabel})"
                : $" ({offsetLabel})";
        }

        [Command("raidstatus", shortHand: "raids", description: "Shows raid vulnerability status of the player/clan. Usage: .raidstatus <PlayerName>", adminOnly: false)]
        public void DisplayRaidStatus(ChatCommandContext ctx, string playerName)
        {
            ShowRaidStatus(ctx, playerName);
        }

        private void ShowRaidStatus(ChatCommandContext ctx, string playerName)
        {
            try
            {
                EntityManager em = VWorld.EntityManager;

                if (!Plugin.SystemsInitialized)
                {
                    ctx.Reply(ChatColors.ErrorText("Systems not fully initialized. Please try again shortly."));
                    return;
                }

                if (!OwnerIdentityHelper.TryResolveFromPlayerName(
                    em,
                    playerName,
                    out OwnerIdentity owner,
                    out string error))
                {
                    ctx.Reply(ChatColors.Format(error, ChatColors.Warning));
                    return;
                }

                string ownerContextualName = owner.GetDisplayNameWithOwnerType();

                var ownedHearts = GetOwnedHearts(
                    em,
                    owner.UserEntity,
                    owner.ClanEntity,
                    ownerContextualName
                );

                if (!ownedHearts.Any())
                {
                    ctx.Reply(ChatColors.InfoText($"No Castle Hearts found for {ownerContextualName}."));
                    return;
                }

                bool purchasedOrpActive = PurchasedOrpConfig.IsPurchasedOrpActive();
                bool purchasedOrpRaidDate = PurchasedOrpService.IsRaidDate(PurchasedOrpService.GetCurrentRaidDate());

                foreach (var (heartEntity, baseName) in ownedHearts)
                {
                    bool isCurrentlyRaiding = em.GetComponentData<CastleHeart>(heartEntity).IsSieged();
                    bool isDecaying = OfflineProtectionService.IsBaseDecaying(heartEntity, em);
                    bool showRaidingStatusForHeart = HasOnlineDefender(em, heartEntity);

                    if (isCurrentlyRaiding)
                    {
                        ReplyRaidStatusLine(ctx, baseName, ChatColors.WarningText("BREACHED"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                        continue;
                    }

                    if (isDecaying)
                    {
                        ReplyRaidStatusLine(ctx, baseName, ChatColors.WarningText("RAIDABLE"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                        continue;
                    }

                    if (OptInRaidingConfig.EnableOptInRaiding.Value && !OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                    {
                        if (OptInRaidService.IsOptedIn(owner.PersistentKey))
                        {
                            ReplyRaidStatusLine(ctx, baseName, ChatColors.WarningText("RAIDABLE"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                        }
                        else
                        {
                            ReplyRaidStatusLine(ctx, baseName, ChatColors.SuccessText("PROTECTED"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: false);
                        }

                        continue;
                    }

                    if (purchasedOrpActive)
                    {
                        bool hasPurchasedProtectionToday = purchasedOrpRaidDate && PurchasedOrpService.HasProtectionForToday(owner.PersistentKey);
                        if (!purchasedOrpRaidDate || hasPurchasedProtectionToday)
                        {
                            ReplyRaidStatusLine(
                                ctx,
                                baseName,
                                ChatColors.SuccessText("PROTECTED"),
                                isCurrentlyRaiding,
                                showRaidingStatusWhenEnabled: false);
                        }
                        else
                        {
                            ReplyRaidStatusLine(
                                ctx,
                                baseName,
                                ChatColors.WarningText("RAIDABLE"),
                                isCurrentlyRaiding,
                                showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                        }

                        continue;
                    }

                    if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
                    {
                        if (!OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday())
                        {
                            ReplyRaidStatusLine(
                                ctx,
                                baseName,
                                ChatColors.WarningText("RAIDABLE"),
                                isCurrentlyRaiding,
                                ChatColors.InfoText("ORP disabled for today"),
                                showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                            continue;
                        }

                        if (OfflineGraceService.TryGetOfflineStartTime(owner.PersistentKey, out DateTime offlineStartTime))
                        {
                            TimeSpan timeOffline = DateTime.UtcNow - offlineStartTime;
                            float graceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;

                            if (graceMinutes > 0 && timeOffline.TotalMinutes < graceMinutes)
                            {
                                TimeSpan timeRemaining = TimeSpan.FromMinutes(graceMinutes) - timeOffline;
                                string timeStr = $"{timeRemaining.Minutes}m {timeRemaining.Seconds}s";

                                ReplyRaidStatusLine(
                                    ctx,
                                    baseName,
                                    ChatColors.WarningText("RAIDABLE"),
                                    isCurrentlyRaiding,
                                    $"{ChatColors.InfoText("Vulnerability Period:")} {ChatColors.WarningText(timeStr)} {ChatColors.InfoText("remaining")}",
                                    showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                            }
                            else if (OfflineProtectionService.AreAllDefendersActuallyOffline(heartEntity, em))
                            {
                                ReplyRaidStatusLine(ctx, baseName, ChatColors.SuccessText("PROTECTED"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: false);
                            }
                            else
                            {
                                ReplyRaidStatusLine(ctx, baseName, ChatColors.WarningText("RAIDABLE"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                            }
                        }
                        else
                        {
                            ReplyRaidStatusLine(ctx, baseName, ChatColors.WarningText("RAIDABLE"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                        }

                        continue;
                    }

                    ReplyRaidStatusLine(ctx, baseName, ChatColors.WarningText("RAIDABLE"), isCurrentlyRaiding, showRaidingStatusWhenEnabled: showRaidingStatusForHeart);
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred processing the command. Check server logs."));
                LoggingHelper.Error("Error executing .raidstatus", ex);
            }
        }

        private static void ReplyRaidStatusLine(
            ChatCommandContext ctx,
            string baseName,
            string protectionStatus,
            bool isCurrentlyRaiding,
            string extraDetail = null,
            bool showRaidingStatusWhenEnabled = true)
        {
            string line = $"{ChatColors.InfoText(baseName)}: {protectionStatus}";

            if (!string.IsNullOrWhiteSpace(extraDetail))
            {
                line += $" {extraDetail}";
            }

            ctx.Reply(line);

            if (showRaidingStatusWhenEnabled &&
                RaidConfig.ShowCurrentRaidingStatusInRaidStatus?.Value == true)
            {
                ctx.Reply(isCurrentlyRaiding
                    ? ChatColors.ErrorText("Raiding")
                    : ChatColors.SuccessText("Not Raiding"));
            }
        }

        [Command("raidstatusreason", description: "Shows why a player/clan is or is not Offline Raid Protected. Usage: .raidstatusreason <PlayerName>", adminOnly: true)]
        public void DisplayRaidStatusReason(ChatCommandContext ctx, string playerName)
        {
            try
            {
                EntityManager em = VWorld.EntityManager;

                if (!Plugin.SystemsInitialized || !VWorld.IsServerWorldReady())
                {
                    ctx.Reply(ChatColors.ErrorText("Systems not fully initialized. Please try again shortly."));
                    return;
                }

                OwnerIdentity owner = default;
                bool resolvedLiveOwner = OwnerIdentityHelper.TryResolveFromPlayerName(
                    em,
                    playerName,
                    out owner,
                    out string resolveError);

                PlayerRegistryEntry registryEntry = null;
                string ownerPersistentKey;
                string ownerContextualName;

                if (resolvedLiveOwner)
                {
                    ownerPersistentKey = owner.PersistentKey;
                    ownerContextualName = owner.GetDisplayNameWithOwnerType();
                    PlayerRegistryService.TryGetEntryByUserKey(owner.UserKey, out registryEntry);
                }
                else
                {
                    var registryMatches = PlayerRegistryService.FindEntriesByCharacterName(playerName);

                    if (registryMatches.Count == 0)
                    {
                        ctx.Reply(ChatColors.ErrorText(resolveError));
                        ctx.Reply(ChatColors.WarningText("Player was also not found in RaidForge_PlayerRegistry.csv."));
                        return;
                    }

                    if (registryMatches.Count > 1)
                    {
                        ctx.Reply(ChatColors.WarningText($"Live player lookup failed: {resolveError}"));
                        ctx.Reply(ChatColors.WarningText("Multiple registry matches found. Use a more exact name:"));
                        foreach (var match in registryMatches.Take(5))
                        {
                            ctx.Reply(ChatColors.InfoText($"- {match.CharacterName} ({match.PlatformId}) owner={match.OwnerPersistentKey}"));
                        }

                        return;
                    }

                    registryEntry = registryMatches[0];
                    ownerPersistentKey = registryEntry.OwnerPersistentKey;
                    ownerContextualName = !string.IsNullOrWhiteSpace(registryEntry.ClanName) && registryEntry.OwnerType == "Clan"
                        ? $"{registryEntry.ClanName} (Clan)"
                        : registryEntry.CharacterName;

                    if (string.IsNullOrWhiteSpace(ownerPersistentKey))
                    {
                        ctx.Reply(ChatColors.WarningText($"Registry entry for '{registryEntry.CharacterName}' has no owner key, so ORP cannot be evaluated."));
                        return;
                    }
                }

                ctx.Reply(ChatColors.HighlightText($"Raid Status Reason: {ownerContextualName}"));
                ctx.Reply(StatusLine("Live user found", YesNoText(resolvedLiveOwner)));
                ctx.Reply(StatusLine("Owner key", ChatColors.AccentText(ownerPersistentKey ?? "missing")));
                ctx.Reply(StatusLine("ORP enabled", YesNoText(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value == true)));
                ctx.Reply(StatusLine("Purchased ORP active", YesNoText(PurchasedOrpConfig.IsPurchasedOrpActive())));
                ctx.Reply(StatusLine("ORP active today", YesNoText(OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday())));
                ctx.Reply(StatusLine("Raids currently enabled", YesNoText(RaidToggleSystem.AreRaidsEnabled())));

                ReplyRegistryStatus(ctx, registryEntry, ownerPersistentKey, resolvedLiveOwner ? owner.UserData.IsConnected : null);

                bool hasOfflineState = OfflineGraceService.TryGetOfflineStartTime(ownerPersistentKey, out DateTime offlineStartTimeUtc);
                ReplyOfflineStateStatus(ctx, hasOfflineState, offlineStartTimeUtc);

                bool shardBypassEnabled = ShardConfig.DisableOrpForShardHolders?.Value == true;
                bool ownerShardVulnerable = shardBypassEnabled && ShardVulnerabilityService.IsVulnerable(ownerPersistentKey);
                ReplyShardStatus(ctx, shardBypassEnabled, ownerShardVulnerable, ownerPersistentKey);

                var ownedHearts = GetOwnedHeartsByPersistentKey(em, ownerPersistentKey, ownerContextualName);
                if (!ownedHearts.Any())
                {
                    ctx.Reply(ChatColors.WarningText("No Castle Hearts found for this owner in the ownership cache."));
                    ctx.Reply(ChatColors.InfoText(
                        $"Run {CommandSettingsConfig.GetInvocation("raidrefreshcache")} if you expected hearts here."));
                    return;
                }

                foreach (var (heartEntity, baseName) in ownedHearts)
                {
                    ReplyHeartProtectionReason(
                        ctx,
                        em,
                        heartEntity,
                        baseName,
                        ownerPersistentKey,
                        hasOfflineState,
                        offlineStartTimeUtc,
                        ownerShardVulnerable);
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred processing the command. Check server logs."));
                LoggingHelper.Error("Error executing .raidstatusreason", ex);
            }
        }

        private List<(Entity HeartEntity, string CastleName)> GetOwnedHearts(EntityManager em, Entity user, Entity clan, string ownerName)
        {
            var ownedHearts = new List<(Entity HeartEntity, string CastleName)>();
            var allHearts = OwnershipCacheService.GetHeartToOwnerCacheView();

            foreach (var pair in allHearts)
            {
                if (!em.Exists(pair.Key) || !em.Exists(pair.Value))
                {
                    continue;
                }

                bool isMatch = false;

                if (pair.Value == user)
                {
                    isMatch = true;
                }

                if (!isMatch && clan != Entity.Null && clan.Exists() && em.Exists(clan))
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

        private static List<(Entity HeartEntity, string CastleName)> GetOwnedHeartsByPersistentKey(EntityManager em, string ownerPersistentKey, string ownerName)
        {
            var ownedHearts = new List<(Entity HeartEntity, string CastleName)>();

            if (string.IsNullOrWhiteSpace(ownerPersistentKey))
            {
                return ownedHearts;
            }

            foreach (var pair in OwnershipCacheService.GetHeartToOwnerCacheView())
            {
                if (!em.Exists(pair.Key) || !em.Exists(pair.Value))
                {
                    continue;
                }

                if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                    em,
                    pair.Value,
                    out OwnerIdentity heartOwner,
                    out _))
                {
                    continue;
                }

                if (heartOwner.PersistentKey == ownerPersistentKey)
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

        private static void ReplyHeartProtectionReason(
            ChatCommandContext ctx,
            EntityManager em,
            Entity heartEntity,
            string baseName,
            string ownerPersistentKey,
            bool hasOfflineState,
            DateTime offlineStartTimeUtc,
            bool ownerShardVulnerable)
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool orpEnabled = OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value == true;
            bool purchasedOrpActive = PurchasedOrpConfig.IsPurchasedOrpActive();
            bool purchasedProtectedToday = purchasedOrpActive && PurchasedOrpService.HasProtectionForToday(ownerPersistentKey);
            int purchasedDaysRemaining = purchasedOrpActive ? PurchasedOrpService.GetRemainingRaidDays(ownerPersistentKey) : 0;
            bool isRaidDate = PurchasedOrpService.IsRaidDate(PurchasedOrpService.GetCurrentRaidDate());
            bool baseDecaying = OfflineProtectionService.IsBaseDecaying(heartEntity, em);
            bool baseSieged = em.Exists(heartEntity) &&
                em.HasComponent<CastleHeart>(heartEntity) &&
                em.GetComponentData<CastleHeart>(heartEntity).IsSieged();
            bool allDefendersOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(heartEntity, em);
            float graceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes?.Value ?? 0f;
            bool inGrace = hasOfflineState &&
                graceMinutes > 0f &&
                (nowUtc - offlineStartTimeUtc).TotalMinutes < graceMinutes;

            string protectionStatus;
            string reason;

            if (baseDecaying)
            {
                protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                reason = "Castle Heart is decaying or has no fuel; decayed bases bypass ORP.";
            }
            else if (baseSieged)
            {
                protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                reason = "Castle Heart is already breached/sieged; breached bases bypass ORP.";
            }
            else if (ownerShardVulnerable)
            {
                protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                reason = "Owner is shard-vulnerable and shard ORP bypass is enabled.";
            }
            else if (!orpEnabled)
            {
                if (purchasedProtectedToday)
                {
                    protectionStatus = ChatColors.SuccessText("PROTECTED");
                    reason = $"Purchased ORP is active. This owner has {purchasedDaysRemaining} raid day(s) of protection.";
                }
                else if (purchasedOrpActive)
                {
                    if (!isRaidDate)
                    {
                        protectionStatus = ChatColors.InfoText("NO RAIDS TODAY");
                        reason = $"Today is not a configured raid date. This owner has {purchasedDaysRemaining} raid day(s) of protection.";
                    }
                    else
                    {
                        protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                        reason = "Purchased ORP is active, but this owner has no purchased protection remaining.";
                    }
                }
                else
                {
                    protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                    reason = "Offline Raid Protection is disabled in config.";
                }
            }
            else if (!OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday())
            {
                protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                reason = $"Offline Raid Protection is disabled for {DateTime.Now.DayOfWeek} in OfflineProtection.cfg.";
            }
            else if (!hasOfflineState)
            {
                protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                reason = "No offline timestamp exists for this owner. They may be online, never fully logged out after boot, or the boot registry did not restamp them.";
            }
            else if (inGrace)
            {
                TimeSpan remaining = TimeSpan.FromMinutes(graceMinutes) - (nowUtc - offlineStartTimeUtc);
                protectionStatus = ChatColors.WarningText("GRACE PERIOD");
                reason = $"Offline timestamp exists, but ORP grace has {FormatDuration(remaining)} remaining.";
            }
            else if (!allDefendersOffline)
            {
                protectionStatus = ChatColors.WarningText("NOT PROTECTED");
                reason = $"At least one defender is online: {BuildOnlineDefenderSummary(em, heartEntity)}.";
            }
            else
            {
                protectionStatus = ChatColors.SuccessText("PROTECTED");
                reason = "Offline timestamp is past grace and all defenders are offline.";
            }

            ctx.Reply($"{ChatColors.InfoText(baseName)}: {protectionStatus}");
            ctx.Reply(ChatColors.MutedText($"Reason: {reason}"));
            ctx.Reply(ChatColors.MutedText($"Defenders offline: {(allDefendersOffline ? "Yes" : "No")}"));
        }

        private static void ReplyRegistryStatus(ChatCommandContext ctx, PlayerRegistryEntry registryEntry, string ownerPersistentKey, bool? liveConnected)
        {
            if (registryEntry == null)
            {
                ctx.Reply(ChatColors.WarningText("Registry: no entry found for this live user's UserKey."));
                return;
            }

            string lastSeen = registryEntry.LastSeenUtcTicks > 0
                ? new DateTime(registryEntry.LastSeenUtcTicks, DateTimeKind.Utc).ToString("u", CultureInfo.InvariantCulture)
                : "unknown";
            string ownerMatch = registryEntry.OwnerPersistentKey == ownerPersistentKey ? "matches" : $"MISMATCH ({registryEntry.OwnerPersistentKey})";
            string liveConnectedText = liveConnected.HasValue ? $", LiveConnected={liveConnected.Value}" : string.Empty;

            ctx.Reply(ChatColors.InfoText($"Registry: {registryEntry.CharacterName}, IsConnected={registryEntry.IsConnected}{liveConnectedText}, LastSeen={lastSeen}, OwnerKey={ownerMatch}"));
        }

        private static void ReplyOfflineStateStatus(ChatCommandContext ctx, bool hasOfflineState, DateTime offlineStartTimeUtc)
        {
            if (!hasOfflineState)
            {
                ctx.Reply(ChatColors.WarningText("Offline state: missing"));
                return;
            }

            TimeSpan offlineFor = DateTime.UtcNow - offlineStartTimeUtc;
            ctx.Reply(ChatColors.InfoText($"Offline state: since {offlineStartTimeUtc.ToString("u", CultureInfo.InvariantCulture)} ({FormatDuration(offlineFor)} ago)"));
        }

        private static void ReplyShardStatus(ChatCommandContext ctx, bool shardBypassEnabled, bool ownerShardVulnerable, string ownerPersistentKey)
        {
            if (!shardBypassEnabled)
            {
                ctx.Reply(ChatColors.InfoText("Shard ORP bypass: disabled"));
                return;
            }

            if (!ownerShardVulnerable)
            {
                ctx.Reply(ChatColors.InfoText("Shard ORP bypass: enabled, no active shard vulnerability for owner"));
                return;
            }

            var shardEntries = ShardVulnerabilityService.GetEntriesForOwner(ownerPersistentKey);
            string shardSummary = shardEntries.Count == 0
                ? "active vulnerability present"
                : string.Join(", ", shardEntries.Select(entry => entry.ShardPrefabGuid.ToString(CultureInfo.InvariantCulture)));

            ctx.Reply(ChatColors.WarningText($"Shard ORP bypass: owner is vulnerable ({shardSummary})"));
        }

        private static bool HasOnlineDefender(EntityManager em, Entity heartEntity)
        {
            if (!em.Exists(heartEntity) || !em.HasComponent<UserOwner>(heartEntity))
            {
                return false;
            }

            Entity ownerUserEntity = em.GetComponentData<UserOwner>(heartEntity).Owner._Entity;
            if (!em.Exists(ownerUserEntity) || !em.HasComponent<User>(ownerUserEntity))
            {
                return false;
            }

            User ownerUser = em.GetComponentData<User>(ownerUserEntity);
            Entity ownerClanEntity = ownerUser.ClanEntity._Entity;

            if (ownerClanEntity != Entity.Null && em.Exists(ownerClanEntity) && em.HasComponent<ClanTeam>(ownerClanEntity))
            {
                return UserHelper.IsAnyClanMemberOnline(em, ownerClanEntity);
            }

            return ownerUser.IsConnected;
        }

        private static string BuildOnlineDefenderSummary(EntityManager em, Entity heartEntity)
        {
            if (!em.Exists(heartEntity) || !em.HasComponent<UserOwner>(heartEntity))
            {
                return "owner not found";
            }

            Entity ownerUserEntity = em.GetComponentData<UserOwner>(heartEntity).Owner._Entity;
            if (!em.Exists(ownerUserEntity) || !em.HasComponent<User>(ownerUserEntity))
            {
                return "owner user not found";
            }

            User ownerUser = em.GetComponentData<User>(ownerUserEntity);
            Entity ownerClanEntity = ownerUser.ClanEntity._Entity;

            if (ownerClanEntity != Entity.Null && em.Exists(ownerClanEntity) && em.HasComponent<ClanTeam>(ownerClanEntity))
            {
                var onlineNames = new List<string>();
                var clanMembers = new NativeList<Entity>(Allocator.Temp);

                try
                {
                    TeamUtility.GetClanMembers(em, ownerClanEntity, clanMembers);

                    foreach (Entity memberUserEntity in clanMembers)
                    {
                        if (!em.Exists(memberUserEntity) || !em.HasComponent<User>(memberUserEntity))
                        {
                            continue;
                        }

                        User memberUser = em.GetComponentData<User>(memberUserEntity);
                        if (memberUser.IsConnected)
                        {
                            onlineNames.Add(memberUser.CharacterName.ToString());
                        }
                    }
                }
                finally
                {
                    if (clanMembers.IsCreated)
                    {
                        clanMembers.Dispose();
                    }
                }

                return onlineNames.Count > 0 ? string.Join(", ", onlineNames) : "none";
            }

            return ownerUser.IsConnected ? ownerUser.CharacterName.ToString() : "none";
        }

    }
}
