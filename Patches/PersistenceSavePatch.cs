using System;
using HarmonyLib;
using ProjectM;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Collections;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(TriggerPersistenceSaveSystem), nameof(TriggerPersistenceSaveSystem.TriggerSave))]
    public static class PersistenceSavePatch
    {
        private static void Postfix(bool __result, SaveReason reason, FixedString128Bytes saveName, ServerRuntimeSettings saveConfig)
        {
            if (!__result || !Plugin.SystemsInitialized || !VWorld.IsServerWorldReady())
            {
                return;
            }

            try
            {
                PlayerRegistryService.SaveIfDirty();
                SharedFileIO.FlushPendingWrites();
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning("[PersistenceSavePatch] Post-save state flush failed.", ex);
            }
        }
    }
}
