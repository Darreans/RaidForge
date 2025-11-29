using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using RaidForge.Config;
using RaidForge.Utils;
using Stunlock.Core;
using ProjectM.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(TeleportationRequestSystem), nameof(TeleportationRequestSystem.OnUpdate))]
    public static class TeleportationRestrictionsPatch
    {
        private const float EXEMPT_RADIUS = 5.0f;
        private const float EXEMPT_TELEPORTER_RADIUS_SQUARED = EXEMPT_RADIUS * EXEMPT_RADIUS;

        private static readonly List<float3> EXEMPT_TELEPORTER_LOCATIONS = new List<float3>
        {
            new float3(731.34955f, 15f, -2987.3306f),
        };

        private static readonly PrefabGUID ALWAYS_ALLOW_TELEPORT_BUFF_GUID = new PrefabGUID(1111481396);

        private static void Prefix(TeleportationRequestSystem __instance)
        {
            if (!Plugin.SystemsInitialized)
            {
                return;
            }

            bool allowWaygateConfig = RaidConfig.AllowWaygateTeleports?.Value ?? true;

            var em = __instance.EntityManager;
            var eventEntities = __instance._TeleportRequestQuery.ToEntityArray(Allocator.TempJob);

            try
            {
                if (eventEntities.Length == 0) return;

                bool currentRaidStatus;
                if (VWorld.IsServerWorldReady() && VWorld.GameBalanceSettings(out var balance) && VWorld.ZDateTime(out var dt))
                {
                    currentRaidStatus = balance.IsCastlePvPEnabled(dt);
                }
                else
                {
                    return;
                }

                if (currentRaidStatus && !allowWaygateConfig)
                {
                    World world = VWorld.Server;
                    ServerScriptMapper serverScriptMapper = null;
                    ServerGameManager sgm = default;
                    bool sgmSystemsReady = false;

                    if (world != null && world.IsCreated)
                    {
                        serverScriptMapper = world.GetExistingSystemManaged<ServerScriptMapper>();
                        if (serverScriptMapper != null)
                        {
                            sgm = serverScriptMapper.GetServerGameManager();
                            sgmSystemsReady = true;
                        }
                    }

                    foreach (var eventEntity in eventEntities)
                    {
                        if (!em.Exists(eventEntity) || !em.HasComponent<TeleportationRequest>(eventEntity)) continue;

                        var requestData = em.GetComponentData<TeleportationRequest>(eventEntity);
                        Entity playerCharacterEntity = requestData.PlayerEntity;

                        if (!em.Exists(playerCharacterEntity) || !em.HasComponent<PlayerCharacter>(playerCharacterEntity)) continue;
                        var requestPlayerCharacter = em.GetComponentData<PlayerCharacter>(playerCharacterEntity);
                        Entity userEntity = requestPlayerCharacter.UserEntity;

                        if (!em.Exists(userEntity) || !em.HasComponent<User>(userEntity)) continue;
                        var requestUserObject = em.GetComponentData<User>(userEntity);

                        if (sgmSystemsReady)
                        {
                            try
                            {
                                if (sgm.TryGetBuff(playerCharacterEntity, ALWAYS_ALLOW_TELEPORT_BUFF_GUID, out _))
                                {
                                    continue;
                                }
                            }
                            catch (Exception)
                            {

                            }
                        }

                        bool isExemptByRadius = false;
                        if (EXEMPT_TELEPORTER_LOCATIONS.Any() && em.HasComponent<Translation>(playerCharacterEntity))
                        {
                            Translation playerTranslation = em.GetComponentData<Translation>(playerCharacterEntity);

                            foreach (float3 exemptLocation in EXEMPT_TELEPORTER_LOCATIONS)
                            {
                                float distanceSq = math.distancesq(playerTranslation.Value, exemptLocation);
                                if (distanceSq <= EXEMPT_TELEPORTER_RADIUS_SQUARED)
                                {
                                    isExemptByRadius = true;
                                    break;
                                }
                            }
                        }

                        if (isExemptByRadius)
                        {
                            continue;
                        }

                        em.DestroyEntity(eventEntity);
                        var message = new FixedString512Bytes(ChatColors.WarningText("You cannot use waygates during raid hours!"));
                        ServerChatUtils.SendSystemMessageToClient(em, requestUserObject, ref message);
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