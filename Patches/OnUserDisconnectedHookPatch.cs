using HarmonyLib;
using ProjectM;
using Stunlock.Network;
using System;
using RaidForge.Config;
using RaidForge.Services;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserDisconnected))]
    public static class OnUserDisconnectedHookPatch
    {
        private static void Prefix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
        {
            try
            {
                if (__instance == null)
                {
                    LogVerboseWarning("[RaidForge] OnUserDisconnectedHookPatch skipped: ServerBootstrapSystem instance was null.");
                    return;
                }

                if (!Plugin.SystemsInitialized)
                {
                    return;
                }

                if (!__instance._NetEndPointToApprovedUserIndex.TryGetValue(netConnectionId, out int userIndex))
                {
                    return;
                }

                if (__instance._ApprovedUsersLookup == null)
                {
                    LogVerboseWarning("[RaidForge] OnUserDisconnectedHookPatch skipped: Approved users lookup was null.");
                    return;
                }

                if (userIndex < 0 || userIndex >= __instance._ApprovedUsersLookup.Length)
                {
                    LogVerboseWarning($"[RaidForge] OnUserDisconnectedHookPatch skipped: Approved user index out of range. Index: {userIndex}, Length: {__instance._ApprovedUsersLookup.Length}");
                    return;
                }

                var serverClient = __instance._ApprovedUsersLookup[userIndex];

                if (serverClient == null)
                {
                    LogVerboseWarning($"[RaidForge] OnUserDisconnectedHookPatch skipped: ServerClient was null for index {userIndex}.");
                    return;
                }

                Entity userEntity = serverClient.UserEntity;
                EntityManager entityManager = __instance.EntityManager;

                if (userEntity == Entity.Null)
                {
                    LogVerboseWarning("[RaidForge] OnUserDisconnectedHookPatch skipped: UserEntity was null.");
                    return;
                }

                if (!entityManager.Exists(userEntity))
                {
                    LogVerboseWarning($"[RaidForge] OnUserDisconnectedHookPatch skipped: UserEntity no longer exists. Entity: {userEntity}");
                    return;
                }

                OfflineGraceService.HandleUserDisconnected(entityManager, userEntity);
                PlayerRegistryService.UpsertUser(entityManager, userEntity, false);
                RaidMapIconService.MarkPersistentStateIconsDirty();
                RaidMapIconService.ProcessCleanup();
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[RaidForge] Error in OnUserDisconnectedHookPatch Prefix: {ex}");
            }
        }

        private static void LogVerboseWarning(string message)
        {
            if (TroubleshootingConfig.EnableVerboseLogging?.Value == true)
            {
                Plugin.Logger?.LogWarning(message);
            }
        }
    }
}
