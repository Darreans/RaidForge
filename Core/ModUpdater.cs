using System;
using UnityEngine;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;

namespace RaidForge.Core
{
    public class ModUpdater : MonoBehaviour
    {
        private float _checkScheduleTimer = 0f;
        private const float CHECK_SCHEDULE_INTERVAL = 30.0f;
        private bool _initialCheckPassedInUpdater = false;

        public ModUpdater(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            try
            {
                bool performGlobalCheckNow = false;
                if (!_initialCheckPassedInUpdater)
                {
                    performGlobalCheckNow = true;
                }
                else
                {
                    _checkScheduleTimer += Time.deltaTime;
                    if (_checkScheduleTimer >= CHECK_SCHEDULE_INTERVAL)
                    {
                        _checkScheduleTimer = 0f;
                        performGlobalCheckNow = true;
                    }
                }

                if (performGlobalCheckNow)
                {
                    bool isInitial = !_initialCheckPassedInUpdater;

                    bool checkCouldRun = RaidSchedulingSystem.CheckScheduleAndToggleRaids(isInitial);
                    GolemAutomationSystem.CheckAutomation();

                    if (isInitial && checkCouldRun)
                    {
                        _initialCheckPassedInUpdater = true;
                    }
                }

                RaidInterferenceService.Tick(Time.deltaTime);
            }
            catch (Exception ex)
            {
                LoggingHelper.Error("!! EXCEPTION within ModUpdater.Update", ex);
            }
        }
    }
}