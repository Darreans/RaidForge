using System;
using System.Collections.Generic;
using System.Linq;
using VampireCommandFramework;
using RaidForge.Config;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Entities;
using static RaidForge.Commands.CommandText;

namespace RaidForge.Commands
{
    public class OptInCommands
    {
        private const int OptListPageSize = 10;

        [Command("raidoptin", description: "Opts you and your clan into being raidable.", adminOnly: false)]
        public void OptInCommand(ChatCommandContext ctx)
        {
            if (TryRejectOptInSystemUnavailable(ctx))
            {
                return;
            }

            if (TryRejectRaidWindowStatusChange(ctx))
            {
                return;
            }

            var em = VWorld.EntityManager;

            if (!OwnerIdentityHelper.TryResolveFromCommandSender(
                em,
                ctx.Event.SenderUserEntity,
                out OwnerIdentity owner,
                out string error))
            {
                ctx.Reply(ChatColors.ErrorText(error));
                return;
            }

            bool defaultEveryoneOptedIn = OptInRaidingConfig.UseDefaultOptInMode;
            bool desiredOptIn = true;

            if (OptInRaidService.IsOptedIn(owner.PersistentKey))
            {
                ctx.Reply(ChatColors.WarningText("You are already opted in to raiding."));
                return;
            }

            if (TryRejectLockedStatusSwitch(ctx, owner.PersistentKey, desiredOptIn))
            {
                return;
            }

            OptInRaidService.OptIn(owner.PersistentKey, owner.ContextualName);
            RaidMapIconService.ProcessCleanup();

            int lockHours = OptInRaidingConfig.OptInLockDurationHours.Value;

            ctx.Reply(ChatColors.SuccessText("You have opted IN to raiding!"));

            if (defaultEveryoneOptedIn)
            {
                ctx.Reply(ChatColors.InfoText("Your opt-out exception was removed. Your base(s) are now raidable by the default server rule."));
            }
            else
            {
                ctx.Reply(ChatColors.InfoText($"Your base(s) are now raidable. You cannot opt-out for {lockHours} hour(s)."));
            }
        }

        [Command("raidoptout", description: "Opts you and your clan out of being raidable, if the time lock has expired.", adminOnly: false)]
        public void OptOutCommand(ChatCommandContext ctx)
        {
            if (TryRejectOptInSystemUnavailable(ctx))
            {
                return;
            }

            if (TryRejectRaidWindowStatusChange(ctx))
            {
                return;
            }

            var em = VWorld.EntityManager;

            if (!OwnerIdentityHelper.TryResolveFromCommandSender(
                em,
                ctx.Event.SenderUserEntity,
                out OwnerIdentity owner,
                out string error))
            {
                ctx.Reply(ChatColors.ErrorText(error));
                return;
            }

            bool defaultEveryoneOptedIn = OptInRaidingConfig.UseDefaultOptInMode;
            bool desiredOptIn = false;

            if (!OptInRaidService.IsOptedIn(owner.PersistentKey))
            {
                ctx.Reply(ChatColors.WarningText("You are already opted out of raiding."));
                return;
            }

            if (TryRejectLockedStatusSwitch(ctx, owner.PersistentKey, desiredOptIn))
            {
                return;
            }

            OptInRaidService.OptOut(owner.PersistentKey, owner.ContextualName);
            RaidMapIconService.ProcessCleanup();

            ctx.Reply(ChatColors.SuccessText("You have opted OUT of raiding. Your base(s) are now protected."));
        }

        [Command("raidoptstatus", description: "Checks your current opt-in raiding status.", adminOnly: false)]
        public void StatusCommand(ChatCommandContext ctx)
        {
            if (TryRejectOptInSystemUnavailable(ctx))
            {
                return;
            }

            var em = VWorld.EntityManager;

            if (!OwnerIdentityHelper.TryResolveFromCommandSender(
                em,
                ctx.Event.SenderUserEntity,
                out OwnerIdentity owner,
                out string error))
            {
                ctx.Reply(ChatColors.ErrorText(error));
                return;
            }

            bool defaultEveryoneOptedIn = OptInRaidingConfig.UseDefaultOptInMode;
            bool isOptedIn = OptInRaidService.IsOptedIn(owner.PersistentKey);
            bool hasManualState = OptInRaidService.TryGetManualStateTime(owner.PersistentKey, out DateTime manualStateTime);

            bool isForcedRaidDay =
                OptInScheduleConfig.EnableOptInSchedule.Value &&
                !OptInScheduleConfig.IsOptInSystemAllowedToday();

            if (isForcedRaidDay)
            {
                ctx.Reply(ChatColors.WarningText("Your raiding status is: RAIDABLE (Forced Raid Day)"));

                string optStatus = isOptedIn ? "Opted-In" : "Opted-Out";
                ctx.Reply(ChatColors.MutedText($"(Your saved status is {optStatus}, but the server schedule is overriding it today.)"));

                return;
            }

            if (isOptedIn)
            {
                ctx.Reply(ChatColors.WarningText(defaultEveryoneOptedIn
                    ? "Your raiding status is: OPTED-IN (Default Vulnerable)"
                    : "Your raiding status is: OPTED-IN (Vulnerable)"));

                if (!defaultEveryoneOptedIn && hasManualState)
                {
                    TimeSpan lockDuration = TimeSpan.FromHours(OptInRaidingConfig.OptInLockDurationHours.Value);
                    TimeSpan timeSinceOptIn = DateTime.UtcNow - manualStateTime;

                    if (timeSinceOptIn < lockDuration)
                    {
                        TimeSpan remainingTime = lockDuration - timeSinceOptIn;
                        ctx.Reply(ChatColors.InfoText($"You can opt-out in: {FormatDuration(remainingTime)}"));
                    }
                    else
                    {
                        ctx.Reply(ChatColors.SuccessText(
                            $"Your time lock has expired. You can use {CommandSettingsConfig.GetInvocation("raidoptout")} at any time."));
                    }
                }
            }
            else
            {
                ctx.Reply(ChatColors.SuccessText(defaultEveryoneOptedIn
                    ? "Your raiding status is: OPTED-OUT (Manual Exception / Protected)"
                    : "Your raiding status is: OPTED-OUT (Protected)"));

                if (defaultEveryoneOptedIn && hasManualState)
                {
                    TimeSpan lockDuration = TimeSpan.FromHours(OptInRaidingConfig.OptInLockDurationHours.Value);
                    TimeSpan timeSinceOptOut = DateTime.UtcNow - manualStateTime;

                    if (timeSinceOptOut < lockDuration)
                    {
                        TimeSpan remainingTime = lockDuration - timeSinceOptOut;
                        ctx.Reply(ChatColors.InfoText($"You can opt-in again in: {FormatDuration(remainingTime)}"));
                    }
                    else
                    {
                        ctx.Reply(ChatColors.SuccessText(
                            $"Your time lock has expired. You can use {CommandSettingsConfig.GetInvocation("raidoptin")} at any time."));
                    }
                }
            }
        }

        [Command("raidoptlist", description: "Lists manually opted-in raidable players/clans. Usage: .raidoptlist [page]", adminOnly: false)]
        public void OptListCommand(ChatCommandContext ctx, int page = 1)
        {
            if (TryRejectOptInSystemUnavailable(ctx))
            {
                return;
            }

            if (page < 1)
            {
                ctx.Reply(ChatColors.ErrorText(
                    $"Invalid page number. Usage: {CommandSettingsConfig.GetInvocation("raidoptlist")} 1"));
                return;
            }

            try
            {
                var em = VWorld.EntityManager;

                List<OptInListEntry> entries = BuildManualOptInEntries(em)
                    .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int totalCount = entries.Count;
                int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)OptListPageSize));

                if (page > totalPages)
                {
                    ctx.Reply(ChatColors.WarningText($"Page {page} does not exist. There are only {totalPages} page(s)."));
                    return;
                }

                bool defaultEveryoneOptedIn = OptInRaidingConfig.UseDefaultOptInMode;
                string listMode = defaultEveryoneOptedIn ? "Opt-Out" : "Opt-In";

                ctx.Reply(ChatColors.HighlightText($"=== Manual Raid {listMode} List | Page {page}/{totalPages} ==="));
                ctx.Reply(ChatColors.InfoText($"Total manually opted-{(defaultEveryoneOptedIn ? "out" : "in")} owners: ") + ChatColors.AccentText(totalCount.ToString()));

                if (totalCount == 0)
                {
                    ctx.Reply(ChatColors.WarningText($"No players or clans are manually opted {(defaultEveryoneOptedIn ? "out" : "in")}."));
                    ctx.Reply(ChatColors.HighlightText($"=== End {listMode} List ==="));
                    return;
                }

                int skip = (page - 1) * OptListPageSize;
                List<OptInListEntry> pageEntries = entries.Skip(skip).Take(OptListPageSize).ToList();

                for (int i = 0; i < pageEntries.Count; i++)
                {
                    OptInListEntry entry = pageEntries[i];
                    int displayIndex = skip + i + 1;

                    string ownerType = entry.IsClan ? "Clan" : "Player";
                    string optTimeText = entry.StateTimeUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");

                    ctx.Reply(
                        ChatColors.MutedText($"{displayIndex}. ") +
                        ChatColors.HighlightText(entry.DisplayName) +
                        ChatColors.MutedText($" ({ownerType}) ") +
                        ChatColors.InfoText($"- Opted {(defaultEveryoneOptedIn ? "out" : "in")}: {optTimeText}")
                    );
                }

                if (page < totalPages)
                {
                    ctx.Reply(ChatColors.InfoText("Next page: ") + ChatColors.AccentText(
                        $"{CommandSettingsConfig.GetInvocation("raidoptlist")} {page + 1}"));
                }

                ctx.Reply(ChatColors.HighlightText($"=== End {listMode} List ==="));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error reading opt-in list. Check server logs."));
                LoggingHelper.Error("Error executing .raidoptlist", ex);
            }
        }

        [Command("forceopt", description: "Admin command to force a player/clan's raiding status. Usage: .forceopt <PlayerName> <in|out>", adminOnly: true)]
        public void ForceOptCommand(ChatCommandContext ctx, string playerName, string status)
        {
            if (TryRejectOptInSystemUnavailable(ctx))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                ctx.Reply(ChatColors.ErrorText("Invalid status. Use 'in' or 'out'."));
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

            status = status.Trim().ToLowerInvariant();

            if (status == "in")
            {
                OptInRaidService.OptIn(owner.PersistentKey, owner.ContextualName);
                RaidMapIconService.ProcessCleanup();
                ctx.Reply(ChatColors.SuccessText($"Successfully forced '{owner.ContextualName}' to OPT-IN status."));
            }
            else if (status == "out")
            {
                OptInRaidService.OptOut(owner.PersistentKey, owner.ContextualName);
                RaidMapIconService.ProcessCleanup();
                ctx.Reply(ChatColors.SuccessText($"Successfully forced '{owner.ContextualName}' to OPT-OUT status. This bypasses any time locks."));
            }
            else
            {
                ctx.Reply(ChatColors.ErrorText("Invalid status. Use 'in' or 'out'."));
            }
        }

        private static List<OptInListEntry> BuildManualOptInEntries(EntityManager em)
        {
            var entries = new List<OptInListEntry>();

            foreach (OwnerIdentity owner in OwnerIdentityQueries.GetUniqueOwners(em, "OptCommands"))
            {
                if (!OptInRaidService.TryGetManualStateTime(owner.PersistentKey, out DateTime stateTimeUtc))
                {
                    continue;
                }

                entries.Add(new OptInListEntry(
                    owner.PersistentKey,
                    owner.GetDisplayNameWithOwnerType(),
                    owner.IsClan,
                    stateTimeUtc
                ));
            }

            return entries;
        }

        private readonly struct OptInListEntry
        {
            public readonly string OwnerKey;
            public readonly string DisplayName;
            public readonly bool IsClan;
            public readonly DateTime StateTimeUtc;

            public OptInListEntry(
                string ownerKey,
                string displayName,
                bool isClan,
                DateTime stateTimeUtc)
            {
                OwnerKey = ownerKey;
                DisplayName = displayName;
                IsClan = isClan;
                StateTimeUtc = stateTimeUtc;
            }
        }

        private static bool TryRejectRaidWindowStatusChange(ChatCommandContext ctx)
        {
            if (OptInRaidingConfig.BlockOptInChangesDuringRaidHours?.Value == true &&
                Plugin.IsAutoRaidCurrentlyActive)
            {
                ctx.Reply(ChatColors.ErrorText("You cannot change your raid opt status while a raid window is active."));
                return true;
            }

            return false;
        }

        private static bool TryRejectOptInSystemUnavailable(ChatCommandContext ctx)
        {
            if (!Plugin.SystemsInitialized || !VWorld.IsServerWorldReady())
            {
                ctx.Reply(ChatColors.ErrorText("Systems not fully initialized. Please try again shortly."));
                return true;
            }

            if (!OptInRaidingConfig.EnableOptInRaiding.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The opt-in raiding system is not enabled on this server."));
                return true;
            }

            if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The opt-in raiding system is disabled because Offline Raid Protection is enabled. Use one system or the other."));
                return true;
            }

            return false;
        }

        private static bool TryRejectLockedStatusSwitch(ChatCommandContext ctx, string ownerPersistentKey, bool desiredOptIn)
        {
            int lockHours = Math.Max(0, OptInRaidingConfig.OptInLockDurationHours?.Value ?? 0);
            if (lockHours <= 0)
            {
                return false;
            }

            bool defaultEveryoneOptedIn = OptInRaidingConfig.UseDefaultOptInMode;

            if (defaultEveryoneOptedIn == desiredOptIn)
            {
                if (!OptInRaidService.TryGetManualStateTime(ownerPersistentKey, out DateTime manualStateTime))
                {
                    return false;
                }

                TimeSpan timeSinceManualState = DateTime.UtcNow - manualStateTime;
                TimeSpan lockDuration = TimeSpan.FromHours(lockHours);

                if (timeSinceManualState < lockDuration)
                {
                    TimeSpan remainingTime = lockDuration - timeSinceManualState;
                    string commandName = desiredOptIn ? "opt-in" : "opt-out";
                    ctx.Reply(ChatColors.ErrorText($"You cannot {commandName} yet. Time remaining: {FormatDuration(remainingTime)}"));
                    return true;
                }
            }

            if (!defaultEveryoneOptedIn && !desiredOptIn)
            {
                if (!OptInRaidService.TryGetManualStateTime(ownerPersistentKey, out DateTime optInTime))
                {
                    ctx.Reply(ChatColors.WarningText("You are not opted in to raiding."));
                    return true;
                }

                TimeSpan timeSinceOptIn = DateTime.UtcNow - optInTime;
                TimeSpan lockDuration = TimeSpan.FromHours(lockHours);

                if (timeSinceOptIn < lockDuration)
                {
                    TimeSpan remainingTime = lockDuration - timeSinceOptIn;
                    ctx.Reply(ChatColors.ErrorText($"You cannot opt-out yet. Time remaining: {FormatDuration(remainingTime)}"));
                    return true;
                }
            }

            return false;
        }

    }
}
