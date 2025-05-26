using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Collections;
using System;



namespace RaidForge.Utils 
{
    public static class UserHelper
    {
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
            catch (Exception ex)
            {
               
                LoggingHelper.Error($"[UserHelper] Exception during IsAnyClanMemberOnline for clan {clanEntity}", ex);
            }
            finally
            {
                if (clanMembers.IsCreated) clanMembers.Dispose();
            }
            return false;
        }
    }
}