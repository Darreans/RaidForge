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
                if (VWorldUtils.GameBalanceSettings(
                out var after,
                settings =>
                {
                    if (settings.SiegeWeaponHealth == newValue)
                    {
                        logger.LogInfo($"[SiegeWeaponManager] SiegeWeaponHealth already set to {newValue}. No change made.");
                        return settings; 
                    }
                
                    settings.SiegeWeaponHealth = newValue;

                    logger.LogInfo($"[SiegeWeaponManager] Updated Golem Health => {newValue}");
                    
                    return settings;
                }))
                    
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Could not update Golem Health: {ex}");
                return false;
            }

            return false;
        }

        public static SiegeWeaponHealth? GetSiegeWeaponHealth(ManualLogSource logger)
        {
            try
            {
                VWorldUtils.GameBalanceSettings(out var settings);
                return settings.SiegeWeaponHealth;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Error reading SGB.SiegeWeaponHealth: {ex}");
                return null;
            }
        }
    }
}