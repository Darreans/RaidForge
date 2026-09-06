using System;
using HarmonyLib;
using HookDOTS.API.Attributes;
using ProjectM;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ReactToInventoryChangedSystem), nameof(ReactToInventoryChangedSystem.OnUpdate))]
    public static class ShardInventoryChangedPatch
    {
        public static void Prefix(ReactToInventoryChangedSystem __instance)
        {
            if (!ShardOwnershipService.IsEnabled) return;
            // Read the game's existing change-event query, not inventories across the world.
            var query = __instance.__query_2096870026_0;
            if (query.IsEmptyIgnoreFilter) return;
            NativeArray<Entity> events = default;
            try
            {
                events = query.ToEntityArray(Allocator.Temp);
                var em = __instance.EntityManager;
                foreach (var entity in events)
                    ShardOwnershipService.OnInventoryChanged(em, em.GetComponentData<InventoryChangedEvent>(entity));
            }
            catch (Exception ex) { LoggingHelper.Error("[ShardOwnership] Inventory event hook failed.", ex); }
            finally { if (events.IsCreated) events.Dispose(); }
        }
    }

    public static class ShardEquipmentChangedPatch
    {
        [EcsSystemUpdatePrefix(typeof(ReactToEquipmentChangedSystem))]
        public static unsafe void Prefix(SystemState* systemState)
        {
            if (!ShardOwnershipService.IsEnabled) return;
            var query = ((ReactToEquipmentChangedSystem*)systemState->m_SystemPtr)->__query_32297106_0;
            if (query.IsEmptyIgnoreFilter) return;
            NativeArray<Entity> events = default;
            try
            {
                events = query.ToEntityArray(Allocator.Temp);
                var em = systemState->EntityManager;
                foreach (var entity in events)
                    ShardOwnershipService.OnEquipmentChanged(em, em.GetComponentData<EquipmentChangedEvent>(entity));
            }
            catch (Exception ex) { LoggingHelper.Error("[ShardOwnership] Equipment event hook failed.", ex); }
            finally { if (events.IsCreated) events.Dispose(); }
        }
    }

    public static class ShardSourceDestroyedPatch
    {
        [EcsSystemUpdatePrefix(typeof(ProcessDestroyEventSystem))]
        public static unsafe void Prefix(SystemState* systemState)
        {
            if (!ShardOwnershipService.IsEnabled) return;
            var query = ((ProcessDestroyEventSystem*)systemState->m_SystemPtr)->_EventQuery;
            if (query.IsEmptyIgnoreFilter) return;
            NativeArray<Entity> events = default;
            try
            {
                events = query.ToEntityArray(Allocator.Temp);
                var em = systemState->EntityManager;
                foreach (var entity in events)
                    ShardOwnershipService.OnDestroyed(em, em.GetComponentData<DestroyTagEvent>(entity).Entity);
            }
            catch (Exception ex) { LoggingHelper.Error("[ShardOwnership] Destruction event hook failed.", ex); }
            finally { if (events.IsCreated) events.Dispose(); }
        }
    }
}
