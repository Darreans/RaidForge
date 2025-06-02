using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using Stunlock.Core;
using RaidForge.Data;
using RaidForge.Utils;
using System.Collections.Generic;
using ProjectM.Gameplay.Systems;
using ProjectM.Scripting;
using System;

//really its for my own tracking.

namespace RaidForge.Patches
{
    [HarmonyPatch]
    public static class GolemAbilityUsageLoggerPatch
    {
        private static readonly HashSet<PrefabGUID> TrackedGolemAbilityGUIDs = new HashSet<PrefabGUID>()
        {
            new PrefabGUID(2106313675),
            new PrefabGUID(395411669),
        };

        [HarmonyPatch(typeof(AbilityCastStarted_SpawnPrefabSystem_Server), nameof(AbilityCastStarted_SpawnPrefabSystem_Server.OnUpdate))]
        public static class AbilityCastStarted_SpawnPrefabSystem_Server_Patch
        {
            public static void Prefix(AbilityCastStarted_SpawnPrefabSystem_Server __instance)
            {
                if (!RaidForge.Config.TroubleshootingConfig.EnableVerboseLogging.Value) return;

                EntityManager em = __instance.EntityManager;
                ServerGameManager? SGM_nullable = VWorld.Server?.GetExistingSystemManaged<ServerScriptMapper>()?.GetServerGameManager();

                if (!SGM_nullable.HasValue)
                {
                    return;
                }
                ServerGameManager SGM = SGM_nullable.Value;

                EntityQuery eventQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AbilityCastStartedEvent>());
                NativeArray<Entity> eventEntities = eventQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    foreach (var eventEntity in eventEntities)
                    {
                        if (!em.Exists(eventEntity)) continue;
                        AbilityCastStartedEvent abilityCastEvent = em.GetComponentData<AbilityCastStartedEvent>(eventEntity);
                        Entity casterCharacterEntity = abilityCastEvent.Character;
                        if (!em.Exists(casterCharacterEntity)) continue;

                        if (em.HasComponent<PlayerCharacter>(casterCharacterEntity))
                        {
                            if (SGM.TryGetBuff(casterCharacterEntity, PrefabData.SiegeGolemBuff, out _))
                            {
                            }
                        }
                    }
                }
                catch (Exception e) { }
                finally { if (eventEntities.IsCreated) eventEntities.Dispose(); }
            }
        }
    }
}