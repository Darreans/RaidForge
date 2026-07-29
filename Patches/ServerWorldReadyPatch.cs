using HarmonyLib;
using ProjectM;
using System;
using System.Threading;
using Unity.Entities;

namespace RaidForge.Patches
{
    /*
		This patch waits for LoadPersistenceSystemV2.SetLoadState(... SuccessfulStartup).
		That is a safer world-load signal than polling ServerStartupState with EntityQuery every tick.

		Important:
		- Do not use the old ServerBootstrapSystem.OnUpdate + EntityQuery.Dispose version.
		- That version can crash IL2CPP/DOTS servers.
	*/
    [HarmonyPatch(typeof(LoadPersistenceSystemV2), nameof(LoadPersistenceSystemV2.SetLoadState))]
    public static class ServerWorldReadyPatch
    {
        private static bool _initRetryRegistered = false;
        private static bool _errorLogged = false;
        private static Timer _initRetryTimer;

        public static void ResetRuntimeState()
        {
            CancelInitializationRetry();
            _initRetryRegistered = false;
            _errorLogged = false;
        }

        private static void Postfix(EntityManager entityManager, ServerStartupState.State loadState)
        {
            try
            {
                if (Plugin.SystemsInitialized)
                {
                    return;
                }

                if (loadState != ServerStartupState.State.SuccessfulStartup)
                {
                    return;
                }

                RegisterInitializationRetry();
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    Plugin.Logger?.LogError($"[RaidForge] Error in persistence-load startup hook: {ex}");
                    _errorLogged = true;
                }
            }
        }

        private static void RegisterInitializationRetry()
        {
            if (_initRetryRegistered)
            {
                return;
            }

            _initRetryRegistered = true;

            Plugin.Logger?.LogInfo("[RaidForge] Persistence load completed. Waiting for final server readiness...");

            /*
				Try once immediately, then keep retrying lightly.
				The repeated callback becomes harmless after SystemsInitialized is true.
            */
            TryInitialize();

            if (Plugin.SystemsInitialized)
            {
                CancelInitializationRetry();
                return;
            }

            _initRetryTimer = RaidForge.Core.RaidForgeScheduler.RunEvery(() =>
            {
                if (Plugin.SystemsInitialized)
                {
                    CancelInitializationRetry();
                    return;
                }

                TryInitialize();

                if (Plugin.SystemsInitialized)
                {
                    CancelInitializationRetry();
                }
            }, 5.0);
        }

        private static void TryInitialize()
        {
            try
            {
                if (Plugin.SystemsInitialized)
                {
                    return;
                }

                Plugin.AttemptInitializeCoreSystems();
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    Plugin.Logger?.LogError($"[RaidForge] Error while attempting delayed core initialization: {ex}");
                    _errorLogged = true;
                }
            }
        }

        private static void CancelInitializationRetry()
        {
            _initRetryTimer?.Dispose();
            _initRetryTimer = null;
        }
    }
}
