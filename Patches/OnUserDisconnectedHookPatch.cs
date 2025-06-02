using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Network;
using System;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserDisconnected))]
    public static class OnUserDisconnectedHookPatch
    {
        private static void Prefix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId, ConnectionStatusChangeReason connectionStatusReason, string extraData)
        {
            string charNameForLog = "Unknown/CouldNotRetrieve";
            Entity userEntity = Entity.Null;
            EntityManager entityManager = __instance.EntityManager;

            try
            {
                if (__instance._NetEndPointToApprovedUserIndex.TryGetValue(netConnectionId, out int userIndexFromEvent))
                {
                    if (userIndexFromEvent >= 0 && userIndexFromEvent < __instance._ApprovedUsersLookup.Length)
                    {
                        var serverClient = __instance._ApprovedUsersLookup[userIndexFromEvent];
                        userEntity = serverClient.UserEntity;
                        if (entityManager.Exists(userEntity) && entityManager.HasComponent<User>(userEntity))
                        {
                            charNameForLog = entityManager.GetComponentData<User>(userEntity).CharacterName.ToString();
                        }
                        else
                        {
                            charNameForLog = $"UserEntity {userEntity} found, but no User component/name or entity doesn't exist.";
                        }
                    }
                }

                if (userEntity == Entity.Null)
                {
                    return;
                }

                if (!entityManager.Exists(userEntity))
                {
                    return;
                }

                OfflineGraceService.HandleUserDisconnected(entityManager, userEntity, false);
            }
            catch (Exception ex)
            {
                
            }
        }
    }
}