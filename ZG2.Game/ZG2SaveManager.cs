using System;
using System.Collections.Generic;
using DV.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Persists ZG2 mod data (job tracking state, station spawn flags)
    /// inside the game's save file via <see cref="SaveGameManager"/>.
    /// </summary>
    public static class ZG2SaveManager
    {
        private const string SAVE_KEY = "ZG2Mod_Data";
        private const int SAVE_VERSION = 2;

        private static bool subscribed;

        /// <summary>
        /// Subscribes to the save event and loads any existing mod data from the save file.
        /// Must be called after SaveGameManager is available.
        /// </summary>
        public static void Initialise()
        {
            if (!ZG2MPUtil.IsSinglePlayerOrHost)
            {
                Main.Log("ZG2SaveManager: skipping init on MP client.");
                return;
            }

            var sgm = SingletonBehaviour<SaveGameManager>.Instance;
            if (sgm == null)
            {
                Main.LogWarning("SaveGameManager not available — cannot initialise save system.");
                return;
            }

            // Subscribe to save events
            if (!subscribed)
            {
                EventHelper.Subscribe(sgm, "OnInternalDataUpdate", new Action<SaveGameData>(OnSave));
                subscribed = true;
            }

            // Load existing data
            LoadData(sgm.data);
        }

        public static void Shutdown()
        {
            if (subscribed)
            {
                var sgm = SingletonBehaviour<SaveGameManager>.Instance;
                if (sgm != null)
                {
                    EventHelper.Unsubscribe(sgm, "OnInternalDataUpdate", new Action<SaveGameData>(OnSave));
                }
                subscribed = false;
            }
        }

        /// <summary>
        /// Called by <see cref="SaveGameManager"/> when the game is about to save.
        /// Serialises all mod state into the save data.
        /// </summary>
        private static void OnSave(SaveGameData data)
        {
            try
            {
                var root = new JObject
                {
                    ["version"] = SAVE_VERSION,
                    ["jobLogs"] = SerialiseJobLogs(),
                    ["trainStates"] = ZG2JobTracker.SerializeTrainStates(),
                    ["mountedWear"] = ZG2BoardWear.Serialize()
                };

                data.SetJObject(SAVE_KEY, root);
                Main.Log("ZG2 save data written.");
            }
            catch (Exception ex)
            {
                Main.LogError($"Failed to save ZG2 data: {ex}");
            }
        }

        /// <summary>
        /// Restores mod state from the save file.
        /// </summary>
        private static void LoadData(SaveGameData data)
        {
            if (data == null) return;

            JObject root = data.GetJObject(SAVE_KEY);
            if (root == null)
            {
                Main.Log("No existing ZG2 save data found — first run.");
                return;
            }

            try
            {
                int version = root.Value<int?>("version") ?? 0;
                if (version > SAVE_VERSION)
                {
                    Main.LogWarning($"Save data version {version} is newer than supported ({SAVE_VERSION}). Some data may be lost.");
                }

                // v1 only stored "ever had a board" – that is meaningless for the checkpoint
                // system, so v1 jobs simply start with an empty protocol.
                if (version >= 2)
                    DeserialiseJobLogs(root["jobLogs"] as JArray);
                else
                    ZG2JobTracker.Restore(null);

                ZG2JobTracker.RestoreTrainStates(root["trainStates"] as JArray);
                ZG2BoardWear.Deserialize(root["mountedWear"] as JArray);

                Main.Log("ZG2 save data loaded.");
            }
            catch (Exception ex)
            {
                Main.LogError($"Failed to load ZG2 data: {ex}");
            }
        }

        #region Serialisation helpers

        private static JArray SerialiseJobLogs()
        {
            var array = new JArray();
            foreach (var kvp in ZG2JobTracker.Logs)
            {
                array.Add(new JObject
                {
                    ["jobId"] = kvp.Key,
                    ["log"] = kvp.Value.Serialize()
                });
            }
            return array;
        }

        private static void DeserialiseJobLogs(JArray array)
        {
            var list = new List<ZG2JobLog>();
            if (array != null)
            {
                foreach (var token in array)
                {
                    if (!(token is JObject obj)) continue;
                    string jobId = obj.Value<string>("jobId");
                    if (string.IsNullOrEmpty(jobId)) continue;
                    var log = ZG2JobLog.Deserialize(jobId, obj.Value<string>("log"));
                    log.AppliedPenaltyPercent = -1f; // not validated yet
                    list.Add(log);
                }
            }
            ZG2JobTracker.Restore(list);
            Main.Log($"ZG2: restored {list.Count} job protocol(s).");
        }

        #endregion
    }
}
