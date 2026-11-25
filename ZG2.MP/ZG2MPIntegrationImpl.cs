using System.Collections.Generic;
using MPAPI;
using UnityEngine;
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

        public bool HasClientReportedBoard(string carId) => boardSync.HasClientReportedBoard(carId);
        public void GetClientBoardPoses(string carId, List<ZG2BoardPose> localPoses) => boardSync.GetClientBoardPoses(carId, localPoses);
        public void NotifyBoardAttached(string carId, int boardKey, Vector3 localPosition, Vector3 localForward)
            => boardSync.NotifyBoardAttached(carId, boardKey, localPosition, localForward);
        public void NotifyBoardRemoved(string carId, int boardKey) => boardSync.NotifyBoardRemoved(carId, boardKey);
        public void BroadcastMarkerState(TrainCar car) => boardSync.BroadcastMarkerState(car);
        public void BroadcastSettings(string data) => boardSync.BroadcastSettings(data);
        public void BroadcastJobLog(string jobId, string data) => boardSync.BroadcastJobLog(jobId, data);
        public void Initialise() => boardSync.Initialise();
        public void Shutdown() => boardSync.Shutdown();
    }
}
