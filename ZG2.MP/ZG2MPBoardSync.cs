using System.Collections.Generic;
using DV.Utils;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;
using UnityEngine;
using ZG2;

namespace ZG2.MP
{
    /// <summary>
    /// Manages a virtual registry of ZG2 boards that clients have placed on train cars.
    /// On the host, this tracks board placements reported by clients so that
    /// <see cref="ZG2GadgetDetector.HasZG2Board"/> can detect boards that only exist
    /// on client-side scenes (not present in the host's TrainCarCustomization).
    /// </summary>
    public class ZG2MPBoardSync
    {
        /// <summary>
        /// carId → ((playerId &lt;&lt; 32) | boardKey) → car-local pose of a client-reported board.
        /// Only maintained on the host; the poses are needed for the train end validation.
        /// </summary>
        private readonly Dictionary<string, Dictionary<long, ZG2BoardPose>> clientBoards
            = new Dictionary<string, Dictionary<long, ZG2BoardPose>>();

        private static long BoardId(byte player, int key) => ((long)player << 32) | (uint)key;
        private static byte PlayerOf(long id) => (byte)(id >> 32);

        /// <summary>
        /// Car IDs the host reports as marked. Only maintained on clients.
        /// Kept so markers can be applied to cars that spawn after the packet arrived.
        /// </summary>
        private readonly HashSet<string> hostMarkedCars = new HashSet<string>();

        private bool initialised;

        /// <summary>
        /// Subscribe to MPAPI lifecycle events. Call once on world load.
        /// </summary>
        public void Initialise()
        {
            if (initialised) return;
            initialised = true;

            clientBoards.Clear();

            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ServerStopped += OnServerStopped;
            MultiplayerAPI.ClientStarted += OnClientStarted;
            MultiplayerAPI.ClientStopped += OnClientStopped;

            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner != null)
                spawner.CarAboutToBeDeleted += OnCarDeleted;

            // If a server/client is already running (late init), hook immediately
            var server = MultiplayerAPI.Server;
            if (server != null) OnServerStarted(server);

            var client = MultiplayerAPI.Client;
            if (client != null) OnClientStarted(client);
        }

        /// <summary>
        /// Unsubscribe from all events and clear state. Call on world unload / mod disable.
        /// </summary>
        public void Shutdown()
        {
            if (!initialised) return;
            initialised = false;

            MultiplayerAPI.ServerStarted -= OnServerStarted;
            MultiplayerAPI.ServerStopped -= OnServerStopped;
            MultiplayerAPI.ClientStarted -= OnClientStarted;
            MultiplayerAPI.ClientStopped -= OnClientStopped;

            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner != null)
                spawner.CarAboutToBeDeleted -= OnCarDeleted;

            UnhookServerEvents();
            UnhookClientEvents();
            clientBoards.Clear();
        }

        /// <summary>
        /// Drops per-car state when a car is deleted, so a later car reusing the ID
        /// does not inherit boards or markers. The marker itself is removed by
        /// <see cref="ZG2MapMarkerManager"/>; clients delete the car on their side too.
        /// </summary>
        private void OnCarDeleted(TrainCar car)
        {
            string carId = car?.logicCar?.ID;
            if (carId == null) return;

            clientBoards.Remove(carId);
            hostMarkedCars.Remove(carId);
        }

        #region Host-side: receive and track client board placements

        private void OnServerStarted(IServer server)
        {
            server.RegisterPacket<ZG2BoardAttachedPacket>(HandleBoardAttached);
            server.RegisterPacket<ZG2BoardRemovedPacket>(HandleBoardRemoved);
            server.OnPlayerDisconnected += OnPlayerDisconnected;
            server.OnPlayerReady += OnPlayerReady;

            Main.Log("ZG2MPBoardSync: Server packet handlers registered.");
        }

        private void OnServerStopped()
        {
            UnhookServerEvents();
            clientBoards.Clear();
            Main.Log("ZG2MPBoardSync: Server stopped, registry cleared.");
        }

        private void UnhookServerEvents()
        {
            var server = MultiplayerAPI.Server;
            if (server != null)
            {
                server.OnPlayerDisconnected -= OnPlayerDisconnected;
                server.OnPlayerReady -= OnPlayerReady;
            }
        }

        private void HandleBoardAttached(ZG2BoardAttachedPacket packet, IPlayer sender)
        {
            if (string.IsNullOrEmpty(packet.CarId)) return;

            if (!clientBoards.TryGetValue(packet.CarId, out var boards))
            {
                boards = new Dictionary<long, ZG2BoardPose>();
                clientBoards[packet.CarId] = boards;
            }

            bool firstOnCar = boards.Count == 0;
            boards[BoardId(sender.PlayerId, packet.BoardKey)] = new ZG2BoardPose(
                new Vector3(packet.PX, packet.PY, packet.PZ),
                new Vector3(packet.FX, packet.FY, packet.FZ));

            if (Main.Settings.DebugLogging)
                Main.Log($"ZG2MPBoardSync: {sender.DisplayName} mounted board on {packet.CarId} ({boards.Count} reported).");

            if (firstOnCar)
                TryUpdateMapMarker(packet.CarId, attached: true);

            BroadcastMarkerState(FindTrainCarById(packet.CarId));
        }

        private void HandleBoardRemoved(ZG2BoardRemovedPacket packet, IPlayer sender)
        {
            if (string.IsNullOrEmpty(packet.CarId)) return;
            if (!clientBoards.TryGetValue(packet.CarId, out var boards)) return;
            if (!boards.Remove(BoardId(sender.PlayerId, packet.BoardKey))) return;

            if (boards.Count == 0)
            {
                clientBoards.Remove(packet.CarId);
                TryUpdateMapMarker(packet.CarId, attached: false);
            }

            // Also sent back to the reporting client: the host or another player may still have a board here.
            BroadcastMarkerState(FindTrainCarById(packet.CarId));
        }

        private void OnPlayerDisconnected(IPlayer player)
        {
            // Remove all board entries for this player
            var carsToClean = new List<string>();

            var tmp = new List<long>();
            foreach (var kvp in clientBoards)
            {
                tmp.Clear();
                foreach (var id in kvp.Value.Keys)
                    if (PlayerOf(id) == player.PlayerId) tmp.Add(id);
                foreach (var id in tmp) kvp.Value.Remove(id);
                if (tmp.Count > 0 && kvp.Value.Count == 0)
                    carsToClean.Add(kvp.Key);
            }

            foreach (var carId in carsToClean)
            {
                clientBoards.Remove(carId);
                TryUpdateMapMarker(carId, attached: false);
                BroadcastMarkerState(FindTrainCarById(carId));
            }

            if (carsToClean.Count > 0)
            {
                Main.Log($"ZG2MPBoardSync: Cleaned up {carsToClean.Count} board entries " +
                         $"for disconnected player {player.PlayerId} ({player.DisplayName}).");
            }
        }

        private void OnPlayerReady(IPlayer player)
        {
            // Send current settings, markers and job protocols to the newly connected player
            SendSettingsToPlayer(player);
            SendMarkersToPlayer(player);
            SendJobLogsToPlayer(player);
        }

        /// <summary>
        /// Sends the car's current marker state to all clients.
        /// Called after any host-side change: the host's own placements and removals,
        /// client reports, and disconnect cleanup.
        /// </summary>
        public void BroadcastMarkerState(TrainCar car)
        {
            var server = MultiplayerAPI.Server;
            if (server == null || car == null || car.logicCar == null) return;

            server.SendPacketToAll(new ZG2MarkerStatePacket
            {
                CarId = car.logicCar.ID,
                Marked = ZG2GadgetDetector.HasZG2Board(car)
            }, reliable: true, excludeSelf: true);
        }

        private void SendMarkersToPlayer(IPlayer player)
        {
            var server = MultiplayerAPI.Server;
            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (server == null || player == null || spawner == null) return;

            int sent = 0;
            foreach (var car in spawner.AllCars)
            {
                if (car == null || car.logicCar == null) continue;
                if (!ZG2GadgetDetector.HasZG2Board(car)) continue;

                server.SendPacketToPlayer(new ZG2MarkerStatePacket
                {
                    CarId = car.logicCar.ID,
                    Marked = true
                }, player);
                sent++;
            }

            Main.Log($"ZG2MPBoardSync: Sent {sent} marked cars to player {player.PlayerId} ({player.DisplayName}).");
        }

        /// <summary>
        /// Checks whether any client has reported a ZG2 board on the given car.
        /// </summary>
        public bool HasClientReportedBoard(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return false;
            return clientBoards.TryGetValue(carId, out var boards) && boards.Count > 0;
        }

        /// <summary>Car-local poses of all client-reported boards on the car (host).</summary>
        public void GetClientBoardPoses(string carId, List<ZG2BoardPose> localPoses)
        {
            if (string.IsNullOrEmpty(carId) || !clientBoards.TryGetValue(carId, out var boards)) return;
            localPoses.AddRange(boards.Values);
        }

        /// <summary>
        /// Attempts to find a TrainCar by its logic car ID and update its map marker.
        /// </summary>
        private void TryUpdateMapMarker(string carId, bool attached)
        {
            var car = FindTrainCarById(carId);
            if (car == null) return;

            if (attached)
                ZG2MapMarkerManager.OnBoardMounted(car);
            else
                ZG2MapMarkerManager.OnBoardRemoved(car);
        }

        /// <summary>
        /// Finds a <see cref="TrainCar"/> by its logic car ID from the global car spawner.
        /// </summary>
        private static TrainCar FindTrainCarById(string carId)
        {
            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner == null) return null;

            foreach (var car in spawner.AllCars)
            {
                if (car != null && car.logicCar != null && car.logicCar.ID == carId)
                    return car;
            }

            return null;
        }

        #endregion

        #region Client-side: receive host state

        private void OnClientStarted(IClient client)
        {
            client.RegisterPacket<ZG2SettingsSyncPacket>(HandleSettingsReceived);
            client.RegisterPacket<ZG2MarkerStatePacket>(HandleMarkerState);
            client.RegisterPacket<ZG2JobLogPacket>(HandleJobLog);

            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner != null)
                spawner.CarSpawned += OnCarSpawned;

            Main.Log("ZG2MPBoardSync: Client started, settings and marker handlers registered.");
        }

        private void OnClientStopped()
        {
            UnhookClientEvents();
            Main.Log("ZG2MPBoardSync: Client stopped.");
        }

        private void UnhookClientEvents()
        {
            var spawner = SingletonBehaviour<CarSpawner>.Instance;
            if (spawner != null)
                spawner.CarSpawned -= OnCarSpawned;

            hostMarkedCars.Clear();
        }

        /// <summary>
        /// Called on the client when the host reports a car's marker state.
        /// </summary>
        private void HandleMarkerState(ZG2MarkerStatePacket packet)
        {
            if (string.IsNullOrEmpty(packet.CarId)) return;

            if (packet.Marked)
                hostMarkedCars.Add(packet.CarId);
            else
                hostMarkedCars.Remove(packet.CarId);

            // Car may not be spawned yet; OnCarSpawned applies the marker later
            var car = FindTrainCarById(packet.CarId);
            if (car == null) return;

            if (packet.Marked)
                ZG2MapMarkerManager.OnBoardMounted(car);
            else
                ZG2MapMarkerManager.OnBoardRemoved(car);
        }

        private void OnCarSpawned(TrainCar car)
        {
            string carId = car?.logicCar?.ID;
            if (carId != null && hostMarkedCars.Contains(carId))
                ZG2MapMarkerManager.OnBoardMounted(car);
        }

        /// <summary>
        /// Adds markers for all host-marked cars that are currently spawned.
        /// </summary>
        private void ApplyHostMarkers()
        {
            foreach (var carId in hostMarkedCars)
            {
                var car = FindTrainCarById(carId);
                if (car != null)
                    ZG2MapMarkerManager.OnBoardMounted(car);
            }
        }

        #endregion

        #region Settings and job protocol sync

        public void BroadcastSettings(string data)
        {
            var server = MultiplayerAPI.Server;
            if (server == null) return;
            server.SendPacketToAll(new ZG2SettingsSyncPacket { Data = data }, reliable: true, excludeSelf: true);
        }

        private void SendSettingsToPlayer(IPlayer player)
        {
            var server = MultiplayerAPI.Server;
            if (server == null || player == null || Main.Settings == null) return;
            server.SendPacketToPlayer(new ZG2SettingsSyncPacket { Data = Main.Settings.ToSyncString() }, player);
        }

        private void HandleSettingsReceived(ZG2SettingsSyncPacket packet)
        {
            var settings = Main.Settings;
            if (settings == null) return;

            bool markersBefore = settings.ShowMapMarkers;
            settings.ApplySyncString(packet.Data);

            // ScanAndAddAll only sees local boards, so re-add the host's markers
            if (!markersBefore && settings.ShowMapMarkers)
                ApplyHostMarkers();

            Main.Log("ZG2MPBoardSync: settings received from host.");
        }

        public void BroadcastJobLog(string jobId, string data)
        {
            var server = MultiplayerAPI.Server;
            if (server == null || string.IsNullOrEmpty(jobId)) return;
            server.SendPacketToAll(new ZG2JobLogPacket { JobId = jobId, Data = data ?? string.Empty }, reliable: true, excludeSelf: true);
        }

        private void SendJobLogsToPlayer(IPlayer player)
        {
            var server = MultiplayerAPI.Server;
            if (server == null || player == null) return;
            foreach (var kv in ZG2JobTracker.AllActiveLogs())
                server.SendPacketToPlayer(new ZG2JobLogPacket { JobId = kv.Key, Data = kv.Value.Serialize() }, player);
        }

        private void HandleJobLog(ZG2JobLogPacket packet)
        {
            ZG2JobTracker.OnClientJobLog(packet.JobId, packet.Data);
        }

        #endregion

        #region Client-side: board notifications

        /// <summary>
        /// Called by the client when a ZG2 board is attached to a train car.
        /// Sends a packet to the host so it can track the placement.
        /// </summary>
        public void NotifyBoardAttached(string carId, int boardKey, Vector3 localPosition, Vector3 localForward)
        {
            if (string.IsNullOrEmpty(carId)) return;

            var client = MultiplayerAPI.Client;
            if (client == null) return;

            client.SendPacketToServer(new ZG2BoardAttachedPacket
            {
                CarId = carId,
                BoardKey = boardKey,
                PX = localPosition.x, PY = localPosition.y, PZ = localPosition.z,
                FX = localForward.x, FY = localForward.y, FZ = localForward.z
            });
            Main.Log($"ZG2MPBoardSync: Notified host of board attached to car {carId}.");
        }

        /// <summary>
        /// Called by the client when a ZG2 board is removed from a train car.
        /// Sends a packet to the host so it can update its tracking.
        /// </summary>
        public void NotifyBoardRemoved(string carId, int boardKey)
        {
            if (string.IsNullOrEmpty(carId)) return;

            var client = MultiplayerAPI.Client;
            if (client == null) return;

            client.SendPacketToServer(new ZG2BoardRemovedPacket { CarId = carId, BoardKey = boardKey });
            Main.Log($"ZG2MPBoardSync: Notified host of board removed from car {carId}.");
        }

        #endregion
    }
}