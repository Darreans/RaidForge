using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(TeleportationRequestSystem), nameof(TeleportationRequestSystem.OnUpdate))]
    public static class TeleportationRestrictionsPatch
    {
        private static void Prefix(TeleportationRequestSystem __instance)
        {
            var em = __instance.EntityManager;
            var query = __instance._TeleportRequestQuery;
            var requests = query.ToEntityArray(Allocator.TempJob);

            try
            {
                if (requests.Length == 0)
                {
                    return;
                }

                bool currentRaidStatus;
                if (VWorld.GameBalanceSettings(out var balance) && VWorld.ZDateTime(out var dt))
                {
                    currentRaidStatus = balance.IsCastlePvPEnabled(dt);
                }
                else
                {
                    if (TroubleshootingConfig.EnableVerboseLogging.Value)
                    {
                        LoggingHelper.Warning("TeleportationRestrictionsPatch: Could not retrieve GameBalanceSettings or ZDateTime. Teleport check skipped.");
                    }
                    return;
                }

                if (currentRaidStatus && !(RaidConfig.AllowWaygateTeleports?.Value ?? true))
                {
                    foreach (var reqEntity in requests)
                    {
                        if (!em.Exists(reqEntity) || !em.HasComponent<TeleportationRequest>(reqEntity)) continue;

                        var requestData = em.GetComponentData<TeleportationRequest>(reqEntity);

                        if (!em.Exists(requestData.PlayerEntity) || !em.HasComponent<PlayerCharacter>(requestData.PlayerEntity)) continue;
                        var requestPlayerCharacter = em.GetComponentData<PlayerCharacter>(requestData.PlayerEntity);

                        Entity userEntity = requestPlayerCharacter.UserEntity;

                        if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity)) continue;
                        var requestUserObject = em.GetComponentData<User>(userEntity);

                        em.DestroyEntity(reqEntity);

                        LoggingHelper.Info($"Teleport request from {requestPlayerCharacter.Name} destroyed (Waygates disallowed during raid window).");

                        var message = new FixedString512Bytes($"You cannot use waygates during raid hours!");
                        ServerChatUtils.SendSystemMessageToClient(em, requestUserObject, ref message);
                    }
                }
            }
            finally
            {
                if (requests.IsCreated) requests.Dispose();
            }
        }
    }
}