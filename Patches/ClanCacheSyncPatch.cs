using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Clan;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using System.Collections.Generic;
using RaidForge.Services;
using RaidForge.Utils;         
using RaidForge.Config;       
using System;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ClanSystem_Server), nameof(ClanSystem_Server.OnUpdate))]
    public static class ClanCacheSync_OnUpdateClanStatus_Patch 
    {
        private static List<(Entity UserEntity, Entity PotentialNewClanEntity, string ActionType)> _potentialClanChangesThisFrame =
            new List<(Entity, Entity, string)>();
        private static Dictionary<Entity, Entity> _userClanStateBeforeUpdate =
            new Dictionary<Entity, Entity>();

        public static void Prefix(ClanSystem_Server __instance)
        {
            if (!Plugin.SystemsInitialized) return;

            _potentialClanChangesThisFrame.Clear();
            _userClanStateBeforeUpdate.Clear();

            EntityManager em = __instance.EntityManager;
            if (em == default) return;

            NativeArray<Entity> createEvents = default;
            NativeArray<Entity> responseEvents = default;
            EntityQuery createQuery = __instance._CreateClanEventQuery; 
            EntityQuery responseQuery = __instance._ClanInviteResponseQuery; 

            try
            {
                if (createQuery.CalculateEntityCount() > 0) 
                {
                    createEvents = createQuery.ToEntityArray(Allocator.TempJob);
                    foreach (var eventEntity in createEvents)
                    {
                        if (!em.Exists(eventEntity) || !em.HasComponent<FromCharacter>(eventEntity)) continue;

                        FromCharacter fromCharacter = em.GetComponentData<FromCharacter>(eventEntity);
                        if (em.TryGetComponentData<User>(fromCharacter.User, out var userCreator) && userCreator.ClanEntity._Entity == Entity.Null)
                        {
                            _userClanStateBeforeUpdate[fromCharacter.User] = userCreator.ClanEntity._Entity;
                            _potentialClanChangesThisFrame.Add((fromCharacter.User, Entity.Null, "Create"));
                            LoggingHelper.Debug($"[ClanCacheSyncPatch:Prefix] Potential CreateClan by User {fromCharacter.User}. Old Clan: {userCreator.ClanEntity._Entity}");
                        }
                    }
                }

                if (responseQuery.CalculateEntityCount() > 0) 
                {
                    responseEvents = responseQuery.ToEntityArray(Allocator.TempJob);
                    foreach (var eventEntity in responseEvents)
                    {
                        if (!em.Exists(eventEntity) ||
                            !em.HasComponent<ClanEvents_Client.ClanInviteResponse>(eventEntity) ||
                            !em.HasComponent<FromCharacter>(eventEntity)) continue;

                        ClanEvents_Client.ClanInviteResponse inviteResponse = em.GetComponentData<ClanEvents_Client.ClanInviteResponse>(eventEntity);

                        if (inviteResponse.Response == InviteRequestResponse.Accept)
                        {
                            FromCharacter fromCharacter = em.GetComponentData<FromCharacter>(eventEntity);
                            NetworkId targetClanNetId = inviteResponse.ClanId;

                            if (ClanUtilities.TryGetClanEntityByNetworkId(em, targetClanNetId, out Entity resolvedClanEntity))
                            {
                                _potentialClanChangesThisFrame.Add((fromCharacter.User, resolvedClanEntity, "JoinAccept"));
                                LoggingHelper.Debug($"[ClanCacheSyncPatch:Prefix] Potential JoinAccept: User {fromCharacter.User} to Clan {resolvedClanEntity} (NetId: {targetClanNetId})");
                            }
                            else
                            {
                                LoggingHelper.Warning($"[ClanCacheSyncPatch:Prefix] Could not resolve ClanEntity for ClanNetId {targetClanNetId} from invite response for User {fromCharacter.User}.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("[ClanCacheSyncPatch:Prefix] Error during event pre-scan: " + ex.ToString());
                _potentialClanChangesThisFrame.Clear();
                _userClanStateBeforeUpdate.Clear();
            }
            finally
            {
                if (createEvents.IsCreated) createEvents.Dispose();
                if (responseEvents.IsCreated) responseEvents.Dispose();
              
            }
        }

        public static void Postfix(ClanSystem_Server __instance)
        {
            if (!Plugin.SystemsInitialized || _potentialClanChangesThisFrame.Count == 0)
            {
                
                return;
            }

            EntityManager em = __instance.EntityManager;
            if (em == default)
            {
                LoggingHelper.Error("[ClanCacheSyncPatch:Postfix] EntityManager is default.");
                _potentialClanChangesThisFrame.Clear();
                _userClanStateBeforeUpdate.Clear();
                return;
            }

            bool verboseLoggingEnabled = TroubleshootingConfig.EnableVerboseLogging?.Value ?? false;
            if (verboseLoggingEnabled)
                LoggingHelper.Debug($"[ClanCacheSyncPatch:Postfix] Processing {_potentialClanChangesThisFrame.Count} potential clan changes.");

            foreach (var change in _potentialClanChangesThisFrame)
            {
                try
                {
                    if (!em.Exists(change.UserEntity) || !em.HasComponent<User>(change.UserEntity)) continue;

                    User currentUserData = em.GetComponentData<User>(change.UserEntity);
                    Entity actualNewClanEntity = currentUserData.ClanEntity._Entity;

                    if (change.ActionType == "Create")
                    {
                        _userClanStateBeforeUpdate.TryGetValue(change.UserEntity, out Entity previousClanInCache);
                        if (actualNewClanEntity != Entity.Null && actualNewClanEntity != previousClanInCache)
                        {
                            LoggingHelper.Info($"[ClanCacheSyncPatch:Postfix] Clan Creation successful for User {change.UserEntity}. New Clan: {actualNewClanEntity}. Updating cache.");
                            OwnershipCacheService.UpdateUserClan(change.UserEntity, actualNewClanEntity, em);
                        }
                    }
                    else if (change.ActionType == "JoinAccept")
                    {
                        if (actualNewClanEntity != Entity.Null && actualNewClanEntity == change.PotentialNewClanEntity)
                        {
                            LoggingHelper.Info($"[ClanCacheSyncPatch:Postfix] Clan Join Accept successful for User {change.UserEntity} to Clan {change.PotentialNewClanEntity}. Updating cache.");
                            OwnershipCacheService.UpdateUserClan(change.UserEntity, change.PotentialNewClanEntity, em);
                        }
                        else if (actualNewClanEntity != change.PotentialNewClanEntity && verboseLoggingEnabled)
                        {
                            LoggingHelper.Debug($"[ClanCacheSyncPatch:Postfix] Clan Join for User {change.UserEntity} to Clan {change.PotentialNewClanEntity} did not result in expected assignment. Actual new clan: {actualNewClanEntity}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error($"[ClanCacheSyncPatch:Postfix] Error processing User {change.UserEntity} for action {change.ActionType}: {ex.ToString()}");
                }
            }
            
        }
    }
}