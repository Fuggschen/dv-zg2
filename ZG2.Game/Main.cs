using System;
using System.Reflection;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace ZG2
{
    /// <summary>
    /// Unity Mod Manager entry point for the ZG2 Zugschlusstafeln mod.
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static ZG2Settings Settings { get; private set; }
        public static bool Enabled { get; private set; }

        private static Harmony harmony;
        private static GameObject modManagerGO;

        /// <summary>
        /// Called by UMM on mod load.
        /// </summary>
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;

            try
            {
                Settings = UnityModManager.ModSettings.Load<ZG2Settings>(modEntry);
            }
            catch (Exception ex)
            {
                modEntry.Logger.LogException("Failed to load settings, using defaults.", ex);
                Settings = new ZG2Settings();
            }

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            harmony = new Harmony(modEntry.Info.Id);

            // Load multiplayer integration if the MP mod is present
            ZG2MPUtil.LoadIntegration(modEntry);

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;

            if (value)
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                SubscribeToWorldEvents();
            }
            else
            {
                harmony.UnpatchAll(modEntry.Info.Id);
                UnsubscribeFromWorldEvents();
                TeardownModSystems();
            }

            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.OnGUI();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        private static readonly Action worldLoadedHandler = OnWorldLoaded;

        private static void SubscribeToWorldEvents()
        {
            EventHelper.SubscribeStatic(typeof(WorldStreamingInit), "LoadingFinished", worldLoadedHandler);
            UnloadWatcher.UnloadRequested += OnWorldUnload;
        }

        private static void UnsubscribeFromWorldEvents()
        {
            EventHelper.UnsubscribeStatic(typeof(WorldStreamingInit), "LoadingFinished", worldLoadedHandler);
            UnloadWatcher.UnloadRequested -= OnWorldUnload;
        }

        /// <summary>
        /// Called when the game world finishes loading.
        /// Initialises all mod subsystems.
        /// </summary>
        private static void OnWorldLoaded()
        {
            if (!Enabled) return;

            try
            {
                Log("World loaded — initialising ZG2 mod systems...");

                // Create a persistent GameObject to host MonoBehaviour-based systems
                modManagerGO = new GameObject("[ZG2 Mod Manager]");
                UnityEngine.Object.DontDestroyOnLoad(modManagerGO);

                // 1. ZG2BoardFactory.Initialise() is now called from ShopInitPatch
                //    during GlobalShopController.Awake (before this handler fires).
                //    Safe to assert it's ready:
                if (!ZG2BoardFactory.IsInitialised)
                    LogWarning("ZG2BoardFactory was not initialised by ShopInitPatch — shops may not have loaded yet.");

                // 2. Restore save data (must happen before spawning)
                ZG2SaveManager.Initialise();

                // 3. Start job tracker coroutine
                var tracker = modManagerGO.AddComponent<ZG2JobTracker>();
                tracker.Initialise();

                // 4. Spawn boards at stations one frame later. The Multiplayer mod hides all
                //    client world items in its own LoadingFinished handler, which would hide ours too.
                tracker.StartCoroutine(ZG2StationSpawner.SpawnAllNextFrame());

                // 5. Prepare penalty handler state
                ZG2PenaltyHandler.Initialise();

                // 6. Hook car deletion, scan for existing mounted boards and add map markers
                ZG2MapMarkerManager.Initialise();

                // 7. Initialise MP integration (client → host notifications)
                ZG2MPUtil.Initialise();

                Log("ZG2 mod systems initialised.");
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialise ZG2 mod: {ex}");
            }
        }

        /// <summary>
        /// Called when the game world is about to unload (return to menu / quit).
        /// </summary>
        private static void OnWorldUnload()
        {
            TeardownModSystems();
        }

        private static void TeardownModSystems()
        {
            try
            {
                ZG2MPUtil.Shutdown();
                ZG2JobTracker.Shutdown();
                ZG2MapMarkerManager.Shutdown();
                ZG2SaveManager.Shutdown();
                ZG2StationSpawner.Shutdown();
                ZG2BoardFactory.Shutdown();

                if (modManagerGO != null)
                {
                    UnityEngine.Object.Destroy(modManagerGO);
                    modManagerGO = null;
                }
            }
            catch (Exception ex)
            {
                LogError($"Error during ZG2 teardown: {ex}");
            }
        }

        #region Logging helpers

        public static void Log(string message)
        {
            ModEntry?.Logger.Log(message);
        }

        public static void LogWarning(string message)
        {
            ModEntry?.Logger.Warning(message);
        }

        public static void LogError(string message)
        {
            ModEntry?.Logger.Error(message);
        }

        #endregion
    }
}
