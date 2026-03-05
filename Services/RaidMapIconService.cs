using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace RaidForge.Services
{
	public static class RaidMapIconService
	{
		private static Dictionary<Entity, (Entity IconProxy, DateTime LastHit, bool IsDecay)> _activeRaidIcons = new();

		public static void AddRaidIcon(Entity castleHeartEntity, bool isOfflineRaid, bool isDecayRaid)
		{
			if (!VWorld.IsServerWorldReady()) return;
			var em = VWorld.EntityManager;

			if (!em.Exists(castleHeartEntity) || !em.HasComponent<Translation>(castleHeartEntity)) return;

			if (isOfflineRaid && !MapIconsConfig.EnableOfflineRaidMapIcon.Value) return;
			if (isDecayRaid && !MapIconsConfig.EnableDecayRaidMapIcon.Value) return;

			if (_activeRaidIcons.TryGetValue(castleHeartEntity, out var iconData))
			{
				if (em.Exists(iconData.IconProxy))
				{
					_activeRaidIcons[castleHeartEntity] = (iconData.IconProxy, DateTime.UtcNow, isDecayRaid);
					return;
				}
			}

			int guidValue = isOfflineRaid
				? MapIconsConfig.OfflineRaidMapIconPrefabGuid.Value
				: MapIconsConfig.DecayRaidMapIconPrefabGuid.Value;

			PrefabGUID iconPrefab = new PrefabGUID(guidValue);

			var prefabCollection = VWorld.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
			if (!prefabCollection._SpawnableNameToPrefabGuidDictionary.TryGetValue("MapIcon_ProxyObject_POI_Unknown", out var proxyPrefabGUID)) return;
			if (!prefabCollection._PrefabGuidToEntityMap.TryGetValue(proxyPrefabGUID, out var proxyPrefabEntity)) return;

			var pos = em.GetComponentData<Translation>(castleHeartEntity).Value;

			var mapIconProxy = em.Instantiate(proxyPrefabEntity);
			em.SetComponentData(mapIconProxy, new Translation { Value = pos });
			em.AddComponentData(mapIconProxy, new SpawnedBy { Value = castleHeartEntity });

			if (em.HasComponent<SyncToUserBitMask>(mapIconProxy)) em.RemoveComponent<SyncToUserBitMask>(mapIconProxy);
			if (em.HasBuffer<SyncToUserBuffer>(mapIconProxy)) em.GetBuffer<SyncToUserBuffer>(mapIconProxy).Clear();
			if (em.HasComponent<OnlySyncToUsersTag>(mapIconProxy)) em.RemoveComponent<OnlySyncToUsersTag>(mapIconProxy);

			var attachBuffer = em.GetBuffer<AttachMapIconsToEntity>(mapIconProxy);
			attachBuffer.Clear();
			attachBuffer.Add(new AttachMapIconsToEntity { Prefab = iconPrefab });

			_activeRaidIcons[castleHeartEntity] = (mapIconProxy, DateTime.UtcNow, isDecayRaid);
		}

		public static void RemoveRaidIcon(Entity castleHeartEntity)
		{
			if (_activeRaidIcons.TryGetValue(castleHeartEntity, out var iconData))
			{
				if (VWorld.IsServerWorldReady() && VWorld.EntityManager.Exists(iconData.IconProxy))
				{
					var em = VWorld.EntityManager;
					if (em.HasBuffer<AttachedBuffer>(iconData.IconProxy))
					{
						var attached = em.GetBuffer<AttachedBuffer>(iconData.IconProxy);
						for (int i = 0; i < attached.Length; i++)
						{
							Entity attachedEntity = attached[i].Entity;
							if (em.Exists(attachedEntity)) em.AddComponent<DestroyTag>(attachedEntity);
						}
					}
					em.AddComponent<DestroyTag>(iconData.IconProxy);
				}
				_activeRaidIcons.Remove(castleHeartEntity);
			}
		}

		public static void ProcessCleanup()
		{
			if (!VWorld.IsServerWorldReady() || _activeRaidIcons.Count == 0) return;
			var em = VWorld.EntityManager;
			int timeoutSeconds = MapIconsConfig.RaidMapIconTimeoutSeconds.Value;
			DateTime nowUtc = DateTime.UtcNow;

			var heartsToCheck = _activeRaidIcons.Keys.ToList();
			foreach (var heart in heartsToCheck)
			{
				var iconData = _activeRaidIcons[heart];

				if (!em.Exists(heart) || !em.HasComponent<CastleHeart>(heart))
				{
					RemoveRaidIcon(heart);
					continue;
				}

				if (!iconData.IsDecay && !OfflineProtectionService.AreAllDefendersActuallyOffline(heart, em))
				{
					RemoveRaidIcon(heart);
					continue;
				}

				if ((nowUtc - iconData.LastHit).TotalSeconds > timeoutSeconds)
				{
					RemoveRaidIcon(heart);
				}
			}
		}

		public static void RemoveAllIcons()
		{
			var keys = _activeRaidIcons.Keys.ToList();
			foreach (var k in keys) RemoveRaidIcon(k);
		}
	}

	[HarmonyPatch(typeof(MapIconSpawnSystem), nameof(MapIconSpawnSystem.OnUpdate))]
	public static class MapIconSpawnSystemPatch
	{
		public static void Prefix(MapIconSpawnSystem __instance)
		{
			if (!MapIconsConfig.EnableDecayRaidMapIcon.Value && !MapIconsConfig.EnableOfflineRaidMapIcon.Value) return;

			var em = __instance.EntityManager;
			var query = em.CreateEntityQuery(ComponentType.ReadWrite<MapIconData>(), ComponentType.ReadOnly<Attach>());
			var entities = query.ToEntityArray(Allocator.Temp);

			try
			{
				foreach (var entity in entities)
				{
					if (!em.HasComponent<Attach>(entity)) continue;
					var attachParent = em.GetComponentData<Attach>(entity).Parent;
					if (attachParent == Entity.Null || !em.Exists(attachParent) || !em.HasComponent<SpawnedBy>(attachParent)) continue;

					var spawnedByEntity = em.GetComponentData<SpawnedBy>(attachParent).Value;
					if (spawnedByEntity == Entity.Null || !em.Exists(spawnedByEntity) || !em.HasComponent<CastleHeart>(spawnedByEntity)) continue;

					var mapIconData = em.GetComponentData<MapIconData>(entity);
					mapIconData.RequiresReveal = false;
					mapIconData.AllySetting = MapIconShowSettings.Global;
					mapIconData.EnemySetting = MapIconShowSettings.Global;
					em.SetComponentData(entity, mapIconData);
				}
			}
			finally
			{
				entities.Dispose();
				query.Dispose();
			}
		}
	}
}