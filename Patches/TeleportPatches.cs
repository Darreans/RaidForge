using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Scripting;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Systems;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(TeleportationRequestSystem), nameof(TeleportationRequestSystem.OnUpdate))]
    public static class TeleportationRestrictionsPatch
    {
        private static void Prefix(TeleportationRequestSystem __instance)
        {
            try
            {
                PrefixCore(__instance);
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning("[Teleport] Error while applying waygate raid-hour restrictions.", ex);
            }
        }

        private static void PrefixCore(TeleportationRequestSystem __instance)
        {
            if (!Plugin.SystemsInitialized)
            {
                return;
            }

            bool allowWaygateConfig = RaidConfig.AllowWaygateTeleports?.Value ?? true;

            if (allowWaygateConfig)
            {
                return;
            }

            if (!VWorld.IsServerWorldReady())
            {
                return;
            }

            var em = __instance.EntityManager;
            if (__instance._TeleportRequestQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            if (!RaidToggleSystem.AreRaidsEnabledLive())
            {
                return;
            }

            var eventEntities = __instance._TeleportRequestQuery.ToEntityArray(Allocator.TempJob);

            try
            {
                foreach (var eventEntity in eventEntities)
                {
                    if (!em.Exists(eventEntity) || !em.HasComponent<TeleportationRequest>(eventEntity))
                    {
                        continue;
                    }

                    var requestData = em.GetComponentData<TeleportationRequest>(eventEntity);
                    Entity playerCharacterEntity = requestData.PlayerEntity;

                    if (!em.Exists(playerCharacterEntity) ||
                        !em.HasComponent<PlayerCharacter>(playerCharacterEntity))
                    {
                        continue;
                    }

                    var requestPlayerCharacter = em.GetComponentData<PlayerCharacter>(playerCharacterEntity);
                    Entity userEntity = requestPlayerCharacter.UserEntity;

                    if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity))
                    {
                        continue;
                    }

                    Entity activeNetherReturnWaypoint = GetActiveReturnToNetherWaypoint(em, playerCharacterEntity);
                    bool hasActiveNetherReturn = activeNetherReturnWaypoint != Entity.Null;
                    bool isActiveNetherReturnTeleport = IsActiveNetherReturnTeleport(em, requestData, activeNetherReturnWaypoint);
                    var requestUserObject = em.GetComponentData<User>(userEntity);
                    bool isAdmin = requestUserObject.IsAdmin;
                    bool isBlockableType = ShouldBlockTeleportType(requestData.TeleportationType);
                    bool isAllowedSpecialTeleport = IsAllowedSpecialTeleportTarget(em, requestData.FromTarget) ||
                        IsAllowedSpecialTeleportTarget(em, requestData.ToTarget);

                    LogTeleportRequest(
                        em,
                        requestData,
                        hasActiveNetherReturn,
                        isActiveNetherReturnTeleport,
                        activeNetherReturnWaypoint,
                        isAdmin,
                        isBlockableType,
                        isAllowedSpecialTeleport);

                    if (!isBlockableType)
                    {
                        continue;
                    }

                    if (isAdmin || isActiveNetherReturnTeleport || isAllowedSpecialTeleport)
                    {
                        continue;
                    }

                    em.DestroyEntity(eventEntity);
                    var message = new FixedString512Bytes(ChatColors.WarningText("You cannot use waygates during raid hours!"));
                    ServerChatUtils.SendSystemMessageToClient(em, requestUserObject, ref message);
                }
            }
            finally
            {
                if (eventEntities.IsCreated)
                {
                    eventEntities.Dispose();
                }
            }
        }

        private static bool ShouldBlockTeleportType(TeleportationType teleportationType)
        {
            return teleportationType == TeleportationType.Waypoint_To_ChunkWaypoint ||
                teleportationType == TeleportationType.ChunkWaypoint_To_CastleWaypoint;
        }

        private static Entity GetActiveReturnToNetherWaypoint(EntityManager em, Entity playerCharacterEntity)
        {
            if (!em.TryGetComponentData(playerCharacterEntity, out ReturnToNetherWaypoint returnToNetherWaypoint))
            {
                return Entity.Null;
            }

            Entity waypointEntity = returnToNetherWaypoint.WaypointEntity;
            return waypointEntity != Entity.Null && em.Exists(waypointEntity) ? waypointEntity : Entity.Null;
        }

        private static bool IsActiveNetherReturnTeleport(EntityManager em, TeleportationRequest requestData, Entity activeNetherReturnWaypoint)
        {
            if (activeNetherReturnWaypoint == Entity.Null || !em.Exists(activeNetherReturnWaypoint))
            {
                return false;
            }

            return requestData.FromTarget == activeNetherReturnWaypoint ||
                requestData.ToTarget == activeNetherReturnWaypoint;
        }

        private static bool IsAllowedSpecialTeleportTarget(EntityManager em, Entity target)
        {
            if (target == Entity.Null || !em.Exists(target))
            {
                return false;
            }

            if (em.HasComponent<ReturnToNetherWaypoint>(target) ||
                em.HasComponent<StartGraveyardExitWaypoint>(target) ||
                em.HasComponent<ActivateDraculaWarpRift>(target) ||
                em.HasComponent<Script_Dracula_EndGamePortal_Tag>(target))
            {
                return true;
            }

            if (!em.TryGetComponentData(target, out PrefabGUID prefabGuid) ||
                !PrefabGuidResolver.TryGetPrefabName(prefabGuid, out string prefabName))
            {
                return false;
            }

            return prefabName.IndexOf("Nether", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prefabName.IndexOf("Dracula", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prefabName.IndexOf("WarpRift", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LogTeleportRequest(
            EntityManager em,
            TeleportationRequest requestData,
            bool hasActiveNetherReturn,
            bool isActiveNetherReturnTeleport,
            Entity activeNetherReturnWaypoint,
            bool isAdmin,
            bool isBlockableType,
            bool isAllowedSpecialTeleport)
        {
            if (TroubleshootingConfig.EnableVerboseLogging?.Value != true)
            {
                return;
            }

            LoggingHelper.Info(
                "[Teleport] Request " +
                $"type={requestData.TeleportationType}, " +
                $"from={DescribeTeleportTarget(em, requestData.FromTarget)}, " +
                $"to={DescribeTeleportTarget(em, requestData.ToTarget)}, " +
                $"admin={isAdmin}, " +
                $"activeNetherReturn={hasActiveNetherReturn}, " +
                $"netherReturnTeleport={isActiveNetherReturnTeleport}, " +
                $"netherReturnWaypoint={DescribeTeleportTarget(em, activeNetherReturnWaypoint)}, " +
                $"allowedSpecial={isAllowedSpecialTeleport}, " +
                $"blockedType={isBlockableType}");
        }

        private static string DescribeTeleportTarget(EntityManager em, Entity target)
        {
            if (target == Entity.Null)
            {
                return "null";
            }

            if (!em.Exists(target))
            {
                return $"{target.Index}:{target.Version} missing";
            }

            if (!em.TryGetComponentData(target, out PrefabGUID prefabGuid))
            {
                return $"{target.Index}:{target.Version} no PrefabGUID";
            }

            string prefabName = PrefabGuidResolver.TryGetPrefabName(prefabGuid, out string resolvedName)
                ? resolvedName
                : "unknown";

            return $"{target.Index}:{target.Version} {prefabName} ({prefabGuid.GuidHash})";
        }
    }
}
