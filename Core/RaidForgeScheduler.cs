using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using RaidForge.Utils;

namespace RaidForge.Core
{
    public static class RaidForgeScheduler
    {
        private static readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private static readonly List<FrameDelayedAction> _frameDelayedActions = new List<FrameDelayedAction>();
        private static readonly List<Timer> _activeTimers = new List<Timer>();
        private static readonly object _frameDelayedActionsLock = new object();
        private static bool _isInitialized = false;

        public static void DrainMainThreadActions()
        {
            DrainFrameDelayedActions();

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

        private static void DrainFrameDelayedActions()
        {
            List<Action> readyActions = null;

            lock (_frameDelayedActionsLock)
            {
                for (int i = _frameDelayedActions.Count - 1; i >= 0; i--)
                {
                    var delayedAction = _frameDelayedActions[i];
                    delayedAction.FramesRemaining--;

                    if (delayedAction.FramesRemaining > 0)
                    {
                        continue;
                    }

                    readyActions ??= new List<Action>();
                    readyActions.Add(delayedAction.Action);
                    _frameDelayedActions.RemoveAt(i);
                }
            }

            if (readyActions == null)
            {
                return;
            }

            foreach (var action in readyActions)
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error($"[RaidForgeScheduler] Error executing frame-delayed action: {ex}");
                }
            }
        }

        public static void Initialize()
        {
            if (_isInitialized) return;

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

            lock (_frameDelayedActionsLock)
            {
                _frameDelayedActions.Clear();
            }

            while (_mainThreadActions.TryDequeue(out _)) { }
            _isInitialized = false;
        }

        private static void EnqueueMainThreadAction(Action action) => _mainThreadActions.Enqueue(action);

        public static void RunAfterFrames(Action action, int frames)
        {
            if (frames <= 0)
            {
                EnqueueMainThreadAction(action);
                return;
            }

            lock (_frameDelayedActionsLock)
            {
                _frameDelayedActions.Add(new FrameDelayedAction(action, frames));
            }
        }

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

        public static Timer RunEvery(Action action, double intervalSeconds, double initialDelaySeconds = 0)
        {
            if (intervalSeconds <= 0) return null;

            int queuedOrRunning = 0;

            Timer timer = new Timer(_ =>
            {
                if (Interlocked.Exchange(ref queuedOrRunning, 1) == 1)
                {
                    return;
                }

                EnqueueMainThreadAction(() =>
                {
                    try
                    {
                        action?.Invoke();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref queuedOrRunning, 0);
                    }
                });
            }, null, TimeSpan.FromSeconds(initialDelaySeconds), TimeSpan.FromSeconds(intervalSeconds));

            lock (_activeTimers) { _activeTimers.Add(timer); }
            return timer;
        }

        private sealed class FrameDelayedAction
        {
            public readonly Action Action;
            public int FramesRemaining;

            public FrameDelayedAction(Action action, int framesRemaining)
            {
                Action = action;
                FramesRemaining = framesRemaining;
            }
        }
    }
}
