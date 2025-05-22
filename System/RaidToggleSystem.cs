using System;
using BepInEx.Logging; 
using ProjectM;
using RaidForge.Utils; 

namespace RaidForge.Systems
{
    public static class RaidToggleSystem
    {
        public static bool EnableRaids(ManualLogSource loggerContext = null) 
        {
            LoggingHelper.Info("RaidToggleSystem: Attempting to ENABLE raids (Set CastleDamageMode.Always)...");
            return SetCastleDamageMode(CastleDamageMode.Always, loggerContext);
        }

        public static bool DisableRaids(ManualLogSource loggerContext = null)
        {
            LoggingHelper.Info("RaidToggleSystem: Attempting to DISABLE raids (Set CastleDamageMode.TimeRestricted)...");
            return SetCastleDamageMode(CastleDamageMode.TimeRestricted, loggerContext);
        }

        private static bool SetCastleDamageMode(CastleDamageMode newMode, ManualLogSource loggerContext = null)
        {
            try
            {
                if (VWorld.GameBalanceSettings(
                out var currentSettings, 
                settings =>
                {
                    LoggingHelper.Debug($"RaidToggleSystem: Current CastleDamageMode is: {settings.CastleDamageMode}. Desired mode: {newMode}");

                    if (settings.CastleDamageMode == newMode)
                    {
                        LoggingHelper.Info($"RaidToggleSystem: CastleDamageMode is already set to {newMode}. No change needed.");
                        return settings; 
                    }

                    LoggingHelper.Info($"RaidToggleSystem: Changing CastleDamageMode from {settings.CastleDamageMode} to {newMode}...");
                    settings.CastleDamageMode = newMode;
                    return settings; 
                }))
                {
                    return true;
                }
                else
                {
                    LoggingHelper.Error($"RaidToggleSystem: Failed to apply GameBalanceSettings for CastleDamageMode {newMode}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"RaidToggleSystem EXCEPTION while setting CastleDamageMode to {newMode}", ex);
                return false;
            }
        }
    }
}