using System;
using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using RaidForge.Config;
using RaidForge.Core;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(MapIconSpawnSystem), nameof(MapIconSpawnSystem.OnUpdate))]
    public static class MapIconSpawnPatch
    {
        private static EntityQuery _mapIconPositionQuery;
        private static EntityManager _cachedEntityManager;
        private static bool _queryInitialized;

        public static void Prefix(MapIconSpawnSystem __instance)
        {
            try
            {
                if (!MapIconsConfig.EnableDecayRaidMapIcon.Value &&
                    !MapIconsConfig.EnableOfflineRaidMapIcon.Value &&
                    !MapIconsConfig.EnableOptInRaidMapIcon.Value &&
                    !MapIconsConfig.EnableOptOutRaidMapIcon.Value) return;
                if (!RaidMapIconService.HasActiveIcons) return;

                var em = __instance.EntityManager;
                ApplyRaidForgeIconData(em, GetMapIconPositionQuery(em));
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning("[MapIconSpawnPatch] Failed to apply RaidForge map icon visibility settings.", ex);
            }
        }

        private static EntityQuery GetMapIconPositionQuery(EntityManager em)
        {
            if (!_queryInitialized || _cachedEntityManager != em)
            {
                _mapIconPositionQuery = em.CreateEntityQuery(
                    ComponentType.ReadWrite<MapIconPosition>(),
                    ComponentType.ReadOnly<Translation>());
                _cachedEntityManager = em;
                _queryInitialized = true;
            }

            return _mapIconPositionQuery;
        }

        private static void ApplyRaidForgeIconData(EntityManager em, EntityQuery mapIconQuery)
        {
            NativeArray<Entity> entities = default;

            try
            {
                if (mapIconQuery.IsEmptyIgnoreFilter) return;

                entities = mapIconQuery.ToEntityArray(Allocator.Temp);

                foreach (var entity in entities)
                {
                    if (!em.Exists(entity) ||
                        !em.HasComponent<MapIconData>(entity) ||
                        !em.HasComponent<Attach>(entity))
                    {
                        continue;
                    }

                    var attachParent = em.GetComponentData<Attach>(entity).Parent;
                    if (attachParent == Entity.Null ||
                        !em.Exists(attachParent) ||
                        !RaidMapIconService.IsRaidIconProxy(attachParent))
                    {
                        continue;
                    }

                    if (!em.HasComponent<SpawnedBy>(attachParent)) continue;

                    var mapIconData = em.GetComponentData<MapIconData>(entity);
                    mapIconData.RequiresReveal = false;
                    mapIconData.ShowOutsideVision = true;
                    mapIconData.ShowOnMinimap = true;
                    mapIconData.AllySetting = MapIconShowSettings.Global;
                    mapIconData.EnemySetting = MapIconShowSettings.Global;
                    em.SetComponentData(entity, mapIconData);
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
            }
        }
    }
}
