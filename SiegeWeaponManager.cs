using System;
using BepInEx.Logging;
using Unity.Entities;
using ProjectM;       

namespace RaidForge 
{
    public static class SiegeWeaponManager
    {
        public static bool SetSiegeWeaponHealth(SiegeWeaponHealth newValue, ManualLogSource logger)
        {
            EntityManager entityManager;
            try
            {
                entityManager = VWorldUtils.EntityManager;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Could not get EntityManager: {ex.Message}");
                return false;
            }

            try
            {
                ComponentType[] queryComponents = { ComponentType.ReadWrite<ServerGameBalanceSettings>() };
                var query = entityManager.CreateEntityQuery(queryComponents);

                if (query.IsEmptyIgnoreFilter)
                {
                    logger.LogWarning("[SiegeWeaponManager] No entity found with ServerGameBalanceSettings!");
                    return false;
                }

                var entity = query.GetSingletonEntity();
                var sgb = entityManager.GetComponentData<ServerGameBalanceSettings>(entity);

                if (sgb.SiegeWeaponHealth == newValue)
                {
                    logger.LogInfo($"[SiegeWeaponManager] SiegeWeaponHealth already set to {newValue}. No change made.");
                    return true; 
                }


                sgb.SiegeWeaponHealth = newValue;
                entityManager.SetComponentData(entity, sgb);

                logger.LogInfo($"[SiegeWeaponManager] Updated Golem Health => {newValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Could not update Golem Health: {ex}");
                return false;
            }
        }

        public static SiegeWeaponHealth? GetSiegeWeaponHealth(ManualLogSource logger)
        {
            EntityManager entityManager;
            try
            {
                entityManager = VWorldUtils.EntityManager;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Could not get EntityManager: {ex.Message}");
                return null;
            }

            try
            {
                ComponentType[] queryComponents = { ComponentType.ReadOnly<ServerGameBalanceSettings>() };
                var query = entityManager.CreateEntityQuery(queryComponents);

                if (query.IsEmptyIgnoreFilter)
                {
                    logger.LogWarning("[SiegeWeaponManager] No entity found with ServerGameBalanceSettings!");
                    return null;
                }

                var entity = query.GetSingletonEntity();
                var sgb = entityManager.GetComponentData<ServerGameBalanceSettings>(entity);
                return sgb.SiegeWeaponHealth;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Error reading SGB.SiegeWeaponHealth: {ex}");
                return null;
            }
        }
    }
}