using System;
using BepInEx.Logging;
using ProjectM;
using RaidForge.Utils;

namespace RaidForge.Systems
{
    public static class RaidToggleSystem
    {
        public static bool EnableRaids()
        {
            return SetCastleDamageMode(CastleDamageMode.Always);
        }

        public static bool DisableRaids()
        {
            return SetCastleDamageMode(CastleDamageMode.TimeRestricted);
        }

        private static bool SetCastleDamageMode(CastleDamageMode newMode)
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
            catch (Exception)
            {
                return false;
            }
        }
    }
}