using UnityEngine;
using UnityModManagerNet;

namespace ZG2
{
    /// <summary>
    /// Configurable mod settings, persisted by UMM.
    /// </summary>
    public class ZG2Settings : UnityModManager.ModSettings, IDrawable
    {
        /// <summary>Fine percentage applied when a job is completed without ZG2 boards (0–100).</summary>
        [Draw("Penalty Percent", Min = 0f, Max = 100f)]
        public float PenaltyPercent = 30f;

        /// <summary>Total number of boards spawned per station (1–10). Variant is chosen randomly.</summary>
        [Draw("Boards Per Station", Min = 1, Max = 10)]
        public int BoardsPerStation = 3;

        /// <summary>Show a map marker (red dot) on wagons with mounted ZG2 boards.</summary>
        [Draw("Show Map Markers")]
        public bool ShowMapMarkers = true;

        /// <summary>Whether to spawn ZG2 boards at stations.</summary>
        [Draw("Spawn Boards At Stations")]
        public bool SpawnBoardsAtStations = true;

        /// <summary>Interval in seconds between ZG2 board presence checks (10–120).</summary>
        [Draw("Check Interval (seconds)", Min = 10f, Max = 120f)]
        public float CheckIntervalSeconds = 30f;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange()
        {
            // Clamp values to valid ranges
            PenaltyPercent = Mathf.Clamp(PenaltyPercent, 0f, 100f);
            BoardsPerStation = Mathf.Clamp(BoardsPerStation, 1, 10);
            CheckIntervalSeconds = Mathf.Clamp(CheckIntervalSeconds, 10f, 120f);
        }

        /// <summary>
        /// Draws the settings UI inside UMM's settings panel.
        /// When connected as a client in multiplayer, settings are read-only
        /// (controlled by the host and synced via packets).
        /// </summary>
        public void OnGUI()
        {
            bool isClient = ZG2MPUtil.IsClient;

            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>ZG2 Zugschlusstafeln — Settings</b>");

            if (isClient)
            {
                GUILayout.Label("<i>Settings are controlled by the host and synced automatically.</i>");
                GUILayout.Space(4);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4);

            // Penalty slider
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Penalty for missing boards: {PenaltyPercent:F0}%", GUILayout.Width(280));
            if (isClient)
            {
                GUILayout.HorizontalSlider(PenaltyPercent, 0f, 100f, GUILayout.Width(300));
            }
            else
            {
                float newPenalty = GUILayout.HorizontalSlider(PenaltyPercent, 0f, 100f, GUILayout.Width(300));
                if (newPenalty != PenaltyPercent)
                {
                    PenaltyPercent = newPenalty;
                    ZG2MPUtil.BroadcastSettings(PenaltyPercent, BoardsPerStation,
                        ShowMapMarkers, SpawnBoardsAtStations, CheckIntervalSeconds);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Boards per station
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Boards per station: {BoardsPerStation}", GUILayout.Width(280));
            if (isClient)
            {
                GUILayout.HorizontalSlider(BoardsPerStation, 1f, 10f, GUILayout.Width(300));
            }
            else
            {
                int newBoards = Mathf.RoundToInt(
                    GUILayout.HorizontalSlider(BoardsPerStation, 1f, 10f, GUILayout.Width(300)));
                if (newBoards != BoardsPerStation)
                {
                    BoardsPerStation = newBoards;
                    ZG2MPUtil.BroadcastSettings(PenaltyPercent, BoardsPerStation,
                        ShowMapMarkers, SpawnBoardsAtStations, CheckIntervalSeconds);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Map markers toggle
            GUILayout.BeginHorizontal();
            if (isClient)
            {
                GUILayout.Toggle(ShowMapMarkers, " Show map markers on wagons with ZG2 boards");
            }
            else
            {
                bool newShowMarkers = GUILayout.Toggle(ShowMapMarkers, " Show map markers on wagons with ZG2 boards");
                if (newShowMarkers != ShowMapMarkers)
                {
                    ShowMapMarkers = newShowMarkers;
                    ZG2MapMarkerManager.OnSettingChanged(ShowMapMarkers);
                    ZG2MPUtil.BroadcastSettings(PenaltyPercent, BoardsPerStation,
                        ShowMapMarkers, SpawnBoardsAtStations, CheckIntervalSeconds);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Spawn boards at stations toggle
            GUILayout.BeginHorizontal();
            if (isClient)
            {
                GUILayout.Toggle(SpawnBoardsAtStations, " Spawn ZG2 boards at stations");
            }
            else
            {
                bool newSpawnBoards = GUILayout.Toggle(SpawnBoardsAtStations, " Spawn ZG2 boards at stations");
                if (newSpawnBoards != SpawnBoardsAtStations)
                {
                    SpawnBoardsAtStations = newSpawnBoards;
                    ZG2MPUtil.BroadcastSettings(PenaltyPercent, BoardsPerStation,
                        ShowMapMarkers, SpawnBoardsAtStations, CheckIntervalSeconds);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Check interval
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Check interval: {CheckIntervalSeconds:F0}s", GUILayout.Width(280));
            if (isClient)
            {
                GUILayout.HorizontalSlider(CheckIntervalSeconds, 10f, 120f, GUILayout.Width(300));
            }
            else
            {
                float newInterval = GUILayout.HorizontalSlider(CheckIntervalSeconds, 10f, 120f, GUILayout.Width(300));
                if (newInterval != CheckIntervalSeconds)
                {
                    CheckIntervalSeconds = newInterval;
                    ZG2MPUtil.BroadcastSettings(PenaltyPercent, BoardsPerStation,
                        ShowMapMarkers, SpawnBoardsAtStations, CheckIntervalSeconds);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}
