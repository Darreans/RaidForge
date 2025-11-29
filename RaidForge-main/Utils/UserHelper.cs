using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Collections;
using System;
using RaidForge.Data;
using ProjectM.CastleBuilding;
using System.Linq;
using System.Collections.Generic;
using Stunlock.Core;
using RaidForge.Services;

namespace RaidForge.Utils
{
    public static class UserHelper
    {
        public static bool IsVulnerableDueToShard(EntityManager em, Entity userEntity, out string reason)
        {
            reason = "No shards found.";
            if (!em.Exists(userEntity) || !em.TryGetComponentData<User>(userEntity, out var userData))
            {
                return false;
            }

            Entity characterEntity = userData.LocalCharacter._Entity;

            if (characterEntity.Exists())
            {
                if (HasSoulShardOnPerson(em, characterEntity, out reason))
                {
                    return true;
                }
            }

            Entity userClan = userData.ClanEntity._Entity;
            var ownedHeartEntities = new HashSet<Entity>();

            var allHeartsInCache = OwnershipCacheService.GetHeartToOwnerCacheView();
            foreach (var pair in allHeartsInCache)
            {
                if (!em.Exists(pair.Key) || !em.Exists(pair.Value)) continue;

                bool isMatch = false;
                if (pair.Value == userEntity) isMatch = true;

                if (!isMatch && userClan.Exists())
                {
                    if (OwnershipCacheService.TryGetUserClan(pair.Value, out Entity heartOwnerClan) && heartOwnerClan == userClan)
                    {
                        isMatch = true;
                    }
                }

                if (isMatch)
                {
                    ownedHeartEntities.Add(pair.Key);
                }
            }

            if (!ownedHeartEntities.Any())
            {
                return false;
            }

            var pedestalGuids = new HashSet<PrefabGUID>(PrefabData.PedestalToExpectedShardMap.Keys);
            var allConnectionsQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CastleHeartConnection>(), ComponentType.ReadOnly<PrefabGUID>());
            var allConnectionEntities = allConnectionsQuery.ToEntityArray(Allocator.Temp);

            try
            {
                foreach (var connectionEntity in allConnectionEntities)
                {
                    if (ownedHeartEntities.Contains(em.GetComponentData<CastleHeartConnection>(connectionEntity).CastleHeartEntity._Entity))
                    {
                        if (pedestalGuids.Contains(em.GetComponentData<PrefabGUID>(connectionEntity)))
                        {
                            if (em.TryGetBuffer<AttachedBuffer>(connectionEntity, out var attachedItems))
                            {
                                foreach (var attachedItem in attachedItems)
                                {
                                    var potentialInventoryEntity = attachedItem.Entity;
                                    if (potentialInventoryEntity.Exists() && em.TryGetComponentData<PrefabGUID>(potentialInventoryEntity, out var attachedGuid))
                                    {
                                        if (attachedGuid == PrefabData.ExternalInventoryPrefab)
                                        {
                                            if (em.TryGetBuffer<InventoryBuffer>(potentialInventoryEntity, out var inventoryBuffer))
                                            {
                                                foreach (var inventoryItem in inventoryBuffer)
                                                {
                                                    if (PrefabData.SoulShardPrefabGUIDs.Contains(inventoryItem.ItemType))
                                                    {
                                                        reason = $"a Soul Shard ({inventoryItem.ItemType}) was found inside a pedestal's inventory.";
                                                        return true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (allConnectionEntities.IsCreated) allConnectionEntities.Dispose();
                allConnectionsQuery.Dispose();
            }

            return false;
        }

        public static bool HasSoulShardOnPerson(EntityManager em, Entity characterEntity, out string location)
        {
            location = "inventory";
            if (InventoryUtilities.TryGetInventoryEntity(em, characterEntity, out Entity inventoryEntity))
            {
                if (em.HasComponent<InventoryBuffer>(inventoryEntity))
                {
                    var inventory = em.GetBuffer<InventoryBuffer>(inventoryEntity);
                    foreach (var item in inventory)
                    {
                        if (em.Exists(item.ItemEntity._Entity) && em.TryGetComponentData<PrefabGUID>(item.ItemEntity._Entity, out var prefabGuid))
                        {
                            if (PrefabData.SoulShardPrefabGUIDs.Contains(prefabGuid))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            if (em.TryGetComponentData<Equipment>(characterEntity, out var equipment))
            {
                var grimoireEntity = equipment.GrimoireSlot.SlotEntity._Entity;
                if (grimoireEntity.Exists() && em.TryGetComponentData<PrefabGUID>(grimoireEntity, out var grimoireGuid))
                {
                    if (PrefabData.SoulShardPrefabGUIDs.Contains(grimoireGuid))
                    {
                        location = "equipped Grimoire";
                        return true;
                    }
                }
            }

            return false;
        }

        #region Existing Methods
        public static bool FindUserEntity(EntityManager em, string identifier,
                                          out Entity userEntity, out User userData, out string characterName)
        {
            userEntity = Entity.Null;
            userData = default;
            characterName = string.Empty;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            EntityQuery userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
            NativeArray<Entity> allUserEntities = userQuery.ToEntityArray(Allocator.Temp);

            try
            {
                bool isSteamIdSearch = ulong.TryParse(identifier, out ulong steamId);

                foreach (Entity potentialUserEntity in allUserEntities)
                {
                    if (!em.Exists(potentialUserEntity) || !em.HasComponent<User>(potentialUserEntity))
                    {
                        continue;
                    }

                    User potentialUserData = em.GetComponentData<User>(potentialUserEntity);

                    if (isSteamIdSearch)
                    {
                        if (potentialUserData.PlatformId == steamId)
                        {
                            userEntity = potentialUserEntity;
                            userData = potentialUserData;
                            characterName = potentialUserData.CharacterName.ToString();
                            return true;
                        }
                    }
                    else
                    {
                        string currentCharacterName = potentialUserData.CharacterName.ToString();
                        if (currentCharacterName.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            userEntity = potentialUserEntity;
                            userData = potentialUserData;
                            characterName = currentCharacterName;
                            return true;
                        }
                    }
                }
            }
            finally
            {
                if (allUserEntities.IsCreated)
                {
                    allUserEntities.Dispose();
                }
            }

            return false;
        }

        public static bool TryGetPlayerOwnerFromSource(EntityManager em, Entity sourceEntity,
                                                       out Entity playerCharacterEntity, out Entity userEntity)
        {
            playerCharacterEntity = Entity.Null;
            userEntity = Entity.Null;

            if (!em.Exists(sourceEntity)) return false;

            if (em.HasComponent<PlayerCharacter>(sourceEntity))
            {
                playerCharacterEntity = sourceEntity;
                if (em.TryGetComponentData<PlayerCharacter>(playerCharacterEntity, out var pc))
                {
                    userEntity = pc.UserEntity;
                    return em.Exists(userEntity);
                }
                return false;
            }

            if (em.HasComponent<EntityOwner>(sourceEntity))
            {
                Entity directOwner = em.GetComponentData<EntityOwner>(sourceEntity).Owner;
                if (em.Exists(directOwner))
                {
                    if (em.HasComponent<PlayerCharacter>(directOwner))
                    {
                        playerCharacterEntity = directOwner;
                        if (em.TryGetComponentData<PlayerCharacter>(playerCharacterEntity, out var pcOwner))
                        {
                            userEntity = pcOwner.UserEntity;
                            return em.Exists(userEntity);
                        }
                        return false;
                    }
                }
            }
            return false;
        }

        public static bool IsAnyClanMemberOnline(EntityManager em, Entity clanEntity, Entity excludingUserEntity = default)
        {
            if (!em.Exists(clanEntity) || !em.HasComponent<ClanTeam>(clanEntity))
            {
                return false;
            }

            var clanMembers = new NativeList<Entity>(Allocator.Temp);
            try
            {
                ProjectM.TeamUtility.GetClanMembers(em, clanEntity, clanMembers);
                foreach (Entity memberUserEntity in clanMembers)
                {
                    if (excludingUserEntity != default && memberUserEntity == excludingUserEntity)
                    {
                        continue;
                    }

                    if (em.Exists(memberUserEntity) && em.HasComponent<User>(memberUserEntity))
                    {
                        User memberUserData = em.GetComponentData<User>(memberUserEntity);
                        if (memberUserData.IsConnected)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                if (clanMembers.IsCreated) clanMembers.Dispose();
            }
            return false;
        }
        #endregion
    }
}