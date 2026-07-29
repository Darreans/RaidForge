using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Services;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ServantCoffinstationActionSystem), nameof(ServantCoffinstationActionSystem.OnUpdate))]
    public static class ServantCoffinLimitPatch
    {
        private static void Prefix(ServantCoffinstationActionSystem __instance)
        {
            try
            {
                ProcessCoffinActionEvents(__instance);
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning(
                    "[ServantLimits] Failed while inspecting servant coffin action events; " +
                    "the original game actions were left intact.",
                    ex);
            }
        }

        private static void ProcessCoffinActionEvents(
            ServantCoffinstationActionSystem system)
        {
            bool detailedLogging =
                ServantLimitsConfig.EnableDetailedLogging?.Value == true;

            if (!Plugin.SystemsInitialized ||
                (!detailedLogging && !ServantLimitService.HasActiveLimits) ||
                system._EventQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            EntityManager entityManager = system.EntityManager;
            NativeArray<Entity> actionEntities = default;

            try
            {
                actionEntities =
                    system._EventQuery.ToEntityArray(Allocator.Temp);

                if (!TryGetNetworkIdLookup(
                        entityManager,
                        out NetworkIdLookupMap networkLookup))
                {
                    if (detailedLogging)
                    {
                        LoggingHelper.Warning(
                            $"[ServantLimits:Action] Found {actionEntities.Length} coffin action event(s), " +
                            "but the NetworkId lookup was unavailable.");
                    }

                    return;
                }

                foreach (Entity actionEntity in actionEntities)
                {
                    ProcessCoffinActionEvent(
                        entityManager,
                        actionEntity,
                        networkLookup,
                        detailedLogging);
                }
            }
            finally
            {
                if (actionEntities.IsCreated)
                {
                    actionEntities.Dispose();
                }
            }
        }

        private static void ProcessCoffinActionEvent(
            EntityManager entityManager,
            Entity actionEntity,
            NetworkIdLookupMap networkLookup,
            bool detailedLogging)
        {
            if (!entityManager.Exists(actionEntity) ||
                !entityManager.TryGetComponentData(
                    actionEntity,
                    out ServantCoffinActionEvent actionEvent) ||
                !entityManager.TryGetComponentData(
                    actionEntity,
                    out FromCharacter fromCharacter))
            {
                return;
            }

            bool workstationResolved =
                networkLookup.TryGetValue(
                    actionEvent.Workstation,
                    out Entity coffinEntity) &&
                entityManager.Exists(coffinEntity);

            bool targetResolved =
                TryGetConversionTarget(
                    entityManager,
                    fromCharacter,
                    out Entity conversionTarget,
                    out ServantConvertable convertable);

            if (detailedLogging)
            {
                string sourceDescription =
                    targetResolved
                        ? DescribeConversionTarget(
                            entityManager,
                            conversionTarget,
                            convertable.ConvertToUnit)
                        : "none";

                LoggingHelper.Info(
                    $"[ServantLimits:Action] event={DescribeEntity(actionEntity)}, " +
                    $"action={actionEvent.Action}, user={DescribeEntity(fromCharacter.User)}, " +
                    $"character={DescribeEntity(fromCharacter.Character)}, " +
                    $"workstationNetworkId={actionEvent.Workstation}, " +
                    $"coffin={(workstationResolved ? DescribeEntity(coffinEntity) : "unresolved")}, " +
                    $"dominatedTarget={sourceDescription}, activeLimits={ServantLimitService.HasActiveLimits}.");
            }

            if (!workstationResolved || !targetResolved)
            {
                return;
            }

            if (actionEvent.Action != ServantCoffinAction.Insert)
            {
                ServantLimitService.LogCountSnapshot(
                    entityManager,
                    coffinEntity,
                    fromCharacter.User,
                    convertable.ConvertToUnit,
                    actionEvent.Action.ToString());
                return;
            }

            bool allow = ServantLimitService.TryAllowInsert(
                entityManager,
                coffinEntity,
                fromCharacter.User,
                convertable.ConvertToUnit,
                out _,
                out _,
                out _);

            if (allow)
            {
                return;
            }

            entityManager.DestroyEntity(actionEntity);

            if (detailedLogging)
            {
                LoggingHelper.Info(
                    $"[ServantLimits:Action] Destroyed blocked Insert event={DescribeEntity(actionEntity)} " +
                    $"for coffin={DescribeEntity(coffinEntity)}.");
            }
        }

        private static bool TryGetNetworkIdLookup(
            EntityManager entityManager,
            out NetworkIdLookupMap networkLookup)
        {
            networkLookup = default;
            EntityQuery networkIdQuery = default;

            try
            {
                // NetworkIdSystem.Singleton lives on a system entity. Queries use
                // EntityQueryOptions.Default unless IncludeSystems is explicit,
                // which made every coffin action fail open before reaching the
                // limit check.
                networkIdQuery = entityManager.CreateEntityQuery(
                    new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<NetworkIdSystem.Singleton>()
                        },
                        Options = EntityQueryOptions.IncludeSystems
                    });

                if (networkIdQuery.IsEmptyIgnoreFilter)
                {
                    return false;
                }

                NetworkIdSystem.Singleton singleton =
                    networkIdQuery.GetSingleton<NetworkIdSystem.Singleton>();
                networkLookup = singleton.GetNetworkIdLookupRO();
                return networkLookup._NetworkIdToEntityMap.IsCreated;
            }
            finally
            {
                if (networkIdQuery != default)
                {
                    networkIdQuery.Dispose();
                }
            }
        }

        private static bool TryGetConversionTarget(
            EntityManager entityManager,
            FromCharacter fromCharacter,
            out Entity conversionTarget,
            out ServantConvertable convertable)
        {
            conversionTarget = Entity.Null;
            convertable = default;

            if (!entityManager.Exists(fromCharacter.Character) ||
                !entityManager.HasBuffer<FollowerBuffer>(fromCharacter.Character))
            {
                return false;
            }

            DynamicBuffer<FollowerBuffer> followers =
                entityManager.GetBuffer<FollowerBuffer>(fromCharacter.Character);

            foreach (FollowerBuffer follower in followers)
            {
                Entity followerEntity = follower.Entity._Entity;

                if (followerEntity == Entity.Null ||
                    !entityManager.Exists(followerEntity) ||
                    !entityManager.TryGetComponentData(
                        followerEntity,
                        out ServantConvertable followerConvertable))
                {
                    continue;
                }

                conversionTarget = followerEntity;
                convertable = followerConvertable;
                return true;
            }

            return false;
        }

        private static string DescribeConversionTarget(
            EntityManager entityManager,
            Entity conversionTarget,
            PrefabGUID servantPrefab)
        {
            string sourceName = "unknown";

            if (entityManager.TryGetComponentData(
                    conversionTarget,
                    out PrefabGUID sourcePrefab) &&
                PrefabGuidResolver.TryGetPrefabName(
                    sourcePrefab,
                    out string resolvedSourceName))
            {
                sourceName = resolvedSourceName;
            }

            string servantName =
                PrefabGuidResolver.TryGetPrefabName(
                    servantPrefab,
                    out string resolvedServantName)
                    ? resolvedServantName
                    : "unknown";

            return $"{DescribeEntity(conversionTarget)} {sourceName} -> " +
                $"{servantName} ({servantPrefab.GuidHash})";
        }

        private static string DescribeEntity(Entity entity)
        {
            return entity == Entity.Null
                ? "null"
                : $"{entity.Index}:{entity.Version}";
        }
    }
}
