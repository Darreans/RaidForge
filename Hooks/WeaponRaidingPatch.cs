using HookDOTS.API.Attributes;
using Unity.Entities;
using ProjectM;
using BepInEx.Logging;
using ProjectM.CastleBuilding;
using ProjectM.Gameplay.Systems;
using Unity.Collections;
using RaidForge.Config;

namespace RaidForge.Hooks
{
    public static class WeaponRaidingHook
    {
        private static ManualLogSource _log = Logger.CreateLogSource("RaidForge-WeaponHook");

        [EcsSystemUpdatePrefix(typeof(DealDamageSystem))]
        public static unsafe void Prefix(SystemState* systemState)
        {
            if (!WeaponRaidingConfig.EnableWeaponRaiding.Value) return;

            var entityManager = systemState->EntityManager;
            var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<DealDamageEvent>());
            var eventEntities = query.ToEntityArray(Allocator.Temp);

            try
            {
                if (eventEntities.Length == 0) return;

                foreach (var entity in eventEntities)
                {
                    var originalEvent = entityManager.GetComponentData<DealDamageEvent>(entity);

                    bool isWall = entityManager.HasComponent<CastleHeartConnection>(originalEvent.Target);
                    bool isHeart = entityManager.HasComponent<CastleHeart>(originalEvent.Target);

                    if (!isWall && !isHeart) continue;

                    if (!entityManager.HasComponent<EntityOwner>(originalEvent.SpellSource)) continue;
                    var ownerEntity = entityManager.GetComponentData<EntityOwner>(originalEvent.SpellSource).Owner;
                    if (!entityManager.HasComponent<PlayerCharacter>(ownerEntity)) continue;

                    
                    if (originalEvent.MaterialModifiers.StoneStructure > 0f) continue;

                   
                    if (TroubleshootingConfig.EnableVerboseLogging.Value)
                    {
                        _log.LogInfo($"[Detection] Player hit {originalEvent.Target.Index}. Current Mod: {originalEvent.MaterialModifiers.StoneStructure}");
                    }

                    var modifiedModifiers = originalEvent.MaterialModifiers;
                    modifiedModifiers.StoneStructure = WeaponRaidingConfig.WeaponDamageVsStoneMultiplier.Value;

                    var newEvent = originalEvent;
                    newEvent.MaterialModifiers = modifiedModifiers;

                    entityManager.SetComponentData(entity, newEvent);

                    if (TroubleshootingConfig.EnableVerboseLogging.Value)
                    {
                        _log.LogInfo($"[Action] Overwrote StoneStructure modifier to {modifiedModifiers.StoneStructure}");
                    }
                }
            }
            finally
            {
                if (eventEntities.IsCreated) eventEntities.Dispose();
            }
        }
    }
}