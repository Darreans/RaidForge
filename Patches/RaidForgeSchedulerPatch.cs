using HarmonyLib;
using ProjectM;
using RaidForge.Core;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUpdate))]
    public static class RaidForgeSchedulerPatch
    {
        [HarmonyPostfix]
        public static void OnUpdate_Postfix()
        {
            RaidForgeScheduler.DrainMainThreadActions();
        }
    }
}
