using System.Collections.Generic;
using UnityEngine;

namespace ZG2
{
    /// <summary>
    /// Interface for multiplayer integration operations.
    /// Implemented by the ZG2.MP assembly when the multiplayer mod is present.
    /// ZG2.Game only interacts with MP through this interface — no MPAPI types here.
    /// </summary>
    public interface IZG2MPIntegration
    {
        /// <summary>True when the multiplayer mod is loaded AND a session is connected.</summary>
        bool IsMultiplayerActive { get; }

        /// <summary>True in single player OR when this instance is the host.</summary>
        bool IsSinglePlayerOrHost { get; }

        /// <summary>True when connected as a client (not host).</summary>
        bool IsClient { get; }

        /// <summary>Host: has any client reported a ZG2 board on the given car?</summary>
        bool HasClientReportedBoard(string carId);

        /// <summary>Host: car-local poses of all boards clients reported on the car.</summary>
        void GetClientBoardPoses(string carId, List<ZG2BoardPose> localPoses);

        /// <summary>Client → host: a board was mounted (car-local position and facing).</summary>
        void NotifyBoardAttached(string carId, int boardKey, Vector3 localPosition, Vector3 localForward);

        /// <summary>Client → host: a board was removed.</summary>
        void NotifyBoardRemoved(string carId, int boardKey);

        /// <summary>Host → clients: current map marker state of the car.</summary>
        void BroadcastMarkerState(TrainCar car);

        /// <summary>Host → clients: settings (see <see cref="ZG2Settings.ToSyncString"/>).</summary>
        void BroadcastSettings(string data);

        /// <summary>Host → clients: checkpoint protocol of a job (see <see cref="ZG2JobLog.Serialize"/>).</summary>
        void BroadcastJobLog(string jobId, string data);

        void Initialise();
        void Shutdown();
    }
}
