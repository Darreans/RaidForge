using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VampireCommandFramework;
using ProjectM;
using ProjectM.Network;
using ProjectM.CastleBuilding;
using RaidForge.Config;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;
using Unity.Entities;
using static RaidForge.Commands.CommandText;

namespace RaidForge.Commands
{
    public class RaidForgeInfoCommands
    {
        [Command("raidforge", description: "RaidForge admin tools. Usage: .raidforge ?, .raidforge status, .raidforge cache", adminOnly: true)]
        public void RaidForgeCommand(ChatCommandContext ctx, string action = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(action) || action.Trim() == "?")
                {
                    ShowHelp(ctx);
                    return;
                }

                action = action.Trim().ToLowerInvariant();

                switch (action)
                {
                    case "status":
                    case "stats":
                        ShowStatus(ctx);
                        break;

                    case "cache":
                    case "counts":
                        ShowCacheCounts(ctx);
                        break;

                    default:
                        ctx.Reply(ChatColors.ErrorText(
                            $"Unknown RaidForge action '{action}'. Use '{CommandSettingsConfig.GetInvocation("raidforge")} ?' for options."));
                        break;
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error running RaidForge admin command. Check server logs."));
                LoggingHelper.Error("Error executing .raidforge command.", ex);
            }
        }

        private static void ShowHelp(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.HighlightText("RaidForge Admin Command Help"));
            ctx.Reply(ChatColors.InfoText($"{CommandSettingsConfig.GetInvocation("raidforge")} status") + ChatColors.MutedText(" - Shows current RaidForge system status and key counts."));
            ctx.Reply(ChatColors.InfoText($"{CommandSettingsConfig.GetInvocation("raidforge")} cache") + ChatColors.MutedText(" - Shows cache/world count details."));
            ctx.Reply(ChatColors.InfoText(CommandSettingsConfig.GetInvocation("raidrefreshcache")) + ChatColors.MutedText(" - Rebuilds ownership cache."));
            ctx.Reply(ChatColors.InfoText(CommandSettingsConfig.GetInvocation("raidauto")) + ChatColors.MutedText(" - Clears manual raid override and resumes the schedule."));
            ctx.Reply(ChatColors.InfoText($"{CommandSettingsConfig.GetInvocation("raidconfigview")} ?") + ChatColors.MutedText(" - Shows numbered config sections."));
            ctx.Reply(ChatColors.InfoText(CommandSettingsConfig.GetInvocation("raidconfigviewall")) + ChatColors.MutedText(" - Shows all loaded config sections."));
            ctx.Reply(ChatColors.HighlightText("End RaidForge Help"));
        }

        private static void ShowStatus(ChatCommandContext ctx)
        {
            EntityManager em = VWorld.EntityManager;

            var cacheCounts = BuildCacheCounts(em);
            var optInEntries = BuildOptInEntries(em);
            var orpEntries = BuildOrpEntries(em);

            ctx.Reply(ChatColors.HighlightText("RaidForge Status"));

            ctx.Reply(ConfigLine("Systems Initialized", YesNoText(Plugin.SystemsInitialized)));
            ctx.Reply(ConfigLine("Server Has Just Booted", YesNoText(Plugin.ServerHasJustBooted)));
            ctx.Reply(ConfigLine("Raid Control Mode", GetRaidControlModeText()));
            ctx.Reply(ConfigLine("Raids Currently Enabled", YesNoText(RaidToggleSystem.AreRaidsEnabled())));
            ctx.Reply(ConfigLine("Effective Raid State", YesNoText(Plugin.IsAutoRaidCurrentlyActive)));

            ctx.Reply(ConfigLine("Offline Protection Enabled", YesNoText(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false)));
            ctx.Reply(ConfigLine("Offline Protection Active Today", YesNoText(OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday())));
            ctx.Reply(ConfigLine("Opt-In Raiding Enabled", YesNoText(OptInRaidingConfig.EnableOptInRaiding?.Value ?? false)));
            ctx.Reply(ConfigLine("Opt-In Raiding Active", YesNoText(
                (OptInRaidingConfig.EnableOptInRaiding?.Value ?? false) &&
                !(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false))));
            ctx.Reply(ConfigLine("Shard ORP Bypass Enabled", YesNoText(ShardConfig.DisableOrpForShardHolders?.Value ?? false)));
            ctx.Reply(ConfigLine("Weapon Raiding Enabled", YesNoText(WeaponRaidingConfig.EnableWeaponRaiding?.Value ?? false)));
            ctx.Reply(ConfigLine("TNT Raiding Enabled", YesNoText(TntRaidingConfig.EnableTntRaiding?.Value ?? false)));
            ctx.Reply(ConfigLine(
                "TNT Normal Damage (T01 / T02)",
                $"{(TntRaidingConfig.T01NormalDamagePercent?.Value ?? 100f).ToString(CultureInfo.InvariantCulture)}% / " +
                $"{(TntRaidingConfig.T02NormalDamagePercent?.Value ?? 100f).ToString(CultureInfo.InvariantCulture)}%"));
            ctx.Reply(ConfigLine(
                "TNT Castle Damage (T01 / T02)",
                $"{(TntRaidingConfig.T01CastleWallDamagePercent?.Value ?? 100f).ToString(CultureInfo.InvariantCulture)}% / " +
                $"{(TntRaidingConfig.T02CastleWallDamagePercent?.Value ?? 100f).ToString(CultureInfo.InvariantCulture)}%"));

            ctx.Reply(ChatColors.AccentText("[World / Cache Counts]"));
            ctx.Reply(ConfigLine("Users In World", CountText(cacheCounts.UserCount)));
            ctx.Reply(ConfigLine("Clans In World", CountText(cacheCounts.ClanCount)));
            ctx.Reply(ConfigLine("Castle Hearts In World", CountText(cacheCounts.CastleHeartCount)));
            ctx.Reply(ConfigLine("Heart Ownership Cache Entries", CountText(cacheCounts.HeartOwnerCacheCount)));

            ctx.Reply(ChatColors.AccentText("[RaidForge State Counts]"));
            ctx.Reply(ConfigLine("Manual Opt-In Owners", CountText(optInEntries.Count)));
            ctx.Reply(ConfigLine("ORP Owners Known", CountText(orpEntries.Count)));
            ctx.Reply(ConfigLine("Timed ORP Owners", CountText(orpEntries.Count(e => e.HasTimedOfflineState))));

            ctx.Reply(ChatColors.HighlightText("End RaidForge Status"));
        }

        private static string GetRaidControlModeText()
        {
            if (!RaidSchedulingSystem.HasManualOverride)
            {
                return ChatColors.SuccessText("Schedule");
            }

            return RaidSchedulingSystem.ManualOverrideValue == true
                ? ChatColors.WarningText("Manual ON")
                : ChatColors.WarningText("Manual OFF");
        }

        private static void ShowCacheCounts(ChatCommandContext ctx)
        {
            EntityManager em = VWorld.EntityManager;

            var cacheCounts = BuildCacheCounts(em);
            var optInEntries = BuildOptInEntries(em);
            var orpEntries = BuildOrpEntries(em);

            ctx.Reply(ChatColors.HighlightText("RaidForge Cache / Count View "));

            ctx.Reply(ConfigLine("Users In World", CountText(cacheCounts.UserCount)));
            ctx.Reply(ConfigLine("Clans In World", CountText(cacheCounts.ClanCount)));
            ctx.Reply(ConfigLine("Castle Hearts In World", CountText(cacheCounts.CastleHeartCount)));
            ctx.Reply(ConfigLine("Heart Ownership Cache Entries", CountText(cacheCounts.HeartOwnerCacheCount)));

            ctx.Reply(ConfigLine("Manual Opt-In Owners", CountText(optInEntries.Count)));
            ctx.Reply(ConfigLine("ORP Owners Known", CountText(orpEntries.Count)));
            ctx.Reply(ConfigLine("Timed ORP Owners", CountText(orpEntries.Count(e => e.HasTimedOfflineState))));

            ctx.Reply(ChatColors.InfoText("Tip: use ") + ChatColors.AccentText(CommandSettingsConfig.GetInvocation("raidrefreshcache")) + ChatColors.InfoText(" to rebuild ownership cache."));

            ctx.Reply(ChatColors.HighlightText("End Cache / Count View"));
        }

        private static CacheCountSnapshot BuildCacheCounts(EntityManager em)
        {
            int userCount = CountEntitiesWithComponent<User>(em);
            int clanCount = CountEntitiesWithComponent<ClanTeam>(em);
            int castleHeartCount = CountEntitiesWithComponent<CastleHeart>(em);

            int heartOwnerCacheCount = 0;

            try
            {
                var heartCache = OwnershipCacheService.GetHeartToOwnerCacheView();

                if (heartCache != null)
                {
                    heartOwnerCacheCount = heartCache.Count;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[RaidForgeInfoCommands] Could not read heart ownership cache count: {ex.Message}");
            }

            return new CacheCountSnapshot(
                userCount,
                clanCount,
                castleHeartCount,
                heartOwnerCacheCount
            );
        }

        private static List<OptInListEntry> BuildOptInEntries(EntityManager em)
        {
            var entries = new List<OptInListEntry>();

            foreach (OwnerIdentity owner in OwnerIdentityQueries.GetUniqueOwners(em, "RaidForgeInfoCommands"))
            {
                if (!OptInRaidService.TryGetOptInTime(owner.PersistentKey, out DateTime optInTimeUtc))
                {
                    continue;
                }

                entries.Add(new OptInListEntry(
                    owner.PersistentKey,
                    owner.GetDisplayNameWithOwnerType(),
                    owner.IsClan,
                    optInTimeUtc
                ));
            }

            return entries;
        }

        private static List<OrpListEntry> BuildOrpEntries(EntityManager em)
        {
            var entries = new List<OrpListEntry>();

            foreach (OwnerIdentity owner in OwnerIdentityQueries.GetUniqueOwners(em, "RaidForgeInfoCommands"))
            {
                bool hasTimedState = OfflineGraceService.TryGetOfflineStartTime(owner.PersistentKey, out DateTime offlineStartTimeUtc);
                if (!hasTimedState)
                {
                    continue;
                }

                entries.Add(new OrpListEntry(
                    owner.PersistentKey,
                    owner.GetDisplayNameWithOwnerType(),
                    owner.IsClan,
                    hasTimedState,
                    hasTimedState ? offlineStartTimeUtc : null
                ));
            }

            return entries;
        }

        private static int CountEntitiesWithComponent<T>(EntityManager em) where T : struct
        {
            EntityQuery query = default;

            try
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
                return query.CalculateEntityCount();
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[RaidForgeInfoCommands] Failed to count component {typeof(T).Name}: {ex.Message}");
                return 0;
            }
            finally
            {
                query.Dispose();
            }
        }

        private readonly struct CacheCountSnapshot
        {
            public readonly int UserCount;
            public readonly int ClanCount;
            public readonly int CastleHeartCount;
            public readonly int HeartOwnerCacheCount;

            public CacheCountSnapshot(
                int userCount,
                int clanCount,
                int castleHeartCount,
                int heartOwnerCacheCount)
            {
                UserCount = userCount;
                ClanCount = clanCount;
                CastleHeartCount = castleHeartCount;
                HeartOwnerCacheCount = heartOwnerCacheCount;
            }
        }

        private readonly struct OptInListEntry
        {
            public readonly string OwnerKey;
            public readonly string DisplayName;
            public readonly bool IsClan;
            public readonly DateTime? OptInTimeUtc;

            public OptInListEntry(
                string ownerKey,
                string displayName,
                bool isClan,
                DateTime? optInTimeUtc)
            {
                OwnerKey = ownerKey;
                DisplayName = displayName;
                IsClan = isClan;
                OptInTimeUtc = optInTimeUtc;
            }
        }

        private readonly struct OrpListEntry
        {
            public readonly string OwnerKey;
            public readonly string DisplayName;
            public readonly bool IsClan;
            public readonly bool HasTimedOfflineState;
            public readonly DateTime? OfflineStartTimeUtc;

            public OrpListEntry(
                string ownerKey,
                string displayName,
                bool isClan,
                bool hasTimedOfflineState,
                DateTime? offlineStartTimeUtc)
            {
                OwnerKey = ownerKey;
                DisplayName = displayName;
                IsClan = isClan;
                HasTimedOfflineState = hasTimedOfflineState;
                OfflineStartTimeUtc = offlineStartTimeUtc;
            }
        }
    }
}
