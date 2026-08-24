using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;

namespace ZG2
{
    /// <summary>
    /// Multiplayer shim — holds an <see cref="IZG2MPIntegration"/> implementation
    /// and delegates all MP calls to it. Defaults to a no-op implementation so
    /// ZG2.Game has zero hard dependency on the Multiplayer mod.
    ///
    /// When the MP mod is present, <see cref="LoadIntegration"/> loads ZG2.MP.dll
    /// via reflection and registers the real implementation.
    /// </summary>
    public static class ZG2MPUtil
    {
        const string MULTIPLAYER_MOD_ID = "Multiplayer";
        const string MPAPI_ASSEMBLY_NAME = "MultiplayerAPI";

        const string MP_INTEGRATION_DLL = "ZG2.MP.dll";
        const string MP_INTEGRATION_BOOTSTRAP = "ZG2.MP.Bootstrap";
        const string MP_INTEGRATION_INIT_METHOD = "Initialize";

        private static IZG2MPIntegration integration = new NoOpIntegration();

        /// <summary>
        /// Registers the real MP integration implementation.
        /// Called by the ZG2.MP Bootstrap via reflection.
        /// </summary>
        public static void Register(IZG2MPIntegration impl)
        {
            integration = impl ?? throw new ArgumentNullException(nameof(impl));
            Main.Log("ZG2MPUtil: MP integration registered.");
        }

        /// <summary>
        /// Attempts to load the ZG2.MP integration assembly if the multiplayer mod is present.
        /// Called from <see cref="Main.Load"/>.
        /// </summary>
        public static void LoadIntegration(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModEntry multiplayer = UnityModManager.FindMod(MULTIPLAYER_MOD_ID);
            var mpapiAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == MPAPI_ASSEMBLY_NAME);

            var path = Path.Combine(modEntry.Path, MP_INTEGRATION_DLL);

            try
            {
                if (multiplayer?.Enabled == true && mpapiAssembly != null)
                {
                    if (!File.Exists(path))
                    {
                        modEntry.Logger.Warning($"{MP_INTEGRATION_DLL} was not found, multiplayer integration will be disabled.");
                        return;
                    }

                    var mpAssembly = Assembly.LoadFile(path);
                    var bootstrap = mpAssembly.GetType(MP_INTEGRATION_BOOTSTRAP);

                    if (bootstrap == null)
                    {
                        modEntry.Logger.Warning($"Failed to find {MP_INTEGRATION_BOOTSTRAP} in {MP_INTEGRATION_DLL}, multiplayer support will be disabled.");
                        return;
                    }

                    var init = bootstrap.GetMethod(MP_INTEGRATION_INIT_METHOD, BindingFlags.Public | BindingFlags.Static);
                    init?.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                modEntry.Logger.Warning($"Failed to load multiplayer integration.\r\n{ex.Message}\r\n{ex.StackTrace}");
            }
        }

        #region Delegated properties

        public static bool IsMultiplayerActive => integration.IsMultiplayerActive;
        public static bool IsSinglePlayerOrHost => integration.IsSinglePlayerOrHost;
        public static bool IsClient => integration.IsClient;

        #endregion

        #region Delegated methods

        public static bool HasClientReportedBoard(string carId)
            => integration.HasClientReportedBoard(carId);

        public static void NotifyBoardAttached(string carId)
            => integration.NotifyBoardAttached(carId);

        public static void NotifyBoardRemoved(string carId)
            => integration.NotifyBoardRemoved(carId);

        public static void BroadcastSettings(float penaltyPercent, int boardsPerStation,
            bool showMapMarkers, bool spawnBoardsAtStations, float checkIntervalSeconds)
            => integration.BroadcastSettings(penaltyPercent, boardsPerStation,
                showMapMarkers, spawnBoardsAtStations, checkIntervalSeconds);

        public static void Initialise()
            => integration.Initialise();

        public static void Shutdown()
            => integration.Shutdown();

        #endregion

        #region No-op implementation

        /// <summary>
        /// Default no-op implementation used when the MP mod is not present.
        /// All properties return safe defaults; all methods are silent no-ops.
        /// </summary>
        private class NoOpIntegration : IZG2MPIntegration
        {
            public bool IsMultiplayerActive => false;
            public bool IsSinglePlayerOrHost => true;
            public bool IsClient => false;
            public bool HasClientReportedBoard(string carId) => false;
            public void NotifyBoardAttached(string carId) { }
            public void NotifyBoardRemoved(string carId) { }
            public void BroadcastSettings(float penaltyPercent, int boardsPerStation,
                bool showMapMarkers, bool spawnBoardsAtStations, float checkIntervalSeconds) { }
            public void Initialise() { }
            public void Shutdown() { }
        }

        #endregion
    }
}