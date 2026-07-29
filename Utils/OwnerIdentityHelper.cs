using System;
using ProjectM;
using ProjectM.Network;
using RaidForge.Services;
using Unity.Entities;

namespace RaidForge.Utils
{
    public readonly struct OwnerIdentity
    {
        public readonly Entity UserEntity;
        public readonly Entity ClanEntity;

        public readonly User UserData;

        public readonly string PersistentKey;
        public readonly string UserKey;
        public readonly string ContextualName;
        public readonly string CharacterName;
        public readonly string ClanName;

        public readonly ulong PlatformId;
        public readonly bool IsClan;

        public OwnerIdentity(
            Entity userEntity,
            Entity clanEntity,
            User userData,
            string persistentKey,
            string userKey,
            string contextualName,
            string characterName,
            string clanName,
            ulong platformId,
            bool isClan)
        {
            UserEntity = userEntity;
            ClanEntity = clanEntity;
            UserData = userData;

            PersistentKey = persistentKey;
            UserKey = userKey;
            ContextualName = contextualName;
            CharacterName = characterName;
            ClanName = clanName;

            PlatformId = platformId;
            IsClan = isClan;
        }

        public string GetDisplayNameWithOwnerType()
        {
            return IsClan ? $"{ContextualName} (Clan)" : ContextualName;
        }
    }

    public static class OwnerIdentityHelper
    {
        public static bool TryResolveFromUserEntity(
            EntityManager em,
            Entity userEntity,
            out OwnerIdentity owner,
            out string error)
        {
            owner = default;
            error = string.Empty;

            try
            {
                if (em == default)
                {
                    error = "EntityManager is not available.";
                    return false;
                }

                if (userEntity == Entity.Null || !em.Exists(userEntity))
                {
                    error = "User entity does not exist.";
                    return false;
                }

                if (!em.HasComponent<User>(userEntity))
                {
                    error = "Entity does not have a User component.";
                    return false;
                }

                User userData = em.GetComponentData<User>(userEntity);

                return TryResolveFromUserData(
                    em,
                    userEntity,
                    userData,
                    null,
                    out owner,
                    out error
                );
            }
            catch (Exception ex)
            {
                error = $"Failed to resolve owner identity from user entity: {ex.Message}";
                return false;
            }
        }

        public static bool TryResolveFromUserData(
            EntityManager em,
            Entity userEntity,
            User userData,
            string fallbackCharacterName,
            out OwnerIdentity owner,
            out string error)
        {
            owner = default;
            error = string.Empty;

            try
            {
                string characterName = !string.IsNullOrWhiteSpace(fallbackCharacterName)
                    ? fallbackCharacterName
                    : SafeFixedStringToString(userData.CharacterName.ToString(), "Unknown Player");

                ulong platformId = userData.PlatformId;

                string userKey = PersistentKeyHelper.GetUserKey(platformId);

                Entity clanEntity = userData.ClanEntity._Entity;

                bool hasValidClan =
                    clanEntity != Entity.Null &&
                    clanEntity.Exists() &&
                    em.Exists(clanEntity) &&
                    em.HasComponent<ClanTeam>(clanEntity);

                if (hasValidClan)
                {
                    ClanTeam clanTeam = em.GetComponentData<ClanTeam>(clanEntity);

                    string clanName = SafeFixedStringToString(
                        clanTeam.Name.ToString(),
                        "Unknown Clan"
                    );

                    string clanKey = PersistentKeyHelper.GetClanKey(em, clanEntity);

                    owner = new OwnerIdentity(
                        userEntity,
                        clanEntity,
                        userData,
                        clanKey,
                        userKey,
                        clanName,
                        characterName,
                        clanName,
                        platformId,
                        true
                    );

                    return true;
                }

                owner = new OwnerIdentity(
                    userEntity,
                    Entity.Null,
                    userData,
                    userKey,
                    userKey,
                    characterName,
                    characterName,
                    string.Empty,
                    platformId,
                    false
                );

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to resolve owner identity from user data: {ex.Message}";
                return false;
            }
        }

        public static bool TryResolveFromPlayerName(
            EntityManager em,
            string playerName,
            out OwnerIdentity owner,
            out string error)
        {
            owner = default;
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(playerName))
                {
                    error = "Player name cannot be empty.";
                    return false;
                }

                if (!UserHelper.FindUserEntity(
                    em,
                    playerName,
                    out Entity userEntity,
                    out User userData,
                    out string foundName))
                {
                    error = $"Player '{playerName}' not found.";
                    return false;
                }

                return TryResolveFromUserData(
                    em,
                    userEntity,
                    userData,
                    foundName,
                    out owner,
                    out error
                );
            }
            catch (Exception ex)
            {
                error = $"Failed to resolve owner identity for player '{playerName}': {ex.Message}";
                return false;
            }
        }

        public static bool TryResolveFromCommandSender(
            EntityManager em,
            Entity senderUserEntity,
            out OwnerIdentity owner,
            out string error)
        {
            return TryResolveFromUserEntity(
                em,
                senderUserEntity,
                out owner,
                out error
            );
        }

        private static string SafeFixedStringToString(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim();
        }
    }
}