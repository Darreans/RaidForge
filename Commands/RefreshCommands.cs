using System;
using VampireCommandFramework;
using ProjectM;
using RaidForge.Services;
using RaidForge.Utils;

namespace RaidForge.Commands
{
    public class RefreshCommands
    {
        [Command("raidrefreshcache", "Forcefully clears and rebuilds the entire RaidForge ownership cache. Use if the server is not detecting players or bases correctly.", adminOnly: true)]
        public void RefreshCacheCommand(ChatCommandContext ctx)
        {
            if (Plugin.Instance == null)
            {
                ctx.Reply(ChatColors.ErrorText("Error: Plugin instance not found."));
                return;
            }

            try
            {
                ctx.Reply(ChatColors.InfoText("Status: ") + ChatColors.HighlightText("Clearing and rebuilding RaidForge cache..."));

                var em = VWorld.Server.EntityManager;

                OwnershipCacheService.ClearAllCaches();

                int heartsFound = OwnershipCacheService.InitializeHeartOwnershipCache(em);
                int usersFound = OwnershipCacheService.InitializeUserToClanCache(em);

                OfflineGraceService.EstablishInitialGracePeriodsOnBoot(em);

                ctx.Reply(ChatColors.SuccessText("Cache Refresh Complete."));
                ctx.Reply(ChatColors.InfoText($"Found & Cached: {ChatColors.AccentText(heartsFound.ToString())} Castle Hearts"));
                ctx.Reply(ChatColors.InfoText($"Found & Cached: {ChatColors.AccentText(usersFound.ToString())} Users/Clans"));

                LoggingHelper.Info($"[Command] Cache refresh triggered by admin. Cached {heartsFound} hearts and {usersFound} users.");
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Critical error during cache refresh. Check server logs."));
                LoggingHelper.Error("Error executing .raidrefreshcache", ex);
            }
        }
    }
}