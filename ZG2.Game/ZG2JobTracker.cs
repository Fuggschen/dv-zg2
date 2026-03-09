using System;
using System.Collections;
using System.Collections.Generic;
using DV.Logic.Job;
using DV.Utils;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Per-job tracking data for ZG2 board presence.
    /// </summary>
    public class ZG2JobData
    {
        public string JobId;
        public bool EverHadBoard;
        public bool CurrentlyHasBoard;

        public ZG2JobData(string jobId)
        {
            JobId = jobId;
            EverHadBoard = false;
            CurrentlyHasBoard = false;
        }
    }

    /// <summary>
    /// Tracks whether active jobs have ZG2 boards attached to the last wagon.
    /// Runs a periodic check coroutine while jobs are active.
    /// Attached to the mod's persistent GameObject by <see cref="Main"/>.
    /// </summary>
    public class ZG2JobTracker : MonoBehaviour
    {
        private static ZG2JobTracker instance;

        /// <summary>Job tracking data. Key = Job.ID.</summary>
        private static readonly Dictionary<string, ZG2JobData> trackedJobs
            = new Dictionary<string, ZG2JobData>();

        private Coroutine checkCoroutine;
        private bool isRunning;

        public static IReadOnlyDictionary<string, ZG2JobData> TrackedJobs => trackedJobs;

        public void Initialise()
        {
            instance = this;
            // If there are already active jobs (restored from save), start tracking
            if (trackedJobs.Count > 0)
            {
                StartCheckLoop();
            }
        }

        /// <summary>
        /// Begin tracking a newly-taken job.
        /// Called from the <see cref="Patches.JobTakenPatch"/>.
        /// </summary>
        public static void StartTracking(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return;
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            if (!trackedJobs.ContainsKey(jobId))
            {
                trackedJobs[jobId] = new ZG2JobData(jobId);
                Main.Log($"Started tracking job: {jobId}");
            }

            instance?.StartCheckLoop();
        }

        /// <summary>
        /// Stop tracking a job (abandoned or completed).
        /// </summary>
        public static void StopTracking(string jobId)
        {
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (trackedJobs.Remove(jobId))
            {
                Main.Log($"Stopped tracking job: {jobId}");
            }

            if (trackedJobs.Count == 0)
            {
                instance?.StopCheckLoop();
            }
        }

        /// <summary>
        /// Get the tracking data for a specific job.
        /// Returns null if the job is not being tracked.
        /// </summary>
        public static ZG2JobData GetJobData(string jobId)
        {
            return trackedJobs.TryGetValue(jobId, out var data) ? data : null;
        }

        /// <summary>
        /// Restores tracking data from save. Called by <see cref="ZG2SaveManager"/>.
        /// </summary>
        public static void RestoreTrackedJobs(Dictionary<string, ZG2JobData> data)
        {
            trackedJobs.Clear();
            if (data != null)
            {
                foreach (var kvp in data)
                    trackedJobs[kvp.Key] = kvp.Value;
            }
        }

        public static void Shutdown()
        {
            if (instance != null)
            {
                instance.StopCheckLoop();
                instance = null;
            }
            // Don't clear trackedJobs here — ZG2SaveManager reads them during save
            trackedJobs.Clear();
        }

        #region Check loop

        private void StartCheckLoop()
        {
            if (isRunning) return;
            isRunning = true;
            checkCoroutine = StartCoroutine(CheckLoop());
            Main.Log("ZG2 check loop started.");
        }

        private void StopCheckLoop()
        {
            if (!isRunning) return;
            isRunning = false;
            if (checkCoroutine != null)
            {
                StopCoroutine(checkCoroutine);
                checkCoroutine = null;
            }
            Main.Log("ZG2 check loop stopped.");
        }

        /// <summary>
        /// Periodically checks all tracked jobs for ZG2 board presence on the last wagon.
        /// </summary>
        private IEnumerator CheckLoop()
        {
            while (isRunning && trackedJobs.Count > 0)
            {
                yield return new WaitForSeconds(Main.Settings.CheckIntervalSeconds);

                if (!Main.Enabled || UnloadWatcher.isUnloading) yield break;

                try
                {
                    PerformCheck();
                }
                catch (Exception ex)
                {
                    Main.LogError($"Error during ZG2 check: {ex}");
                }
            }

            isRunning = false;
            checkCoroutine = null;
        }

        /// <summary>
        /// Checks every tracked job to see whether its train(s) have ZG2 boards.
        /// </summary>
        private void PerformCheck()
        {
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            var jobsManager = SingletonBehaviour<JobsManager>.Instance;
            if (jobsManager == null) return;

            var registry = SingletonBehaviour<TrainCarRegistry>.Instance;
            if (registry == null) return;

            // jobToJobCars is accessible via Krafs.Publicizer (was private Dictionary<Job, HashSet<Car>>)
            var jobCarMap = jobsManager.jobToJobCars;
            if (jobCarMap == null) return;

            // Iterate taken jobs
            foreach (var job in jobsManager.currentJobs)
            {
                if (job == null) continue;

                if (!trackedJobs.TryGetValue(job.ID, out var trackData))
                    continue;

                bool foundBoard = CheckJobForBoards(job, jobCarMap, registry);

                trackData.CurrentlyHasBoard = foundBoard;
                if (foundBoard)
                {
                    trackData.EverHadBoard = true;
                }
            }
        }

        /// <summary>
        /// For a single job, determines whether any of its train consist end cars
        /// have a ZG2 board gadget attached.
        /// </summary>
        private bool CheckJobForBoards(
            Job job,
            Dictionary<Job, HashSet<Car>> jobCarMap,
            TrainCarRegistry registry)
        {
            if (!jobCarMap.TryGetValue(job, out var logicCars) || logicCars == null)
                return false;

            // Collect unique trainsets that this job's cars belong to
            var checkedSets = new HashSet<int>();

            foreach (var logicCar in logicCars)
            {
                if (!registry.logicCarToTrainCar.TryGetValue(logicCar, out var trainCar))
                    continue;

                var trainset = trainCar.trainset;
                if (trainset == null || !checkedSets.Add(trainset.id))
                    continue;

                // Check both ends of the trainset
                if (ZG2GadgetDetector.HasZG2Board(trainset.firstCar))
                    return true;
                if (ZG2GadgetDetector.HasZG2Board(trainset.lastCar))
                    return true;
            }

            return false;
        }

        #endregion
    }
}
