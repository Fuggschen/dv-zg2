using System;
using System.Reflection;

namespace ZG2
{
    /// <summary>
    /// Soft-dependency multiplayer detection utility.
    /// Uses reflection to query the MPAPI.MultiplayerAPI class so ZG2 has no
    /// hard compile-time reference to MultiplayerAPI.dll.
    /// All methods are safe to call whether the multiplayer mod is installed or not.
    /// </summary>
    public static class ZG2MPUtil
    {
        private static bool resolved;
        private static PropertyInfo isLoadedProp;   // MPAPI.MultiplayerAPI.IsMultiplayerLoaded
        private static PropertyInfo instanceProp;    // MPAPI.MultiplayerAPI.Instance
        private static PropertyInfo isHostProp;      // IMultiplayerAPI.IsHost
        private static PropertyInfo isConnectedProp; // IMultiplayerAPI.IsConnected

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            try
            {
                var apiType = Type.GetType("MPAPI.MultiplayerAPI, MultiplayerAPI");
                if (apiType == null)
                {
                    Main.Log("ZG2MPUtil: Multiplayer mod not found — running in single-player mode.");
                    return;
                }

                Main.Log("ZG2MPUtil: Multiplayer mod detected.");

                isLoadedProp = apiType.GetProperty("IsMultiplayerLoaded",
                    BindingFlags.Public | BindingFlags.Static);
                instanceProp = apiType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);

                // Resolve instance-level properties from the interface type
                var ifaceType = instanceProp?.PropertyType;
                if (ifaceType != null)
                {
                    isHostProp = ifaceType.GetProperty("IsHost");
                    isConnectedProp = ifaceType.GetProperty("IsConnected");
                }
            }
            catch (Exception ex)
            {
                Main.LogWarning($"ZG2MPUtil: failed to resolve MP API via reflection: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true when the multiplayer mod is loaded AND a session is connected.
        /// Returns false when the MP mod is absent or the player is in single-player.
        /// </summary>
        public static bool IsMultiplayerActive
        {
            get
            {
                Resolve();
                if (isLoadedProp == null) return false;

                try
                {
                    bool loaded = (bool)isLoadedProp.GetValue(null);
                    if (!loaded) return false;

                    object instance = instanceProp?.GetValue(null);
                    if (instance == null) return false;

                    return (bool)(isConnectedProp?.GetValue(instance) ?? false);
                }
                catch
                {
                    return false;
                }
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

                try
                {
                    object instance = instanceProp?.GetValue(null);
                    if (instance == null) return true;

                    return (bool)(isHostProp?.GetValue(instance) ?? true);
                }
                catch
                {
                    return true;
                }
            }
        }
    }
}
