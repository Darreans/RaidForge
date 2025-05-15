using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches;

[HarmonyPatch(typeof(TeleportationRequestSystem), nameof(TeleportationRequestSystem.OnUpdate))]
public static class HandleTeleportationRequest
{
	private static bool _isRaidWindow;
	
	private static void Prefix(TeleportationRequestSystem __instance)
	{
		var em = __instance.EntityManager;
		var query = __instance._TeleportRequestQuery;
		var requests = query.ToEntityArray(Allocator.Temp);

		if (VWorldUtils.GameBalanceSettings(out var balance) && VWorldUtils.ZDateTime(out var dt))
		{ 
			_isRaidWindow = balance.IsCastlePvPEnabled(dt);
		}
		else
		{
			return;
		}

		// Allow function to execute normalyl if castle pvp is enabled & config var is set to true
		if (_isRaidWindow && RaidConfig.AllowWaygateTeleports.Value.Equals(true))
			return;
		
		foreach (var req in requests)
		{
			var requestData = req.Read<TeleportationRequest>();
			var requestPlayerCharacter = requestData.PlayerEntity.Read<PlayerCharacter>();
			var requestUserObject = requestPlayerCharacter.UserEntity.Read<User>();

			// Destroy TeleportationRequest entity
			em.DestroyEntity(req);
				
			// Log & notify
			RaidForgePlugin.Logger.LogInfo($"Teleport request from {requestPlayerCharacter.Name} destroyed.");
				
			var message = new FixedString512Bytes($"You cannot use waygates during raid hours!");
			ServerChatUtils.SendSystemMessageToClient(em, requestUserObject, ref message);
		}
	}
}