using System;
using VampireCommandFramework;
using Unity.Entities;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Services;
using RaidForge.Utils;
using ProjectM;

namespace RaidForge.Commands
{
    public class OptInCommands
    {
        [Command("raidoptin", "Opts you and your clan into being raidable.", adminOnly: false)]
        public void OptInCommand(ChatCommandContext ctx)
        {
            if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The Opt-In Raiding system is disabled because Offline Raid Protection is currently active."));
                return;
            }

            if (!OptInRaidingConfig.EnableOptInRaiding.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The opt-in raiding system is not enabled on this server."));
                return;
            }

            var em = VWorld.EntityManager;
            var userEntity = ctx.Event.SenderUserEntity;
            var user = em.GetComponentData<User>(userEntity);
            Entity clanEntity = user.ClanEntity._Entity;

            string persistentKey;
            string contextualName;

            if (clanEntity.Exists())
            {
                persistentKey = PersistentKeyHelper.GetClanKey(em, clanEntity);
                contextualName = em.GetComponentData<ClanTeam>(clanEntity).Name.ToString();
            }
            else
            {
                persistentKey = PersistentKeyHelper.GetUserKey(user.PlatformId);
                contextualName = user.CharacterName.ToString();
            }

            if (OptInRaidService.IsOptedIn(persistentKey))
            {
                ctx.Reply(ChatColors.WarningText($"You are already opted in to raiding."));
                return;
            }

            OptInRaidService.OptIn(persistentKey, contextualName);
            int lockHours = OptInRaidingConfig.OptInLockDurationHours.Value;
            ctx.Reply(ChatColors.SuccessText($"You have opted IN to raiding!"));
            ctx.Reply(ChatColors.InfoText($"Your base(s) are now raidable. You cannot opt-out for {lockHours} hour(s)."));
        }

        [Command("raidoptout", "Opts you and your clan out of being raidable, if the time lock has expired.", adminOnly: false)]
        public void OptOutCommand(ChatCommandContext ctx)
        {
            if (!OptInRaidingConfig.EnableOptInRaiding.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The opt-in raiding system is not enabled on this server."));
                return;
            }

            if (VWorld.GameBalanceSettings(out var balance) && VWorld.ZDateTime(out var dt))
            {
                if (balance.IsCastlePvPEnabled(dt))
                {
                    ctx.Reply(ChatColors.ErrorText("You cannot opt-out while a raid window is active."));
                    return;
                }
            }

            var em = VWorld.EntityManager;
            var userEntity = ctx.Event.SenderUserEntity;
            var user = em.GetComponentData<User>(userEntity);
            Entity clanEntity = user.ClanEntity._Entity;

            string persistentKey;
            string contextualName;

            if (clanEntity.Exists())
            {
                persistentKey = PersistentKeyHelper.GetClanKey(em, clanEntity);
                contextualName = em.GetComponentData<ClanTeam>(clanEntity).Name.ToString();
            }
            else
            {
                persistentKey = PersistentKeyHelper.GetUserKey(user.PlatformId);
                contextualName = user.CharacterName.ToString();
            }

            if (!OptInRaidService.TryGetOptInTime(persistentKey, out DateTime optInTime))
            {
                ctx.Reply(ChatColors.WarningText("You are not opted in to raiding."));
                return;
            }

            TimeSpan lockDuration = TimeSpan.FromHours(OptInRaidingConfig.OptInLockDurationHours.Value);
            TimeSpan timeSinceOptIn = DateTime.UtcNow - optInTime;

            if (timeSinceOptIn < lockDuration)
            {
                TimeSpan remainingTime = lockDuration - timeSinceOptIn;
                ctx.Reply(ChatColors.ErrorText($"You cannot opt-out yet. Time remaining: {remainingTime.Hours}h {remainingTime.Minutes}m"));
                return;
            }

            OptInRaidService.OptOut(persistentKey, contextualName);
            ctx.Reply(ChatColors.SuccessText("You have opted OUT of raiding. Your base(s) are now protected."));
        }

        [Command("raidoptstatus", "Checks your current opt-in raiding status.", adminOnly: false)]
        public void StatusCommand(ChatCommandContext ctx)
        {
            if (!OptInRaidingConfig.EnableOptInRaiding.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The opt-in raiding system is not enabled on this server."));
                return;
            }

            var em = VWorld.EntityManager;
            var userEntity = ctx.Event.SenderUserEntity;
            var user = em.GetComponentData<User>(userEntity);
            Entity clanEntity = user.ClanEntity._Entity;
            string persistentKey = clanEntity.Exists() ? PersistentKeyHelper.GetClanKey(em, clanEntity) : PersistentKeyHelper.GetUserKey(user.PlatformId);

            bool isOptedIn = OptInRaidService.TryGetOptInTime(persistentKey, out DateTime optInTime);

            bool isForcedRaidDay = OptInScheduleConfig.EnableOptInSchedule.Value && !OptInScheduleConfig.IsOptInSystemAllowedToday();

            if (isForcedRaidDay)
            {
                ctx.Reply(ChatColors.WarningText("Your raiding status is: RAIDABLE (Forced Raid Day)"));
                string optStatus = isOptedIn ? "Opted-In" : "Opted-Out";
                ctx.Reply(ChatColors.MutedText($"(Your saved status is {optStatus}, but the server schedule is overriding it today.)"));
                return;
            }

            if (isOptedIn)
            {
                ctx.Reply(ChatColors.WarningText("Your raiding status is: OPTED-IN (Vulnerable)"));
                TimeSpan lockDuration = TimeSpan.FromHours(OptInRaidingConfig.OptInLockDurationHours.Value);
                TimeSpan timeSinceOptIn = DateTime.UtcNow - optInTime;

                if (timeSinceOptIn < lockDuration)
                {
                    TimeSpan remainingTime = lockDuration - timeSinceOptIn;
                    ctx.Reply(ChatColors.InfoText($"You can opt-out in: {remainingTime.Hours}h {remainingTime.Minutes}m"));
                }
                else
                {
                    ctx.Reply(ChatColors.SuccessText("Your time lock has expired. You can use .raidoptout at any time."));
                }
            }
            else
            {
                ctx.Reply(ChatColors.SuccessText("Your raiding status is: OPTED-OUT (Protected)"));
            }
        }

        [Command("forceopt", "Admin command to force a player/clan's raiding status. Usage: .forceopt <PlayerName> <in|out>", adminOnly: true)]
        public void ForceOptCommand(ChatCommandContext ctx, string playerName, string status)
        {
            if (!OptInRaidingConfig.EnableOptInRaiding.Value)
            {
                ctx.Reply(ChatColors.ErrorText("The opt-in raiding system is not enabled on this server."));
                return;
            }

            var em = VWorld.EntityManager;
            if (!UserHelper.FindUserEntity(em, playerName, out _, out User targetUserData, out _))
            {
                ctx.Reply(ChatColors.ErrorText($"Player '{playerName}' not found."));
                return;
            }

            Entity targetClanEntity = targetUserData.ClanEntity._Entity;
            string persistentKey;
            string contextualName;

            if (targetClanEntity.Exists())
            {
                persistentKey = PersistentKeyHelper.GetClanKey(em, targetClanEntity);
                contextualName = em.GetComponentData<ClanTeam>(targetClanEntity).Name.ToString();
            }
            else
            {
                persistentKey = PersistentKeyHelper.GetUserKey(targetUserData.PlatformId);
                contextualName = targetUserData.CharacterName.ToString();
            }

            status = status.ToLowerInvariant();
            if (status == "in")
            {
                OptInRaidService.OptIn(persistentKey, contextualName);
                ctx.Reply(ChatColors.SuccessText($"Successfully forced '{contextualName}' to OPT-IN status."));
            }
            else if (status == "out")
            {
                OptInRaidService.OptOut(persistentKey, contextualName);
                ctx.Reply(ChatColors.SuccessText($"Successfully forced '{contextualName}' to OPT-OUT status. This bypasses any time locks."));
            }
            else
            {
                ctx.Reply(ChatColors.ErrorText("Invalid status. Use 'in' or 'out'."));
            }
        }
    }
}