using HarmonyLib;
using ProjectM;

namespace RaidForge
{
    /// <summary>
    /// Patches VivoxConnectionSystem.OnUpdate so we can do scheduling each server frame.
    /// Then inside OnServerTick() we only do reflection once every 'RaidCheckInterval' seconds.
    /// </summary>
    [HarmonyPatch(typeof(VivoxConnectionSystem), nameof(VivoxConnectionSystem.OnUpdate))]
    public static class VivoxPatch
    {
        [HarmonyPostfix]
        public static void Postfix(VivoxConnectionSystem __instance)
        {
            // Removed the spammy debug logs:
            // RaidForgePlugin.Logger.LogInfo("[DEBUG] VivoxConnectionSystem.OnUpdate => Patch Fired!");

            // Pass control to RaidtimeManager
            RaidtimeManager.OnServerTick();
        }
    }
}
