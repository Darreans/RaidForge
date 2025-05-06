using System;
using BepInEx.Logging;
using ProjectM;       
using Unity.Entities; 

namespace RaidForge 
{
    public static class VrisingRaidToggler
    {
        public static bool EnableRaids(ManualLogSource logger)
        {
            logger?.LogInfo("Toggler: Attempting to ENABLE raids (Set CastleDamageMode.Always)...");
            return SetCastleDamageMode(CastleDamageMode.Always, logger);
        }

        public static bool DisableRaids(ManualLogSource logger)
        {
            logger?.LogInfo("Toggler: Attempting to DISABLE raids (Set CastleDamageMode.TimeRestricted)...");
            return SetCastleDamageMode(CastleDamageMode.TimeRestricted, logger);
        }

        private static bool SetCastleDamageMode(CastleDamageMode newMode, ManualLogSource logger)
        {
            EntityManager entityManager;
            try
            {
                entityManager = VWorldUtils.EntityManager;
            }
            catch (Exception ex)
            {
                logger?.LogError($"!!! Toggler FAILED: Could not get EntityManager: {ex.Message}");
                return false;
            }

            try
            {
                ComponentType[] queryComponents = { ComponentType.ReadWrite<ServerGameBalanceSettings>() };
                EntityQuery settingsQuery = entityManager.CreateEntityQuery(queryComponents);

                if (settingsQuery.IsEmptyIgnoreFilter)
                {
                    logger?.LogError("!!! Toggler FAILED: Could not find ServerGameBalanceSettings entity.");
                    return false;
                }

                Entity settingsEntity = settingsQuery.GetSingletonEntity();
                ServerGameBalanceSettings balanceSettings = entityManager.GetComponentData<ServerGameBalanceSettings>(settingsEntity);

                logger?.LogInfo($"Toggler: Current CastleDamageMode is: {balanceSettings.CastleDamageMode}. Desired mode: {newMode}");

                if (balanceSettings.CastleDamageMode == newMode)
                {
                    logger?.LogInfo($"Toggler: CastleDamageMode is already set to {newMode}. No change needed.");
                    return true; 
                }

                logger?.LogInfo($"Toggler: Changing CastleDamageMode from {balanceSettings.CastleDamageMode} to {newMode}...");
                balanceSettings.CastleDamageMode = newMode;
                entityManager.SetComponentData(settingsEntity, balanceSettings);
                logger?.LogInfo($"Toggler: Successfully SET CastleDamageMode to {newMode}.");
                return true; 
            }
            catch (Exception ex)
            {
                logger?.LogError($"!!! Toggler EXCEPTION while setting CastleDamageMode: {ex}");
                return false; 
            }
        }
    }
}