using System;
using VampireCommandFramework;
using RaidForge.Services;
using RaidForge.Utils;

namespace RaidForge.Commands
{
	public class IconCommands
	{
		[Command("clearraidforgeicons", "Manually clears all active raid map icons.", adminOnly: true)]
		public void ClearIconsCommand(ChatCommandContext ctx)
		{
			try
			{
				ctx.Reply(ChatColors.InfoText("Status: ") + ChatColors.HighlightText("Clearing active raid map icons..."));
				RaidMapIconService.RemoveAllIcons();
				ctx.Reply(ChatColors.SuccessText("All raid map icons have been successfully removed."));
				LoggingHelper.Info($"[Command] Admin '{ctx.Event.User.CharacterName}' forcefully cleared all raid map icons.");
			}
			catch (Exception ex)
			{
				ctx.Reply(ChatColors.ErrorText("Critical error during icon cleanup. Check server logs."));
				LoggingHelper.Error("Error executing .clearraidforgeicons", ex);
			}
		}
	}
}