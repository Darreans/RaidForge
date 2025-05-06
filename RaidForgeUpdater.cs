using System;
using UnityEngine;

namespace RaidForge
{
    public class RaidForgeUpdater : MonoBehaviour
    {
        private float _checkScheduleTimer = 0f;
        private const float CHECK_SCHEDULE_INTERVAL = 30.0f;
        private bool _initialCheckPassedInUpdater = false;

        void Update()
        {
            try
            {
                bool performCheckNow = false;
                if (!_initialCheckPassedInUpdater) { performCheckNow = true; } else { _checkScheduleTimer += Time.deltaTime; if (_checkScheduleTimer >= CHECK_SCHEDULE_INTERVAL) { _checkScheduleTimer = 0f; performCheckNow = true; } }

                if (performCheckNow)
                {
                    bool isInitial = !_initialCheckPassedInUpdater;
                    bool checkCouldRun = RaidForgePlugin.CheckScheduleAndToggleRaids(isInitial);
                    RaidForgePlugin.CheckGolemAutomation();

                    if (isInitial && checkCouldRun) { _initialCheckPassedInUpdater = true; }
                }
            }
            catch (Exception ex) { RaidForgePlugin.Logger?.LogError($"!! EXCEPTION within RaidForgeUpdater.Update: {ex}"); }
        }
    }
}