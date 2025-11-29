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
            EntityManager entityManager = __instance.EntityManager;

            if (entityManager == default)
            {
                return;
            }

            if (userToLeave == Entity.Null)
            {
                return;
            }

            try
            {
                FixedString64Bytes userWhoLeftCharacterName = new FixedString64Bytes("Unknown (User Data N/A)");
                if (entityManager.Exists(userToLeave) && entityManager.HasComponent<User>(userToLeave))
                {
                    userWhoLeftCharacterName = entityManager.GetComponentData<User>(userToLeave).CharacterName;
                }

                if (entityManager.Exists(clanEntity))
                {
                    OfflineGraceService.HandleClanMemberDeparted(entityManager, userToLeave, clanEntity, userWhoLeftCharacterName);
                }

                OwnershipCacheService.UpdateUserClan(userToLeave, Entity.Null, entityManager);
            }
            catch (Exception ex)
            {
            }
        }
    }
}