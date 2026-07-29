using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Network;
using System;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Entities;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserConnected))]
    public static class UserConnectHookPatch
    {
        private static void Postfix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
        {
            EntityManager entityManager = __instance.EntityManager;
            Entity userEntity = Entity.Null;
            string charNameForLog = "UnknownOnConnect";
            User connectedUserData = default;

            try
            {
                if (__instance._NetEndPointToApprovedUserIndex.TryGetValue(netConnectionId, out int userIndex))
                {
                    if (userIndex >= 0 && userIndex < __instance._ApprovedUsersLookup.Length)
                    {
                        userEntity = __instance._ApprovedUsersLookup[userIndex].UserEntity;
                        if (entityManager.Exists(userEntity) && entityManager.TryGetComponentData<User>(userEntity, out connectedUserData))
                        {
                            charNameForLog = connectedUserData.CharacterName.ToString();
                        }
                    }
                }

                if (userEntity == Entity.Null || connectedUserData.Equals(default(User)))
                {
                    return;
                }

                PlayerRegistryService.UpsertUser(entityManager, userEntity, true);

                ulong platformId = connectedUserData.PlatformId;
                string userPersistentKey = PersistentKeyHelper.GetUserKey(platformId);

                OfflineGraceService.MarkAsOnline(userPersistentKey, charNameForLog);
                RaidMapIconService.RemoveOfflineRaidIconsForOwner(userPersistentKey, entityManager);
                OwnershipCacheService.HandlePlayerConnected(userEntity, entityManager);
                Entity liveClanEntity = connectedUserData.ClanEntity._Entity;

                if (liveClanEntity != Entity.Null && entityManager.Exists(liveClanEntity))
                {
                    string clanPersistentKey = PersistentKeyHelper.GetClanKey(entityManager, liveClanEntity);
                    if (!string.IsNullOrEmpty(clanPersistentKey))
                    {
                        string clanNameForContext = "Clan";
                        if (entityManager.HasComponent<ClanTeam>(liveClanEntity))
                        {
                            clanNameForContext = entityManager.GetComponentData<ClanTeam>(liveClanEntity).Name.ToString();
                        }

                        OfflineGraceService.MarkAsOnline(clanPersistentKey, $"{clanNameForContext} (member {charNameForLog} connected)");
                        RaidMapIconService.RemoveOfflineRaidIconsForOwner(clanPersistentKey, entityManager);

                    }
                }

                RaidMapIconService.MarkPersistentStateIconsDirty();
                RaidMapIconService.ProcessCleanup();
                Plugin.RunOnBootShardScanIfNeeded();

            }
            catch (Exception ex)
            {
                LoggingHelper.Error("Error in UserConnectHookPatch Postfix", ex);
            }
        }
    }
}
