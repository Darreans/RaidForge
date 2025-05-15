using System;
using BepInEx.Logging;
using ProjectM;       

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
            try
            {
                if (VWorldUtils.GameBalanceSettings(
                out var after,
                settings =>
                {
                    logger?.LogInfo($"Toggler: Current CastleDamageMode is: {settings.CastleDamageMode}. Desired mode: {newMode}");

                    if (settings.CastleDamageMode == newMode)
                    {
                        logger?.LogInfo($"Toggler: CastleDamageMode is already set to {newMode}. No change needed.");
                        return settings;
                    }

                    logger?.LogInfo($"Toggler: Changing CastleDamageMode from {settings.CastleDamageMode} to {newMode}...");
                    settings.CastleDamageMode = newMode;
                    
                    return settings;
                }))
                
                return true; 
            }
            catch (Exception ex)
            {
                logger?.LogError($"!!! Toggler EXCEPTION while setting CastleDamageMode: {ex}");
                return false; 
            }

            return false;
        }
    }
}