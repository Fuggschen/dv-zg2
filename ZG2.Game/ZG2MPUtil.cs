using MPAPI;
using MPAPI.Interfaces;

namespace ZG2
{
    /// <summary>
    /// Multiplayer utility class.
    /// Provides safe access to the MPAPI for ZG2, whether the multiplayer mod is installed or not.
    /// </summary>
    public static class ZG2MPUtil
    {
        /// <summary>
        /// Returns true when the multiplayer mod is loaded AND a session is connected.
        /// Returns false when the MP mod is absent or the player is in single-player.
        /// </summary>
        public static bool IsMultiplayerActive
        {
            get
            {
                if (!MultiplayerAPI.IsMultiplayerLoaded) return false;
                var instance = MultiplayerAPI.Instance;
                return instance != null && instance.IsConnected;
            }
        }

        /// <summary>
        /// Returns true when running in single-player OR when this instance is the
        /// multiplayer host. Returns false only for non-host MP clients.
        /// Use this to guard logic that should only execute on the authoritative side.
        /// </summary>
        public static bool IsSinglePlayerOrHost
        {
            get
            {
                if (!IsMultiplayerActive) return true;
                var instance = MultiplayerAPI.Instance;
                return instance == null || instance.IsHost;
            }
        }

        /// <summary>
        /// Returns true when the multiplayer mod is loaded, a session is connected,
        /// and this instance is a client (not the host).
        /// </summary>
        public static bool IsClient
        {
            get
            {
                if (!IsMultiplayerActive) return false;
                var instance = MultiplayerAPI.Instance;
                return instance != null && !instance.IsHost;
            }
        }

        /// <summary>
        /// Gets the current server instance, or null if not available.
        /// </summary>
        public static IServer Server => MultiplayerAPI.Server;

        /// <summary>
        /// Gets the current client instance, or null if not available.
        /// </summary>
        public static IClient Client => MultiplayerAPI.Client;
    }
}