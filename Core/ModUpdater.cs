using System;
using UnityEngine;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;
using RaidForge;

namespace RaidForge.Core
{
    public class ModUpdater : MonoBehaviour
    {
        private float _checkScheduleTimer = 0f;
        private const float CHECK_SCHEDULE_INTERVAL = 45.0f;

        private bool _initialPeriodicChecksPassed = false;

        private float _coreInitAttemptTimer = 0f;
        private const float CORE_INIT_ATTEMPT_INTERVAL = 5.0f;

        public ModUpdater(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            try
            {
                if (!Plugin.SystemsInitialized)
                {
                    _coreInitAttemptTimer += Time.deltaTime;
                    if (_coreInitAttemptTimer >= CORE_INIT_ATTEMPT_INTERVAL)
                    {
                        _coreInitAttemptTimer = 0f;
                        if (VWorld.IsServerWorldReady())
                        {
                            Plugin.AttemptInitializeCoreSystems();
                        }
                    }
                }

                if (Plugin.SystemsInitialized)
                {
                    bool performPeriodicChecksNow = false;
                    if (!_initialPeriodicChecksPassed)
                    {
                        performPeriodicChecksNow = true;
                    }
                    else
                    {
                        _checkScheduleTimer += Time.deltaTime;
                        if (_checkScheduleTimer >= CHECK_SCHEDULE_INTERVAL)
                        {
                            _checkScheduleTimer = 0f;
                            performPeriodicChecksNow = true;
                        }
                    }

                    if (performPeriodicChecksNow)
                    {
                        bool isFirstRun = !_initialPeriodicChecksPassed;

                        RaidSchedulingSystem.CheckScheduleAndToggleRaids(isFirstRun);
                        GolemAutomationSystem.CheckAutomation();

                        if (isFirstRun)
                        {
                            _initialPeriodicChecksPassed = true;
                        }
                    }
                    RaidInterferenceService.Tick(Time.deltaTime);
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}