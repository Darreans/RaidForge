using VampireCommandFramework;
using System;

namespace RaidForge
{
    public class RaidCommand
    {
        [Command("raidon", "Immediately turn raids ON (one-time).", adminOnly: true)]
        public void RaidOn(ChatCommandContext ctx)
        {
            VrisingRaidToggler.EnableRaids(RaidForgePlugin.Logger);
            ctx.Reply("[RaidForge] Raids turned ON now. Next schedule boundary may revert it if we're outside the window.");
        }

        [Command("raidoff", "Immediately turn raids OFF. If inside today's window, skip the rest of that window.", adminOnly: true)]
        public void RaidOff(ChatCommandContext ctx)
        {
            VrisingRaidToggler.DisableRaids(RaidForgePlugin.Logger);
            RaidtimeManager.SkipCurrentWindowIfAny();
            ctx.Reply("[RaidForge] Raids turned OFF. If we were in a window, that window is skipped for today.");
        }

        [Command("raidtime", "Shows the current server date/time and day-of-week.", adminOnly: true)]
        public void RaidTime(ChatCommandContext ctx)
        {
            var now = DateTime.Now;
            ctx.Reply($"[RaidForge] Server time: {now:F} (DayOfWeek={now.DayOfWeek}).");
        }

        // NEW: .raidt => Show next scheduled ON time
        [Command("raidt", "Shows the next scheduled time raids will turn ON automatically.", adminOnly: false)]
        public void RaidNextOnTime(ChatCommandContext ctx)
        {
            var nextOn = RaidtimeManager.GetNextOnTime(DateTime.Now);
            if (nextOn == null)
            {
                ctx.Reply("[RaidForge] No further raid-on time found in the next 7 days.");
            }
            else
            {
                ctx.Reply($"[RaidForge] Next scheduled ON time: {nextOn.Value:F}");
            }
        }
    }
}
