using System.Collections.Generic;
using DV.Utils;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;

namespace ZG2
{
    /// <summary>
    /// Manages a virtual registry of ZG2 boards that clients have placed on train cars.
    /// On the host, this tracks board placements reported by clients so that
    /// <see cref="ZG2GadgetDetector.HasZG2Board"/> can detect boards that only exist
    /// on client-side scenes (not present in the host's TrainCarCustomization).
    /// </summary>
    public static class ZG2MPBoardSync
    {
        /// <summary>
        /// Maps carId → (playerId → board count) for client-reported boards.
        /// Only maintained on the host. Tracks how many boards each player has on each car.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<byte, int>> clientBoards
            = new Dictionary<string, Dictionary<byte, int>>();

        private static bool initialised;

        /// <summary>
        /// Subscribe to MPAPI lifecycle events. Call once on world load.
        /// </summary>
        public static void Initialise()
        {
            if (initialised) return;
            initialised = true;

            clientBoards.Clear();

            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ServerStopped += OnServerStopped;
            MultiplayerAPI.ClientStarted += OnClientStarted;
            MultiplayerAPI.ClientStopped += OnClientStopped;

            // If a server/client is already running (late init), hook immediately
            var server = MultiplayerAPI.Server;
            if (server != null) OnServerStarted(server);

            var client = MultiplayerAPI.Client;
            if (client != null) OnClientStarted(client);
        }

        /// <summary>
        /// Unsubscribe from all events and clear state. Call on world unload / mod disable.
        /// </summary>
        public static void Shutdown()
        {
            if (!initialised) return;
            initialised = false;

            MultiplayerAPI.ServerStarted -= OnServerStarted;
            MultiplayerAPI.ServerStopped -= OnServerStopped;
            MultiplayerAPI.ClientStarted -= OnClientStarted;
            MultiplayerAPI.ClientStopped -= OnClientStopped;

            UnhookServerEvents();
            clientBoards.Clear();
        }

        #region Host-side: receive and track client board placements

        private static void OnServerStarted(IServer server)
        {
            server.RegisterPacket<ZG2BoardAttachedPacket>(HandleBoardAttached);
            server.RegisterPacket<ZG2BoardRemovedPacket>(HandleBoardRemoved);
            server.OnPlayerDisconnected += OnPlayerDisconnected;
            server.OnPlayerReady += OnPlayerReady;

            Main.Log("ZG2MPBoardSync: Server packet handlers registered.");
        }

        private static void OnServerStopped()
        {
            UnhookServerEvents();
            clientBoards.Clear();
            Main.Log("ZG2MPBoardSync: Server stopped, registry cleared.");
        }

        private static void UnhookServerEvents()
        {
            var server = MultiplayerAPI.Server;
            if (server != null)
            {
                server.OnPlayerDisconnected -= OnPlayerDisconnected;
                server.OnPlayerReady -= OnPlayerReady;
            }
        }

        private static void HandleBoardAttached(ZG2BoardAttachedPacket packet, IPlayer sender)
        {
            if (string.IsNullOrEmpty(packet.CarId)) return;

            if (!clientBoards.TryGetValue(packet.CarId, out var players))
            {
                players = new Dictionary<byte, int>();
                clientBoards[packet.CarId] = players;
            }

            players.TryGetValue(sender.PlayerId, out int count);
            players[sender.PlayerId] = count + 1;

            Main.Log($"ZG2MPBoardSync: Player {sender.PlayerId} ({sender.DisplayName}) " +
                     $"attached board to car {packet.CarId} (count: {count + 1}).");

            // Trigger map marker on host if this is the first board on this car
            if (count == 0)
                TryUpdateMapMarker(packet.CarId, attached: true);
        }

        private static void HandleBoardRemoved(ZG2BoardRemovedPacket packet, IPlayer sender)
        {
            if (string.IsNullOrEmpty(packet.CarId)) return;

            if (!clientBoards.TryGetValue(packet.CarId, out var players)) return;
            if (!players.TryGetValue(sender.PlayerId, out int count)) return;

            count--;
            if (count <= 0)
            {
                players.Remove(sender.PlayerId);
                Main.Log($"ZG2MPBoardSync: Player {sender.PlayerId} ({sender.DisplayName}) " +
                         $"removed last board from car {packet.CarId}.");
            }
            else
            {
                players[sender.PlayerId] = count;
                Main.Log($"ZG2MPBoardSync: Player {sender.PlayerId} ({sender.DisplayName}) " +
                         $"removed board from car {packet.CarId} (count: {count}).");
            }

            // Remove map marker only when no players have any boards on this car
            if (players.Count == 0)
            {
                clientBoards.Remove(packet.CarId);
                TryUpdateMapMarker(packet.CarId, attached: false);
            }
        }

        private static void OnPlayerDisconnected(IPlayer player)
        {
            // Remove all board entries for this player
            var carsToClean = new List<string>();

            foreach (var kvp in clientBoards)
            {
                if (kvp.Value.Remove(player.PlayerId) && kvp.Value.Count == 0)
                {
                    carsToClean.Add(kvp.Key);
                }
            }

            foreach (var carId in carsToClean)
            {
                clientBoards.Remove(carId);
                TryUpdateMapMarker(carId, attached: false);
            }

            if (carsToClean.Count > 0)
            {
                Main.Log($"ZG2MPBoardSync: Cleaned up {carsToClean.Count} board entries " +
                         $"for disconnected player {player.PlayerId} ({player.DisplayName}).");
            }
        }

        private static void OnPlayerReady(IPlayer player)
        {
            // Send current settings to the newly connected player
            SendSettingsToPlayer(player);
        }

        /// <summary>
        /// Checks whether any client has reported a ZG2 board on the given car.
        /// Called by <see cref="ZG2GadgetDetector.HasZG2Board"/> as a fallback.
        /// </summary>
        public static bool HasClientReportedBoard(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return false;
            return clientBoards.TryGetValue(carId, out var players) && players.Count > 0;
        }

        /// <summary>
        /// Attempts to find a TrainCar by its logic car ID and update its map marker.
        /// </summary>
        private static void TryUpdateMapMarker(string carId, bool attached)
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

        #region Client-side: send board placement notifications

        private static void OnClientStarted(IClient client)
        {
            client.RegisterPacket<ZG2SettingsSyncPacket>(HandleSettingsReceived);
            Main.Log("ZG2MPBoardSync: Client started, settings sync handler registered.");
        }

        private static void OnClientStopped()
        {
            Main.Log("ZG2MPBoardSync: Client stopped.");
        }

        #endregion

        #region Settings sync

        /// <summary>
        /// Called by the host when settings change or a new player connects.
        /// Broadcasts current settings to all connected clients.
        /// </summary>
        public static void BroadcastSettings()
        {
            var server = ZG2MPUtil.Server;
            if (server == null) return;

            var settings = Main.Settings;
            if (settings == null) return;

            server.SendPacketToAll(new ZG2SettingsSyncPacket
            {
                PenaltyPercent = settings.PenaltyPercent,
                BoardsPerStation = settings.BoardsPerStation,
                ShowMapMarkers = settings.ShowMapMarkers,
                SpawnBoardsAtStations = settings.SpawnBoardsAtStations,
                CheckIntervalSeconds = settings.CheckIntervalSeconds
            }, reliable: true, excludeSelf: true);

            Main.Log("ZG2MPBoardSync: Settings broadcast to all clients.");
        }

        /// <summary>
        /// Called by the host to send current settings to a specific player.
        /// </summary>
        public static void SendSettingsToPlayer(IPlayer player)
        {
            var server = ZG2MPUtil.Server;
            if (server == null || player == null) return;

            var settings = Main.Settings;
            if (settings == null) return;

            server.SendPacketToPlayer(new ZG2SettingsSyncPacket
            {
                PenaltyPercent = settings.PenaltyPercent,
                BoardsPerStation = settings.BoardsPerStation,
                ShowMapMarkers = settings.ShowMapMarkers,
                SpawnBoardsAtStations = settings.SpawnBoardsAtStations,
                CheckIntervalSeconds = settings.CheckIntervalSeconds
            }, player);

            Main.Log($"ZG2MPBoardSync: Settings sent to player {player.PlayerId} ({player.DisplayName}).");
        }

        /// <summary>
        /// Called on the client when a settings sync packet is received from the host.
        /// Applies the host's settings to the local settings instance.
        /// </summary>
        private static void HandleSettingsReceived(ZG2SettingsSyncPacket packet)
        {
            var settings = Main.Settings;
            if (settings == null) return;

            settings.PenaltyPercent = packet.PenaltyPercent;
            settings.BoardsPerStation = packet.BoardsPerStation;
            settings.SpawnBoardsAtStations = packet.SpawnBoardsAtStations;
            settings.CheckIntervalSeconds = packet.CheckIntervalSeconds;

            bool markersChanged = settings.ShowMapMarkers != packet.ShowMapMarkers;
            settings.ShowMapMarkers = packet.ShowMapMarkers;

            if (markersChanged)
                ZG2MapMarkerManager.OnSettingChanged(settings.ShowMapMarkers);

            Main.Log($"ZG2MPBoardSync: Settings received from host — " +
                     $"Penalty={settings.PenaltyPercent:F0}%, " +
                     $"BoardsPerStation={settings.BoardsPerStation}, " +
                     $"MapMarkers={settings.ShowMapMarkers}, " +
                     $"CheckInterval={settings.CheckIntervalSeconds:F0}s.");
        }

        /// <summary>
        /// Returns true if the current instance is a client (not host) in multiplayer.
        /// Used by the settings UI to disable editing.
        /// </summary>
        public static bool IsClientInMP => ZG2MPUtil.IsClient;

        /// <summary>
        /// Called by the client when a ZG2 board is attached to a train car.
        /// Sends a packet to the host so it can track the placement.
        /// </summary>
        public static void NotifyBoardAttached(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return;

            var client = ZG2MPUtil.Client;
            if (client == null) return;

            client.SendPacketToServer(new ZG2BoardAttachedPacket { CarId = carId });
            Main.Log($"ZG2MPBoardSync: Notified host of board attached to car {carId}.");
        }

        /// <summary>
        /// Called by the client when a ZG2 board is removed from a train car.
        /// Sends a packet to the host so it can update its tracking.
        /// </summary>
        public static void NotifyBoardRemoved(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return;

            var client = ZG2MPUtil.Client;
            if (client == null) return;

            client.SendPacketToServer(new ZG2BoardRemovedPacket { CarId = carId });
            Main.Log($"ZG2MPBoardSync: Notified host of board removed from car {carId}.");
        }

        #endregion
    }
}