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
            return SetCastleDamageMode(CastleDamageMode.Always, loggerContext);
        }

        public static bool DisableRaids(ManualLogSource loggerContext = null)
        {
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
                        if (settings.CastleDamageMode == newMode)
                        {
                            return settings;
                        }
                        settings.CastleDamageMode = newMode;
                        return settings;
                    }))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}