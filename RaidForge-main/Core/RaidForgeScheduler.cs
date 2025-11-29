using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using ProjectM;
using RaidForge.Utils;

namespace RaidForge.Core
{
    [HarmonyPatch]
    public static class RaidForgeScheduler
    {
        private static readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private static readonly List<Timer> _activeTimers = new List<Timer>();
        private static bool _isInitialized = false;

        [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUpdate))]
        [HarmonyPostfix]
        public static void OnUpdate_Postfix()
        {
            while (_mainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error($"[RaidForgeScheduler] Error executing action: {ex}");
                }
            }
        }

        public static void Initialize(Harmony harmony)
        {
            if (_isInitialized) return;

            harmony.PatchAll(typeof(RaidForgeScheduler));

            _isInitialized = true;
            LoggingHelper.Info("[RaidForgeScheduler] Scheduler Initialized.");
        }

        public static void Dispose()
        {
            lock (_activeTimers)
            {
                foreach (var timer in _activeTimers) timer?.Dispose();
                _activeTimers.Clear();
            }
            while (_mainThreadActions.TryDequeue(out _)) { }
            _isInitialized = false;
        }

        private static void EnqueueMainThreadAction(Action action) => _mainThreadActions.Enqueue(action);

        public static void RunOnce(Action action, double delaySeconds)
        {
            if (delaySeconds < 0) delaySeconds = 0;

            Timer timer = null;
            timer = new Timer(_ =>
            {
                EnqueueMainThreadAction(() =>
                {
                    try
                    {
                        action?.Invoke();
                    }
                    finally
                    {
                        lock (_activeTimers) { _activeTimers.Remove(timer); }
                        timer?.Dispose();
                    }
                });
            }, null, TimeSpan.FromSeconds(delaySeconds), Timeout.InfiniteTimeSpan);

            lock (_activeTimers) { _activeTimers.Add(timer); }
        }

        public static void RunEvery(Action action, double intervalSeconds, double initialDelaySeconds = 0)
        {
            if (intervalSeconds <= 0) return;

            Timer timer = new Timer(_ =>
            {
                EnqueueMainThreadAction(action);
            }, null, TimeSpan.FromSeconds(initialDelaySeconds), TimeSpan.FromSeconds(intervalSeconds));

            lock (_activeTimers) { _activeTimers.Add(timer); }
        }
    }
}