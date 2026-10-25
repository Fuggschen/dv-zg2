using MPAPI;
using ZG2;

namespace ZG2.MP
{
    /// <summary>
    /// Real implementation of <see cref="IZG2MPIntegration"/> that uses the MPAPI.
    /// Created and registered by <see cref="Bootstrap"/> when the multiplayer mod is present.
    /// </summary>
    public class ZG2MPIntegrationImpl : IZG2MPIntegration
    {
        private readonly ZG2MPBoardSync boardSync = new ZG2MPBoardSync();

        public bool IsMultiplayerActive
        {
            get
            {
                if (!MultiplayerAPI.IsMultiplayerLoaded) return false;
                var instance = MultiplayerAPI.Instance;
                return instance != null && instance.IsConnected;
            }
        }

        public bool IsSinglePlayerOrHost
        {
            get
            {
                if (!IsMultiplayerActive) return true;
                var instance = MultiplayerAPI.Instance;
                return instance == null || instance.IsHost;
            }
        }

        public bool IsClient
        {
            get
            {
                if (!IsMultiplayerActive) return false;
                var instance = MultiplayerAPI.Instance;
                return instance != null && !instance.IsHost;
            }
        }

        public bool HasClientReportedBoard(string carId)
            => boardSync.HasClientReportedBoard(carId);

        public void NotifyBoardAttached(string carId)
            => boardSync.NotifyBoardAttached(carId);

        public void NotifyBoardRemoved(string carId)
            => boardSync.NotifyBoardRemoved(carId);

        public void BroadcastMarkerState(TrainCar car)
            => boardSync.BroadcastMarkerState(car);

        public void BroadcastSettings(float penaltyPercent, int boardsPerStation,
            bool showMapMarkers, bool spawnBoardsAtStations, float checkIntervalSeconds)
            => boardSync.BroadcastSettings(penaltyPercent, boardsPerStation,
                showMapMarkers, spawnBoardsAtStations, checkIntervalSeconds);

        public void Initialise()
            => boardSync.Initialise();

        public void Shutdown()
            => boardSync.Shutdown();
    }
}