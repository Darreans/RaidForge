using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using System;
using RaidForge.Services; 
using RaidForge.Utils;    

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.CreateUserDisconnectedEvent))]
    public static class UserCreateDisconnectedEventHookPatch
    {
        private static void Postfix(Entity userEntity, EntityManager entityManager, ConnectedUser connectedUser, bool isFromPersistenceLoad)
        {
            LoggingHelper.Info($"[UserCreateDisconnectedEventHookPatch] Postfix entered. UserEntity: {userEntity}, IsFromPersistenceLoad: {isFromPersistenceLoad}");

            if (entityManager == default)
            {
                LoggingHelper.Error("[UserCreateDisconnectedEventHookPatch] EntityManager is default! Cannot proceed.");
                return;
            }

            try
            {
                if (entityManager.Exists(userEntity))
                {
                    string charNameForLog = "[Name Unknown/No UserComp]";
                    if (entityManager.HasComponent<User>(userEntity))
                    {
                        User userDataForLog = entityManager.GetComponentData<User>(userEntity);
                        charNameForLog = userDataForLog.CharacterName.ToString();
                        LoggingHelper.Info($"[UserCreateDisconnectedEventHookPatch] User '{charNameForLog}' (Entity: {userEntity}) disconnected event created. Calling HandleUserDisconnected.");
                    }
                    else
                    {
                        LoggingHelper.Warning($"[UserCreateDisconnectedEventHookPatch] UserEntity {userEntity} exists but lacks User component. Still calling HandleUserDisconnected.");
                    }
                    OfflineGraceService.HandleUserDisconnected(entityManager, userEntity); 
                }
                else
                {
                    LoggingHelper.Warning($"[UserCreateDisconnectedEventHookPatch] Disconnected UserEntity {userEntity} provided by game event does NOT exist in EntityManager. Cannot call HandleUserDisconnected.");
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"Error in UserCreateDisconnectedEventHookPatch Postfix", ex);
            }
        }
    }
}