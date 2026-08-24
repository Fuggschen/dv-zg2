using MPAPI;
using MPAPI.Types;
using ZG2;

namespace ZG2.MP
{
    /// <summary>
    /// Entry point for the ZG2 multiplayer integration.
    /// Loaded via reflection by <see cref="ZG2MPUtil.LoadIntegration"/> when the
    /// Multiplayer mod is detected.
    /// </summary>
    public static class Bootstrap
    {
        public static void Initialize()
        {
            if (!MultiplayerAPI.IsMultiplayerLoaded)
                return;

            // Set compatibility state
            MultiplayerAPI.Instance.SetModCompatibility(
                Main.ModEntry.Info.Id, MultiplayerCompatibility.All);

            // Create and register the real integration implementation
            var impl = new ZG2MPIntegrationImpl();
            ZG2MPUtil.Register(impl);

            Main.Log("ZG2.MP: Bootstrap initialized.");
        }
    }
}