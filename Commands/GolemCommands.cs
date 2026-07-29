using System;
using System.Linq;
using VampireCommandFramework;
using ProjectM;
using RaidForge.Systems;
using RaidForge.Config;
using RaidForge.Utils;
using Unity.Entities;
using ProjectM.Network;
using RaidForge.Data;

namespace RaidForge.Commands
{
    public class GolemCommands
    {
        [Command("golemstartdate", "Sets the Golem Automation start date to the current time.", adminOnly: true)]
        public void SetGolemStartDate(ChatCommandContext ctx)
        {
            if (Plugin.Instance == null)
            {
                ctx.Reply(ChatColors.ErrorText("Error: Plugin instance not found."));
                return;
            }

            try
            {
                DateTime now = DateTime.Now;
                string formattedDate = now.ToString("yyyy-MM-dd HH:mm:ss");

                if (!GolemAutomationConfig.SetServerStartDateAndSave(formattedDate))
                {
                    ctx.Reply(ChatColors.ErrorText("Failed to set and save Golem start date. Config may not be initialized."));
                    return;
                }

                Plugin.TriggerGolemAutomationResetFromCommand();
                GolemAutomationSystem.CheckAutomation();

                ctx.Reply(ChatColors.SuccessText("Golem Automation start date set to: ") + ChatColors.InfoText(formattedDate));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while setting the Golem start date. Check server logs."));
                LoggingHelper.Error("Error executing .golemstartdate", ex);
            }
        }

        [Command("golemcurrent", "Shows the current Golem health settings.", adminOnly: true)]
        public void GolemCurrent(ChatCommandContext ctx)
        {
            try
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

                string manualLevelOverrideVal = string.IsNullOrWhiteSpace(GolemAutomationConfig.ManualSiegeWeaponHealthOverride?.Value)
                    ? "Not set"
                    : GolemAutomationConfig.ManualSiegeWeaponHealthOverride.Value.Trim();

                bool dayBasedAutomationEnabled = GolemAutomationConfig.EnableDayBasedAutomation?.Value ?? false;

                ctx.Reply(ChatColors.InfoText($"Config - Manual Level Override: {ChatColors.HighlightText(manualLevelOverrideVal)}"));
                ctx.Reply(ChatColors.InfoText($"Config - Day-Based Automation Enabled: {ChatColors.HighlightText(dayBasedAutomationEnabled.ToString())}"));

                if (!string.Equals(manualLevelOverrideVal, "Not set", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Reply(ChatColors.WarningText("Manual Level Override is active and takes precedence over day-based automation. Use '.golemauto' to clear."));
                }
                else if (!dayBasedAutomationEnabled)
                {
                    ctx.Reply(ChatColors.WarningText("Day-based automation is disabled and no manual level is set. Golem HP may use server default or last known setting."));
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while checking Golem health settings. Check server logs."));
                LoggingHelper.Error("Error executing .golemcurrent", ex);
            }
        }

        [Command("golemsethp", "Manually sets and persists a Siege Golem health level. Usage: .golemsethp <LevelName>", adminOnly: true)]
        public void GolemSetHpByLevelName(ChatCommandContext ctx, string levelName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(levelName))
                {
                    string validLevelsForEmptyInput = string.Join(", ", Enum.GetNames(typeof(SiegeWeaponHealth)));
                    ctx.Reply(ChatColors.ErrorText($"Missing health level name. Valid levels are: {validLevelsForEmptyInput}"));
                    return;
                }

                levelName = levelName.Trim();

                if (!Enum.TryParse<SiegeWeaponHealth>(levelName, true, out var healthValue))
                {
                    string validLevels = string.Join(", ", Enum.GetNames(typeof(SiegeWeaponHealth)));
                    ctx.Reply(ChatColors.ErrorText($"Invalid health level name '{levelName}'. Valid levels are: {validLevels}"));
                    return;
                }

                GolemAutomationConfig.SetManualSiegeWeaponHealthOverrideAndSave(healthValue);

                Plugin.TriggerGolemAutomationResetFromCommand();
                GolemAutomationSystem.CheckAutomation();

                string hpEstimate = string.Empty;

                if (GolemAutomationConfig.GolemHpEstimates.TryGetValue(healthValue, out int approxHpVal))
                {
                    hpEstimate = $" (~{approxHpVal} HP)";
                }

                ctx.Reply(ChatColors.SuccessText($"Persistent Golem health override set to Level: {ChatColors.AccentText(healthValue.ToString())}{hpEstimate}. Day-based automation is now overridden. Use '.golemauto' to clear."));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while setting Golem HP. Check server logs."));
                LoggingHelper.Error("Error executing .golemsethp", ex);
            }
        }

        [Command("golemauto", "Clears manual Golem health override. Day-based automation will apply if enabled.", adminOnly: true)]
        public void GolemSetAuto(ChatCommandContext ctx)
        {
            try
            {
                GolemAutomationConfig.ClearManualSiegeWeaponHealthOverrideAndSave();

                Plugin.TriggerGolemAutomationResetFromCommand();
                GolemAutomationSystem.CheckAutomation();

                ctx.Reply(ChatColors.SuccessText("Manual Golem health override cleared. Automation will now apply if enabled."));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while clearing the Golem manual override. Check server logs."));
                LoggingHelper.Error("Error executing .golemauto", ex);
            }
        }

        [Command("golemlist", "Lists available Siege Golem health levels and estimated HP.", adminOnly: true)]
        public void GolemList(ChatCommandContext ctx)
        {
            try
            {
                ctx.Reply(ChatColors.HighlightText("Siege Golem Health Levels (Estimates from Config):"));

                if (GolemAutomationConfig.GolemHpEstimates == null || !GolemAutomationConfig.GolemHpEstimates.Any())
                {
                    ctx.Reply(ChatColors.WarningText("HP estimates not available in GolemAutomationConfig."));
                    return;
                }

                foreach (var kvp in GolemAutomationConfig.GolemHpEstimates.OrderBy(kv => kv.Value))
                {
                    ctx.Reply(ChatColors.InfoText($"{kvp.Key}") + " = " + ChatColors.SuccessText($"~{kvp.Value} HP"));
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while listing Golem health levels. Check server logs."));
                LoggingHelper.Error("Error executing .golemlist", ex);
            }
        }

        [Command("golem", "Transforms the target player into a Siege Golem. Usage: .golem [PlayerName]", adminOnly: true)]
        public void GolemTransformCommand(ChatCommandContext ctx, string playerName = null)
        {
            try
            {
                Entity targetCharacterEntity = Entity.Null;
                string targetName;

                if (string.IsNullOrWhiteSpace(playerName))
                {
                    targetCharacterEntity = ctx.Event.SenderCharacterEntity;
                    targetName = "yourself";
                }
                else
                {
                    playerName = playerName.Trim();

                    if (!UserHelper.FindUserEntity(VWorld.EntityManager, playerName, out _, out User targetUserData, out string foundName))
                    {
                        ctx.Reply(ChatColors.ErrorText($"Player '{playerName}' not found."));
                        return;
                    }

                    targetCharacterEntity = targetUserData.LocalCharacter._Entity;
                    targetName = foundName;
                }

                if (targetCharacterEntity == Entity.Null)
                {
                    ctx.Reply(ChatColors.ErrorText("Could not find a valid character entity for the target."));
                    return;
                }

                if (!VWorld.EntityManager.Exists(targetCharacterEntity))
                {
                    ctx.Reply(ChatColors.ErrorText("Target character entity no longer exists."));
                    return;
                }

                ApplyBuff(targetCharacterEntity, PrefabData.SiegeGolemBuff.Guid);

                ctx.Reply(ChatColors.SuccessText($"Applied Siege Golem form to {targetName}."));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Failed to apply golem buff. Check server logs."));
                LoggingHelper.Error("Error executing .golem", ex);
            }
        }

        private static void ApplyBuff(Entity target, Stunlock.Core.PrefabGUID buffGuid)
        {
            var entityManager = VWorld.EntityManager;
            var debugEventsSystem = VWorld.Server.GetExistingSystemManaged<DebugEventsSystem>();

            if (debugEventsSystem == null)
            {
                throw new InvalidOperationException("DebugEventsSystem could not be found.");
            }

            var fromCharacter = new FromCharacter
            {
                Character = target
            };

            if (entityManager.HasComponent<PlayerCharacter>(target))
            {
                var playerCharacter = entityManager.GetComponentData<PlayerCharacter>(target);

                if (playerCharacter.UserEntity != Entity.Null && entityManager.Exists(playerCharacter.UserEntity))
                {
                    fromCharacter.User = playerCharacter.UserEntity;
                }
            }

            var buffEvent = new ApplyBuffDebugEvent
            {
                BuffPrefabGUID = buffGuid
            };

            debugEventsSystem.ApplyBuff(fromCharacter, buffEvent);
        }
    }
}
