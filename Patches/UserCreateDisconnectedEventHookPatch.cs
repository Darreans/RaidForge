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
    public static class UserCreateDisconnectedHookPatch
    {
        private static void Prefix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
        {
            try
            {
                if (__instance._NetEndPointToApprovedUserIndex.TryGetValue(netConnectionId, out int userIndex))
                {
                    var serverClient = __instance._ApprovedUsersLookup[userIndex];
                    var userEntity = serverClient.UserEntity;
                    var entityManager = __instance.EntityManager;

                    if (entityManager.Exists(userEntity))
                    {
                        OfflineGraceService.HandleUserDisconnected(entityManager, userEntity, false);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error in OnUserDisconnectedHookPatch Prefix", ex);
            }
        }
    }
}