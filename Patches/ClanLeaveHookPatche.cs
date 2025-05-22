using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Clan;
using ProjectM.Network;
using Unity.Entities;
using System;
using RaidForge.Services; 
using RaidForge.Utils;    
using Unity.Collections;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ClanSystem_Server), nameof(ClanSystem_Server.LeaveClan))]
    public static class ClanLeaveHookPatch
    {
        private static void Postfix(ClanSystem_Server __instance, EntityCommandBuffer commandBuffer, ModificationsRegistry modificationsRegistry, Entity clanEntity, Entity userToLeave, ClanSystem_Server.LeaveReason reason, CastleHeartLimitType castleHeartLimitType)
        {
            LoggingHelper.Info($"[ClanLeaveHookPatch] Postfix for LeaveClan entered. User: {userToLeave}, Clan: {clanEntity}, Reason: {reason}");

            if (__instance == null)
            {
                LoggingHelper.Error("[ClanLeaveHookPatch] __instance (ClanSystem_Server) is null! Cannot proceed.");
                return;
            }

            EntityManager entityManager = __instance.EntityManager;

            if (entityManager == default)
            {
                LoggingHelper.Error("[ClanLeaveHookPatch] EntityManager is default! Cannot proceed.");
                return;
            }

            try
            {
                FixedString64Bytes userWhoLeftCharacterName = new FixedString64Bytes("Unknown (User Data N/A)");
                if (entityManager.Exists(userToLeave) && entityManager.HasComponent<User>(userToLeave))
                {
                    userWhoLeftCharacterName = entityManager.GetComponentData<User>(userToLeave).CharacterName;
                }
                else
                {
                    LoggingHelper.Warning($"[ClanLeaveHookPatch] UserToLeave {userToLeave} does not exist or lacks User component. Using default name for logging.");
                }

                if (!entityManager.Exists(clanEntity))
                {
                    LoggingHelper.Warning($"[ClanLeaveHookPatch] ClanEntity {clanEntity} does not exist. Cannot process clan leave for grace period.");
                    return;
                }

                LoggingHelper.Info($"[ClanLeaveHookPatch] User '{userWhoLeftCharacterName.ToString()}' (Entity: {userToLeave}) left Clan {clanEntity}. Calling HandleClanMemberDeparted.");
                OfflineGraceService.HandleClanMemberDeparted(entityManager, userToLeave, clanEntity, userWhoLeftCharacterName);
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"Error in ClanLeaveHookPatch Postfix", ex);
            }
        }
    }
}