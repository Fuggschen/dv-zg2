using System;
using System.Collections;
using System.Collections.Generic;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Checkpoint-based Zg2 monitoring.
    ///
    /// Every second (host / single player only) the tracker looks at the trainsets that contain
    /// at least one car of an active, Zg2-relevant job and follows their station state:
    ///   outside → inner zone of a station  = "Einfahrt-Check"
    ///   in station → no car left in its outer zone = "Ausfahrt-Check"
    /// At each checkpoint the actual train end is validated once and the result is written to
    /// the protocol of every job that has cars in this trainset.
    ///
    /// A trainset seen for the first time (new job, coupling, uncoupling, save load) is
    /// initialised silently – only real transitions create checkpoints, so splitting a train
    /// at a station never creates a checkpoint by itself, but the next departure validates the
    /// NEW train end.
    /// </summary>
    public class ZG2JobTracker : MonoBehaviour
    {
        private const float POLL_INTERVAL = 1f;
        private const int NO_STATION = -1;

        private static ZG2JobTracker instance;

        /// <summary>Protocols of active jobs. Key = Job.ID.</summary>
        private static readonly Dictionary<string, ZG2JobLog> logs = new Dictionary<string, ZG2JobLog>();

        /// <summary>Protocols of jobs validated this session (for reports / reprints). Key = Job.ID.</summary>
        private static readonly Dictionary<string, ZG2JobLog> finished = new Dictionary<string, ZG2JobLog>();
        private static readonly Queue<string> finishedOrder = new Queue<string>();
        private const int MAX_FINISHED = 64;

        /// <summary>Station state of a train (shared by all its cars).</summary>
        private class TrainState
        {
            /// <summary>Station the train is currently in (index into ZG2StationZones) or -1.</summary>
            public int AtStation = NO_STATION;
            /// <summary>Station the train has departed from (scored departure) or -1 = unknown.</summary>
            public int DepartedFrom = NO_STATION;

            public TrainState Clone() => new TrainState { AtStation = AtStation, DepartedFrom = DepartedFrom };
        }

        // The state is kept per CAR and handed over between polls, so it survives coupling and
        // uncoupling (a new Trainset object inherits the state of its cars).
        private static Dictionary<TrainCar, TrainState> prevCarState = new Dictionary<TrainCar, TrainState>();
        private static Dictionary<TrainCar, TrainState> curCarState = new Dictionary<TrainCar, TrainState>();

        /// <summary>Train states from the save game (car ID → yard IDs), applied when the car is first seen.</summary>
        private class SavedState { public string At; public string Departed; }
        private static readonly Dictionary<string, SavedState> savedStates = new Dictionary<string, SavedState>();

        // Re-used per poll
        private static readonly Dictionary<Trainset, List<ZG2JobLog>> jobsPerTrainset = new Dictionary<Trainset, List<ZG2JobLog>>();
        private static readonly Stack<List<ZG2JobLog>> listPool = new Stack<List<ZG2JobLog>>();
        private static readonly HashSet<string> activeJobIds = new HashSet<string>();
        private static readonly HashSet<string> changedLogs = new HashSet<string>();

        private static readonly List<string> staleLogs = new List<string>();
        private static int pollCount;

        private Coroutine loop;

        public static IReadOnlyDictionary<string, ZG2JobLog> Logs => logs;

        public void Initialise()
        {
            instance = this;
            pollCount = 0;
            loop = StartCoroutine(Loop());
        }

        public static void Shutdown()
        {
            if (instance != null)
            {
                if (instance.loop != null) instance.StopCoroutine(instance.loop);
                instance = null;
            }
            logs.Clear();
            finished.Clear();
            finishedOrder.Clear();
            prevCarState.Clear();
            curCarState.Clear();
            savedStates.Clear();
            jobsPerTrainset.Clear();
        }

        #region Public API

        /// <summary>Protocol for a job – active, finished this session, or received from the host.</summary>
        public static ZG2JobLog GetLog(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return null;
            if (logs.TryGetValue(jobId, out var l)) return l;
            return finished.TryGetValue(jobId, out l) ? l : null;
        }

        public static void StartTracking(Job job)
        {
            if (job == null || !ZG2MPUtil.IsSinglePlayerOrHost) return;
            if (!ZG2JobTypes.IsTracked(job)) return;
            if (!logs.ContainsKey(job.ID))
            {
                logs[job.ID] = new ZG2JobLog(job.ID);
                Main.Log($"Zg2: tracking job {job.ID} ({ZG2JobTypes.KeyOf(job.jobType)}).");
            }
        }

        /// <summary>Removes a job from monitoring (abandoned).</summary>
        public static void StopTracking(string jobId)
        {
            if (jobId != null && logs.Remove(jobId))
                Main.Log($"Zg2: stopped tracking job {jobId}.");
        }

        /// <summary>Moves a validated job's protocol to the finished cache.</summary>
        public static void FinishJob(string jobId)
        {
            if (jobId == null || !logs.TryGetValue(jobId, out var log)) return;
            logs.Remove(jobId);
            StoreFinished(log);
        }

        private static void StoreFinished(ZG2JobLog log)
        {
            if (!finished.ContainsKey(log.JobId)) finishedOrder.Enqueue(log.JobId);
            finished[log.JobId] = log;
            while (finishedOrder.Count > MAX_FINISHED)
                finished.Remove(finishedOrder.Dequeue());
        }

        /// <summary>Restores protocols from the save game.</summary>
        public static void Restore(IEnumerable<ZG2JobLog> restored)
        {
            logs.Clear();
            if (restored == null) return;
            foreach (var l in restored)
                if (l != null && !string.IsNullOrEmpty(l.JobId)) logs[l.JobId] = l;
        }

        /// <summary>MP client: protocol received from the host.</summary>
        public static void OnClientJobLog(string jobId, string data)
        {
            if (string.IsNullOrEmpty(jobId)) return;
            try
            {
                var log = ZG2JobLog.Deserialize(jobId, data);
                if (log.AppliedPenaltyPercent >= 0f)
                {
                    logs.Remove(jobId);
                    StoreFinished(log);
                }
                else
                {
                    logs[jobId] = log;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"Zg2: invalid job log from host for {jobId}: {ex.Message}");
            }
        }

        /// <summary>Host: send all active protocols (e.g. to a newly connected player).</summary>
        public static IEnumerable<KeyValuePair<string, ZG2JobLog>> AllActiveLogs() => logs;

        #endregion

        #region Poll loop

        private IEnumerator Loop()
        {
            var wait = new WaitForSeconds(POLL_INTERVAL);
            while (true)
            {
                yield return wait;
                if (!Main.Enabled || UnloadWatcher.isUnloading) continue;

                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    Main.LogError($"Zg2: error during check: {ex}");
                }

                try { ZG2BoardWear.Tick(POLL_INTERVAL); }
                catch (Exception ex) { Main.LogError($"Zg2: wear update failed: {ex.Message}"); }
            }
        }

        private static void Poll()
        {
            if (!ZG2MPUtil.IsSinglePlayerOrHost) return;

            var jobsManager = SingletonBehaviour<JobsManager>.Instance;
            var registry = SingletonBehaviour<TrainCarRegistry>.Instance;
            if (jobsManager == null || registry == null) return;

            ReleaseLists();

            // 1) Reconcile active, configured jobs (also covers config changes and save loads)
            activeJobIds.Clear();
            foreach (var job in jobsManager.currentJobs)
            {
                if (job == null || job.State != JobState.InProgress) continue;
                if (!ZG2JobTypes.IsTracked(job)) continue;

                activeJobIds.Add(job.ID);
                if (!logs.TryGetValue(job.ID, out var log))
                {
                    log = new ZG2JobLog(job.ID);
                    logs[job.ID] = log;
                }

                // 2) Trainsets that contain cars of this job
                if (!jobsManager.jobToJobCars.TryGetValue(job, out var cars) || cars == null) continue;
                foreach (var logicCar in cars)
                {
                    if (logicCar == null) continue;
                    if (!registry.logicCarToTrainCar.TryGetValue(logicCar, out var trainCar) || trainCar == null) continue;
                    var ts = trainCar.trainset;
                    if (ts == null) continue;

                    if (!jobsPerTrainset.TryGetValue(ts, out var list))
                    {
                        list = listPool.Count > 0 ? listPool.Pop() : new List<ZG2JobLog>(4);
                        jobsPerTrainset[ts] = list;
                    }
                    if (!list.Contains(log)) list.Add(log);
                }
            }

            // Once after loading: learn the job ID abbreviations of mod job types from the offered jobs
            if (pollCount == 15) ScanOfferedJobTypes();

            // Drop protocols of jobs that are no longer active (completed elsewhere, removed by
            // other mods ...). Grace period after load so restored jobs are not lost early.
            if (++pollCount > 30)
            {
                staleLogs.Clear();
                foreach (var id in logs.Keys)
                    if (!activeJobIds.Contains(id)) staleLogs.Add(id);
                foreach (var id in staleLogs) logs.Remove(id);
            }

            if (!ZG2StationZones.IsBuilt) return;

            // 3) Station transitions per relevant trainset
            changedLogs.Clear();
            curCarState.Clear();
            foreach (var kv in jobsPerTrainset)
                UpdateTrainset(kv.Key, kv.Value);

            // 4) Hand the car states over to the next poll; cars without job trains are forgotten.
            var swap = prevCarState;
            prevCarState = curCarState;
            curCarState = swap;

            // 5) MP: push changed protocols to clients
            if (changedLogs.Count > 0 && ZG2MPUtil.IsMultiplayerActive)
            {
                foreach (var id in changedLogs)
                    if (logs.TryGetValue(id, out var l)) ZG2MPUtil.BroadcastJobLog(id, l.Serialize());
            }
        }

        private static void ScanOfferedJobTypes()
        {
            try
            {
                ZG2JobTypes.LearnAbbreviations(); // in case the ID generator was not ready at world load
                foreach (var job in SingletonBehaviour<JobsManager>.Instance.currentJobs) ZG2JobTypes.RecordSeen(job);
                foreach (var station in StationController.allStations)
                {
                    var jobs = station != null && station.logicStation != null ? station.logicStation.availableJobs : null;
                    if (jobs == null) continue;
                    foreach (var job in jobs) ZG2JobTypes.RecordSeen(job);
                }
            }
            catch (Exception ex)
            {
                Main.LogWarning($"Zg2: job type scan failed: {ex.Message}");
            }
        }

        private static void ReleaseLists()
        {
            foreach (var list in jobsPerTrainset.Values)
            {
                list.Clear();
                listPool.Push(list);
            }
            jobsPerTrainset.Clear();
        }

        private static void UpdateTrainset(Trainset ts, List<ZG2JobLog> jobs)
        {
            var cars = ts.cars;

            // State of this train: inherited from its cars (survives coupling / uncoupling)
            TrainState st = null;
            for (int i = 0; i < cars.Count && st == null; i++)
                if (cars[i] != null) prevCarState.TryGetValue(cars[i], out st);

            if (st == null)
                st = TakeSavedState(cars);

            if (st == null)
            {
                // First time seen (new job ...): position only, nothing is scored.
                st = new TrainState { AtStation = FindInnerStation(ts, NO_STATION) };
            }
            else
            {
                st = st.Clone();
                Step(ts, jobs, st);
            }

            for (int i = 0; i < cars.Count; i++)
                if (cars[i] != null) curCarState[cars[i]] = st;
        }

        /// <summary>State restored from the save game for one of the cars (consumed for all cars of the train).</summary>
        private static TrainState TakeSavedState(List<TrainCar> cars)
        {
            if (savedStates.Count == 0) return null;
            TrainState st = null;
            for (int i = 0; i < cars.Count; i++)
            {
                string id = cars[i] != null && cars[i].logicCar != null ? cars[i].logicCar.ID : null;
                if (id == null || !savedStates.TryGetValue(id, out var saved)) continue;
                savedStates.Remove(id);
                if (st == null)
                {
                    st = new TrainState
                    {
                        AtStation = ZG2StationZones.IndexOf(saved.At),
                        DepartedFrom = ZG2StationZones.IndexOf(saved.Departed)
                    };
                }
            }
            return st;
        }

        #region Save game

        /// <summary>Station state of every car of a Zg2 train (so "already departed" survives save/load).</summary>
        public static JArray SerializeTrainStates()
        {
            var arr = new JArray();
            var written = new HashSet<string>();
            foreach (var kv in prevCarState)
            {
                var car = kv.Key;
                if (car == null || car.logicCar == null) continue;
                string id = car.logicCar.ID;
                if (!written.Add(id)) continue;
                var st = kv.Value;
                if (st.AtStation == NO_STATION && st.DepartedFrom == NO_STATION) continue;
                arr.Add(new JObject
                {
                    ["car"] = id,
                    ["at"] = st.AtStation == NO_STATION ? string.Empty : ZG2StationZones.GetYardId(st.AtStation),
                    ["dep"] = st.DepartedFrom == NO_STATION ? string.Empty : ZG2StationZones.GetYardId(st.DepartedFrom)
                });
            }
            // Not yet matched since the last load → keep
            foreach (var kv in savedStates)
            {
                if (!written.Add(kv.Key)) continue;
                arr.Add(new JObject { ["car"] = kv.Key, ["at"] = kv.Value.At ?? string.Empty, ["dep"] = kv.Value.Departed ?? string.Empty });
            }
            return arr;
        }

        public static void RestoreTrainStates(JArray arr)
        {
            savedStates.Clear();
            prevCarState.Clear();
            if (arr == null) return;
            foreach (var token in arr)
            {
                if (!(token is JObject o)) continue;
                string id = o.Value<string>("car");
                if (string.IsNullOrEmpty(id)) continue;
                savedStates[id] = new SavedState { At = o.Value<string>("at"), Departed = o.Value<string>("dep") };
            }
            Main.Log($"Zg2: restored station state of {savedStates.Count} car(s).");
        }

        #endregion

        /// <summary>
        /// State machine:
        ///   in station S, other station X reached  → departure S + arrival X
        ///   in station S, no car within exit distance → departure S (scored)
        ///   outside, station X reached:
        ///       after a departure from another station → arrival X (scored)
        ///       back at the station it departed from  → logged only, not scored
        ///       without a previous departure          → logged only, not scored
        /// </summary>
        private static void Step(Trainset ts, List<ZG2JobLog> jobs, TrainState st)
        {
            var settings = Main.Settings;

            if (st.AtStation != NO_STATION)
            {
                int other = FindInnerStation(ts, st.AtStation);
                if (other == NO_STATION && AnyCarInOuter(ts, st.AtStation)) return;

                // Departure from the current station
                int from = st.AtStation;
                st.AtStation = NO_STATION;
                st.DepartedFrom = from;
                if (settings.CheckOnDeparture)
                    RecordCheckpoint(ts, jobs, ZG2CheckpointType.Departure, from);

                if (other == NO_STATION) return;
                Arrive(ts, jobs, st, other);
                return;
            }

            int entered = FindInnerStation(ts, NO_STATION);
            if (entered != NO_STATION) Arrive(ts, jobs, st, entered);
        }

        private static void Arrive(Trainset ts, List<ZG2JobLog> jobs, TrainState st, int station)
        {
            int departedFrom = st.DepartedFrom;
            st.AtStation = station;
            st.DepartedFrom = NO_STATION;

            string yard = ZG2StationZones.GetYardId(station);
            if (departedFrom == NO_STATION)
            {
                Main.Log($"Zg2 Arrival {yard}: not scored (no previous departure).");
                return;
            }
            if (departedFrom == station)
            {
                Main.Log($"Zg2 Arrival {yard}: not scored (returned to the departure station).");
                return;
            }
            if (Main.Settings.CheckOnArrival)
                RecordCheckpoint(ts, jobs, ZG2CheckpointType.Arrival, station);
        }

        /// <summary>First station (other than <paramref name="exclude"/>) whose arrival zone contains a car, or -1.</summary>
        private static int FindInnerStation(Trainset ts, int exclude)
        {
            var cars = ts.cars;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null) continue;
                int s = ZG2StationZones.FindInner(car.transform.position);
                if (s != NO_STATION && s != exclude) return s;
            }
            return NO_STATION;
        }

        private static bool AnyCarInOuter(Trainset ts, int station)
        {
            var cars = ts.cars;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car != null && ZG2StationZones.InOuter(station, car.transform.position)) return true;
            }
            return false;
        }

        private static void RecordCheckpoint(Trainset ts, List<ZG2JobLog> jobs, ZG2CheckpointType type, int station)
        {
            var result = ZG2TrainEndValidator.Check(ts);
            string yard = ZG2StationZones.GetYardId(station);

            foreach (var log in jobs)
            {
                log.Add(new ZG2Checkpoint
                {
                    Type = type,
                    Station = yard,
                    Ok = result.Ok,
                    Reason = result.Reason,
                    Valid = result.Valid,
                    Required = result.Required,
                    Value = result.Value,
                    EndCarId = result.EndCarId
                });
                changedLogs.Add(log.JobId);
            }

            if (Main.Settings.DebugLogging || !result.Ok)
            {
                var sample = jobs.Count > 0 ? jobs[0].Entries[jobs[0].Entries.Count - 1] : null;
                string reason = sample != null ? ZG2JobLog.DescribeReason(sample) : result.Reason.ToString();
                Main.Log($"Zg2 {type} {yard}: {(result.Ok ? "OK" : "FAIL – " + reason)} " +
                         $"(end car {result.EndCarId}, {jobs.Count} job(s))");
            }
        }

        #endregion
    }
}
