using System;
using System.Linq;
using VampireCommandFramework;
using ProjectM;
using RaidForge.Systems;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Commands
{
    public class GolemCommands
    {
        [Command("golemstartdate", "Sets the Golem Automation start date to the current time.", adminOnly: true)]
        public void SetGolemStartDate(ChatCommandContext ctx)
        {
            if (Plugin.Instance == null) { ctx.Reply(ChatColors.ErrorText("Error: Plugin instance not found.")); return; }
            try
            {
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
                    ctx.Reply(ChatColors.ErrorText("Failed to set and save Golem start date. Config may not be initialized."));
                }
            }
            catch (Exception)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while setting the Golem start date."));
            }
        }

        [Command("golemcurrent", "Shows the current Golem health settings.", adminOnly: true)]
        public void GolemCurrent(ChatCommandContext ctx)
        {
            var currentActualHealthEnum = SiegeWeaponSystem.GetSiegeWeaponHealth();

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
            GolemAutomationConfig.ClearManualSiegeWeaponHealthOverrideAndSave();
            Plugin.TriggerGolemAutomationResetFromCommand();
            GolemAutomationSystem.CheckAutomation();
            ctx.Reply(ChatColors.SuccessText("Manual Golem health override cleared. Automation will now apply if enabled."));
        }

        [Command("golemlist", "Lists available Siege Golem health levels and estimated HP.", adminOnly: true)]
        public void GolemList(ChatCommandContext ctx)
        {
            ctx.Reply(ChatColors.HighlightText("Siege Golem Health Levels (Estimates from Config):"));
            if (GolemAutomationConfig.GolemHpEstimates != null && GolemAutomationConfig.GolemHpEstimates.Any())
            {
                foreach (var kvp in GolemAutomationConfig.GolemHpEstimates.OrderBy(kv => kv.Value))
                {
                    ctx.Reply(ChatColors.InfoText($"{kvp.Key}") + " = " + ChatColors.SuccessText($"~{kvp.Value} HP"));
                }
            }
            else
            {
                ctx.Reply(ChatColors.WarningText("HP estimates not available in GolemAutomationConfig."));
            }
        }
    }
}