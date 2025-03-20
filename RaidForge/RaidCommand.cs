using BepInEx.IL2CPP;
using ForgeScheduler;
using VampireCommandFramework;
using System;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP;

namespace RaidForge
{
    public class RaidCommand
    {
        private static readonly Dictionary<ProjectM.SiegeWeaponHealth, int> GolemHpEstimates
            = new Dictionary<ProjectM.SiegeWeaponHealth, int>
            {
                { ProjectM.SiegeWeaponHealth.VeryLow,    750 },
                { ProjectM.SiegeWeaponHealth.Low,       1000 },
                { ProjectM.SiegeWeaponHealth.Normal,    1250 },
                { ProjectM.SiegeWeaponHealth.High,      1750 },
                { ProjectM.SiegeWeaponHealth.VeryHigh,  2500 },
                { ProjectM.SiegeWeaponHealth.MegaHigh,  3250 },
                { ProjectM.SiegeWeaponHealth.UltraHigh, 4000 },
                { ProjectM.SiegeWeaponHealth.CrazyHigh, 5000 },
                { ProjectM.SiegeWeaponHealth.Max,       7500 },
            };

        [Command("raidmode", "Sets the schedule to ignore or normal scheduling.", adminOnly: true)]
        public void RaidMode(ChatCommandContext ctx, string newMode)
        {
            newMode = newMode.Trim().ToLowerInvariant();
            switch (newMode)
            {
                case "ignore":
                    if (!IL2CPPChainloader.Instance.Plugins.ContainsKey("forgescheduler"))
                    {
                        ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>ForgeScheduler not installed.</color>");
                        return;
                    }
                    RaidForgeScheduleManager.IgnoreScheduleTime.Value = true;
                    ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Ignoring schedule time. Raids must be toggled manually.</color>");
                    break;

                case "normal":
                    if (!IL2CPPChainloader.Instance.Plugins.ContainsKey("forgescheduler"))
                    {
                        ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>ForgeScheduler not installed.</color>");
                        return;
                    }
                    RaidForgeScheduleManager.IgnoreScheduleTime.Value = false;
                    ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Resumed normal day-of-week scheduling.</color>");
                    break;

                default:
                    ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Invalid mode. Use <i>ignore</i> or <i>normal</i>.</color>");
                    break;
            }
        }

        [Command("raidon", "Manually turn raids ON.", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
            ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#00FF00>Raids turned ON.</color>");
        }

        [Command("raidoff", "Manually turn raids OFF.", adminOnly: true)]
        public void Raidoff(ChatCommandContext ctx)
        {
            VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
            ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Raids turned OFF.</color>");
        }

        [Command("raidt", "Shows the next scheduled time raids will turn ON automatically.", adminOnly: false)]
        public void RaidNextOnTime(ChatCommandContext ctx)
        {
            if (!IL2CPPChainloader.Instance.Plugins.ContainsKey("forgescheduler"))
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>ForgeScheduler not installed. No scheduled raid times.</color>");
                return;
            }

            try
            {
                DateTime? nextOn = RaidForgeScheduleManager.GetNextOnTime(7);
                if (nextOn == null)
                {
                    ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>No further raid-on time found in the next 7 days.</color>");
                }
                else
                {
                    ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Next scheduled ON time: {nextOn.Value:F}</color>");
                }
            }
            catch (Exception ex)
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Error fetching schedule from ForgeScheduler.</color>");
                RaidForgePlugin.Logger.LogError($"[RaidForge] .raidt error: {ex}");
            }
        }

        [Command("golemcurrent", "Shows the current SiegeWeaponHealth (SGB).", adminOnly: true)]
        public void GolemCurrent(ChatCommandContext ctx)
        {
            var current = SiegeWeaponManager.GetSiegeWeaponHealth(RaidForgePlugin.Logger);
            if (current == null)
            {
                ctx.Reply("<color=#FFD700>[RaidForge]</color> <color=#FF0000>Could not retrieve SiegeWeaponHealth!</color>");
                return;
            }

            if (GolemHpEstimates.TryGetValue(current.Value, out int approxHp))
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Current = {current.Value} (~{approxHp} HP)</color>");
            }
            else
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FFFFFF>Current = {current.Value}</color>");
            }
        }

        [Command("golemlow", "Sets SiegeWeaponHealth => Low", adminOnly: true)]
        public void GolemLow(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.Low);
        }

        [Command("golemnormal", "Sets SiegeWeaponHealth => Normal", adminOnly: true)]
        public void GolemNormal(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.Normal);
        }

        [Command("golemhigh", "Sets SiegeWeaponHealth => High", adminOnly: true)]
        public void GolemHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.High);
        }

        [Command("golemveryhigh", "Sets SiegeWeaponHealth => VeryHigh", adminOnly: true)]
        public void GolemVeryHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.VeryHigh);
        }

        [Command("golemmegahigh", "Sets SiegeWeaponHealth => MegaHigh", adminOnly: true)]
        public void GolemMegaHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.MegaHigh);
        }

        [Command("golemultrahigh", "Sets SiegeWeaponHealth => UltraHigh", adminOnly: true)]
        public void GolemUltraHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.UltraHigh);
        }

        [Command("golemcrazyhigh", "Sets SiegeWeaponHealth => CrazyHigh", adminOnly: true)]
        public void GolemCrazyHigh(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.CrazyHigh);
        }

        [Command("golemmax", "Sets SiegeWeaponHealth => Max", adminOnly: true)]
        public void GolemMax(ChatCommandContext ctx)
        {
            ApplySiegeHealth(ctx, ProjectM.SiegeWeaponHealth.Max);
        }

        private void ApplySiegeHealth(ChatCommandContext ctx, ProjectM.SiegeWeaponHealth healthValue)
        {
            bool ok = SiegeWeaponManager.SetSiegeWeaponHealth(healthValue, RaidForgePlugin.Logger);
            if (!ok)
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#FF0000>Failed to set SiegeWeaponHealth to {healthValue}!</color>");
                return;
            }

            if (GolemHpEstimates.TryGetValue(healthValue, out int approxHp))
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#00FF00>Golems updated to ~{approxHp} HP ({healthValue}).</color>");
            }
            else
            {
                ctx.Reply($"<color=#FFD700>[RaidForge]</color> <color=#00FF00>Golems updated: {healthValue}.</color>");
            }
        }
    }
}
