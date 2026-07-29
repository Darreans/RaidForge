using System;
using System.Globalization;
using System.Linq;
using VampireCommandFramework;
using RaidForge.Config;
using RaidForge.Utils;
using static RaidForge.Commands.CommandText;

namespace RaidForge.Commands
{
    public class RaidConfigViewCommands
    {
        private const int CONFIG_RAID_GENERAL = 1;
        private const int CONFIG_OFFLINE_PROTECTION = 2;
        private const int CONFIG_OPT_IN_RAIDING = 3;
        private const int CONFIG_OPT_IN_SCHEDULE = 4;
        private const int CONFIG_SOUL_SHARDS = 5;
        private const int CONFIG_MAP_ICONS = 6;
        private const int CONFIG_WEAPON_RAIDING = 7;
        private const int CONFIG_GOLEM_SETTINGS = 8;
        private const int CONFIG_RAID_INTERFERENCE = 9;
        private const int CONFIG_TROUBLESHOOTING = 10;

        [Command("raidconfigview", "Shows a specific RaidForge config section. Usage: .raidconfigview ? OR .raidconfigview <number>", adminOnly: true)]
        public void RaidConfigView(ChatCommandContext ctx, string selection = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selection) || selection.Trim() == "?")
                {
                    ShowConfigMenu(ctx);
                    return;
                }

                selection = selection.Trim();

                if (selection.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    ShowAllConfigs(ctx);
                    return;
                }

                if (!int.TryParse(selection, out int configNumber))
                {
                    ctx.Reply(ChatColors.ErrorText($"Invalid config selection '{selection}'. Use '.raidconfigview ?' to see valid options."));
                    return;
                }

                ShowConfigByNumber(ctx, configNumber);
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error reading RaidForge config values. Check server logs."));
                LoggingHelper.Error("Error executing .raidconfigview", ex);
            }
        }

        [Command("raidconfigviewall", "Shows all currently loaded RaidForge configuration values.", adminOnly: true)]
        public void RaidConfigViewAll(ChatCommandContext ctx)
        {
            try
            {
                ShowAllConfigs(ctx);
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error reading all RaidForge config values. Check server logs."));
                LoggingHelper.Error("Error executing .raidconfigviewall", ex);
            }
        }

        private static void ShowConfigMenu(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.HighlightText(" RaidForge Config View Menu "));
            ctx.Reply(ChatColors.InfoText("Use ") + ChatColors.AccentText(".raidconfigview <number>") + ChatColors.InfoText(" to view one config section."));
            ctx.Reply(ChatColors.InfoText("Use ") + ChatColors.AccentText(".raidconfigviewall") + ChatColors.InfoText(" to view everything."));

            ctx.Reply(ChatColors.MutedText($"{CONFIG_RAID_GENERAL}. ") + ChatColors.SuccessText("Raid Schedule / General"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_OFFLINE_PROTECTION}. ") + ChatColors.SuccessText("Offline Raid Protection"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_OPT_IN_RAIDING}. ") + ChatColors.SuccessText("Opt-In Raiding"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_OPT_IN_SCHEDULE}. ") + ChatColors.SuccessText("Opt-In Schedule"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_SOUL_SHARDS}. ") + ChatColors.SuccessText("Soul Shards"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_MAP_ICONS}. ") + ChatColors.SuccessText("Map Icons"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_WEAPON_RAIDING}. ") + ChatColors.SuccessText("Weapon Raiding"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_GOLEM_SETTINGS}. ") + ChatColors.SuccessText("Golem Settings"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_RAID_INTERFERENCE}. ") + ChatColors.SuccessText("Raid Interference"));
            ctx.Reply(ChatColors.MutedText($"{CONFIG_TROUBLESHOOTING}. ") + ChatColors.SuccessText("Troubleshooting"));

            ctx.Reply(ChatColors.HighlightText(" End Config View Menu "));
        }

        private static void ShowAllConfigs(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.HighlightText(" RaidForge Loaded Config View: ALL "));

            ShowGeneralRaidConfig(ctx);
            ShowOfflineProtectionConfig(ctx);
            ShowOptInRaidingConfig(ctx);
            ShowOptInScheduleConfig(ctx);
            ShowShardConfig(ctx);
            ShowMapIconConfig(ctx);
            ShowWeaponRaidingConfig(ctx);
            ShowGolemConfig(ctx);
            ShowRaidInterferenceConfig(ctx);
            ShowTroubleshootingConfig(ctx);

            ctx.Reply(ChatColors.HighlightText(" End RaidForge Loaded Config View "));
        }

        private static void ShowConfigByNumber(ChatCommandContext ctx, int configNumber)
        {
            switch (configNumber)
            {
                case CONFIG_RAID_GENERAL:
                    ShowGeneralRaidConfig(ctx);
                    break;

                case CONFIG_OFFLINE_PROTECTION:
                    ShowOfflineProtectionConfig(ctx);
                    break;

                case CONFIG_OPT_IN_RAIDING:
                    ShowOptInRaidingConfig(ctx);
                    break;

                case CONFIG_OPT_IN_SCHEDULE:
                    ShowOptInScheduleConfig(ctx);
                    break;

                case CONFIG_SOUL_SHARDS:
                    ShowShardConfig(ctx);
                    break;

                case CONFIG_MAP_ICONS:
                    ShowMapIconConfig(ctx);
                    break;

                case CONFIG_WEAPON_RAIDING:
                    ShowWeaponRaidingConfig(ctx);
                    break;

                case CONFIG_GOLEM_SETTINGS:
                    ShowGolemConfig(ctx);
                    break;

                case CONFIG_RAID_INTERFERENCE:
                    ShowRaidInterferenceConfig(ctx);
                    break;

                case CONFIG_TROUBLESHOOTING:
                    ShowTroubleshootingConfig(ctx);
                    break;

                default:
                    ctx.Reply(ChatColors.ErrorText($"Invalid config number '{configNumber}'. Use '.raidconfigview ?' to see valid options."));
                    break;
            }
        }

        private static void ShowGeneralRaidConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[1] Raid Schedule / General"));

            ctx.Reply(ConfigLine(
                "AllowWaygateTeleportsDuringRaid",
                EnabledText(RaidConfig.AllowWaygateTeleports?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "ShowCurrentRaidingStatusInRaidStatus",
                EnabledText(RaidConfig.ShowCurrentRaidingStatusInRaidStatus?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "RaidScheduleTimeZoneForDisplay",
                ChatColors.HighlightText(RaidConfig.RaidScheduleTimeZoneDisplayString?.Value ?? "Not Set")
            ));

            ctx.Reply(ConfigLine(
                "RaidScheduleDisplayOffsetHours",
                ChatColors.HighlightText((RaidConfig.RaidScheduleDisplayOffsetHours?.Value ?? 0.0).ToString("0.##", CultureInfo.InvariantCulture))
            ));

            ctx.Reply(ConfigLine(
                "Current Raid Schedule Clock",
                ChatColors.HighlightText(RaidConfig.GetRaidScheduleNow().ToString("ddd HH:mm", CultureInfo.InvariantCulture))
            ));

            var schedule = RaidConfig.Schedule;

            if (schedule == null || schedule.Count == 0)
            {
                ctx.Reply(ChatColors.WarningText("Parsed Raid Schedule: No raid windows configured."));
                return;
            }

            ctx.Reply(ConfigLine(
                "Parsed Schedule Entries",
                ChatColors.HighlightText(schedule.Count.ToString())
            ));

            foreach (var entry in schedule.OrderBy(e => DaySortValue(e.Day)).ThenBy(e => e.StartTime))
            {
                string start = FormatTime(entry.StartTime);
                string end = entry.EndTime == TimeSpan.Zero && entry.SpansMidnight
                    ? "Midnight"
                    : FormatTime(entry.EndTime);

                string spans = entry.SpansMidnight ? " (spans midnight)" : string.Empty;

                ctx.Reply(ChatColors.MutedText($"- {entry.Day}: ") +
                    ChatColors.SuccessText($"{start} - {end}{spans}"));
            }
        }

        private static void ShowOfflineProtectionConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[2] Offline Raid Protection"));

            ctx.Reply(ConfigLine(
                "EnableOfflineProtection",
                EnabledText(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "EnableOfflineProtectionDaySchedule",
                EnabledText(OfflineRaidProtectionConfig.EnableOfflineProtectionDaySchedule?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "OfflineProtectionActiveToday",
                EnabledText(OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday())
            ));

            ctx.Reply(ConfigLine(
                "GracePeriodMinutes",
                ChatColors.HighlightText((OfflineRaidProtectionConfig.GracePeriodDurationMinutes?.Value ?? 0f).ToString(CultureInfo.InvariantCulture))
            ));

            ctx.Reply(ConfigLine(
                "AnnounceOfflineRaid",
                EnabledText(OfflineRaidProtectionConfig.AnnounceOfflineRaid?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "AnnounceDecayedBaseRaid",
                EnabledText(OfflineRaidProtectionConfig.AnnounceDecayedBaseRaid?.Value ?? false)
            ));

            if ((OfflineRaidProtectionConfig.EnableOfflineProtectionDaySchedule?.Value ?? false) &&
                OfflineRaidProtectionConfig.DayProtectionToggles != null)
            {
                foreach (var day in Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().OrderBy(DaySortValue))
                {
                    if (OfflineRaidProtectionConfig.DayProtectionToggles.TryGetValue(day, out var entry))
                    {
                        string meaning = entry.Value ? "ORP blocks damage" : "ORP tracking only";

                        ctx.Reply(ChatColors.MutedText($"- {day}: ") +
                            EnabledText(entry.Value) +
                            ChatColors.InfoText($" ({meaning})"));
                    }
                }
            }
        }

        private static void ShowOptInRaidingConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[3] Opt-In Raiding"));

            ctx.Reply(ConfigLine(
                "EnableOptInRaiding",
                EnabledText(OptInRaidingConfig.EnableOptInRaiding?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "OptInRaidingActive",
                EnabledText((OptInRaidingConfig.EnableOptInRaiding?.Value ?? false) &&
                    !(OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value ?? false))
            ));

            ctx.Reply(ConfigLine(
                "DefaultEveryoneOptedOut",
                EnabledText(OptInRaidingConfig.DefaultEveryoneOptedOut?.Value ?? true)
            ));

            ctx.Reply(ConfigLine(
                "DefaultEveryoneOptedIn",
                EnabledText(OptInRaidingConfig.DefaultEveryoneOptedIn?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "OptInLockDurationHours",
                ChatColors.HighlightText((OptInRaidingConfig.OptInLockDurationHours?.Value ?? 0).ToString())
            ));

            ctx.Reply(ConfigLine(
                "BlockOptInChangesDuringRaidHours",
                EnabledText(OptInRaidingConfig.BlockOptInChangesDuringRaidHours?.Value ?? true)
            ));

            ctx.Reply(ConfigLine(
                "AutoOptOutAfterCooldown",
                EnabledText(OptInRaidingConfig.AutoOptOutAfterCooldown?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "AutoOptInShardHolders",
                EnabledText(OptInRaidingConfig.AutoOptInShardHolders?.Value ?? false)
            ));
        }

        private static void ShowOptInScheduleConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[4] Opt-In Schedule"));

            ctx.Reply(ConfigLine(
                "EnableOptInSchedule",
                EnabledText(OptInScheduleConfig.EnableOptInSchedule?.Value ?? false)
            ));

            if (OptInScheduleConfig.DayToggles == null || OptInScheduleConfig.DayToggles.Count == 0)
            {
                ctx.Reply(ChatColors.WarningText("Opt-In day toggles are not loaded."));
                return;
            }

            foreach (var day in Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().OrderBy(DaySortValue))
            {
                if (OptInScheduleConfig.DayToggles.TryGetValue(day, out var entry))
                {
                    string meaning = entry.Value ? "Opt-In system allowed" : "Forced raid day";

                    ctx.Reply(ChatColors.MutedText($"- {day}: ") +
                        EnabledText(entry.Value) +
                        ChatColors.InfoText($" ({meaning})"));
                }
            }
        }

        private static void ShowShardConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[5] Soul Shards"));

            ctx.Reply(ConfigLine(
                "MaxAllowedShardsPerType",
                ChatColors.HighlightText((ShardConfig.MaxAllowedShardsPerType?.Value ?? 1).ToString())
            ));

            ctx.Reply(ConfigLine(
                "DisableOrpForShardHolders",
                EnabledText(ShardConfig.DisableOrpForShardHolders?.Value ?? false)
            ));
        }

        private static void ShowMapIconConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[6] Map Icons"));

            ctx.Reply(ConfigLine(
                "EnableOfflineRaidMapIcon",
                EnabledText(MapIconsConfig.EnableOfflineRaidMapIcon?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "OfflineRaidMapIconPrefab",
                ChatColors.HighlightText(MapIconsConfig.OfflineRaidMapIconPrefab?.Value ?? string.Empty)
            ));

            ctx.Reply(ConfigLine(
                "EnableDecayRaidMapIcon",
                EnabledText(MapIconsConfig.EnableDecayRaidMapIcon?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "DecayRaidMapIconPrefab",
                ChatColors.HighlightText(MapIconsConfig.DecayRaidMapIconPrefab?.Value ?? string.Empty)
            ));

            ctx.Reply(ConfigLine(
                "EnableOptInRaidMapIcon",
                EnabledText(MapIconsConfig.EnableOptInRaidMapIcon?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "EnableOptOutRaidMapIcon",
                EnabledText(MapIconsConfig.EnableOptOutRaidMapIcon?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "RaidMapIconTimeoutSeconds",
                ChatColors.HighlightText((MapIconsConfig.RaidMapIconTimeoutSeconds?.Value ?? 0).ToString())
            ));
        }

        private static void ShowWeaponRaidingConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[7] Weapon Raiding"));

            ctx.Reply(ConfigLine(
                "EnableWeaponRaiding",
                EnabledText(WeaponRaidingConfig.EnableWeaponRaiding?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "WeaponDamageVsStoneMultiplier",
                ChatColors.HighlightText((WeaponRaidingConfig.WeaponDamageVsStoneMultiplier?.Value ?? 0f).ToString(CultureInfo.InvariantCulture))
            ));
        }

        private static void ShowGolemConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[8] Golem Settings"));

            ctx.Reply(ConfigLine(
                "EnableDayBasedAutomation",
                EnabledText(GolemAutomationConfig.EnableDayBasedAutomation?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "ServerStartDateForAutomation",
                ChatColors.HighlightText(string.IsNullOrWhiteSpace(GolemAutomationConfig.ServerStartDateForAutomation?.Value)
                    ? "Not Set"
                    : GolemAutomationConfig.ServerStartDateForAutomation.Value)
            ));

            ctx.Reply(ConfigLine(
                "ManualOverrideSiegeLevel",
                ChatColors.HighlightText(string.IsNullOrWhiteSpace(GolemAutomationConfig.ManualSiegeWeaponHealthOverride?.Value)
                    ? "Not Set"
                    : GolemAutomationConfig.ManualSiegeWeaponHealthOverride.Value)
            ));

            if (GolemAutomationConfig.ParsedStartDate.HasValue)
            {
                ctx.Reply(ConfigLine(
                    "ParsedStartDate",
                    ChatColors.HighlightText(GolemAutomationConfig.ParsedStartDate.Value.ToString("yyyy-MM-dd HH:mm:ss"))
                ));
            }
            else
            {
                ctx.Reply(ConfigLine(
                    "ParsedStartDate",
                    ChatColors.WarningText("Not Set / Invalid")
                ));
            }

            if (GolemAutomationConfig.ParsedDayBasedSchedule == null || GolemAutomationConfig.ParsedDayBasedSchedule.Count == 0)
            {
                ctx.Reply(ChatColors.WarningText("Parsed Golem Day Schedule: Empty"));
                return;
            }

            ctx.Reply(ConfigLine(
                "ParsedDayBasedScheduleEntries",
                ChatColors.HighlightText(GolemAutomationConfig.ParsedDayBasedSchedule.Count.ToString())
            ));

            foreach (var kvp in GolemAutomationConfig.ParsedDayBasedSchedule)
            {
                ctx.Reply(ChatColors.MutedText($"- Day {kvp.Key}: ") + ChatColors.SuccessText(kvp.Value.ToString()));
            }
        }

        private static void ShowRaidInterferenceConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[9] Raid Interference"));

            ctx.Reply(ConfigLine(
                "EnableRaidInterference",
                EnabledText(RaidInterferenceConfig.EnableRaidInterference?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "DisableInterferenceForOfflineRaids",
                EnabledText(RaidInterferenceConfig.DisableInterferenceForOfflineRaids?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "DisableInterferenceForDecayingBases",
                EnabledText(RaidInterferenceConfig.DisableInterferenceForDecayingBases?.Value ?? false)
            ));

            ctx.Reply(ConfigLine(
                "ExemptBearFormUsers",
                EnabledText(RaidInterferenceConfig.ExemptBearFormUsers?.Value ?? false)
            ));
        }

        private static void ShowTroubleshootingConfig(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.AccentText("[10] Troubleshooting"));

            ctx.Reply(ConfigLine(
                "EnableVerboseLogging",
                EnabledText(TroubleshootingConfig.EnableVerboseLogging?.Value ?? false)
            ));
        }

        private static int DaySortValue(DayOfWeek day)
        {
            return day == DayOfWeek.Sunday ? 7 : (int)day;
        }
    }
}
