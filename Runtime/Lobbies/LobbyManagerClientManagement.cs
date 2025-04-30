using System;
using System.Collections.Generic;
using System.Linq;
using EMullen.Core;
using EMullen.PlayerMgmt;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace EMullen.Networking.Lobby 
{
    /// <summary>
    /// Lobby Manager - Client management
    /// Keeps track of connected clients and puts/removes them from lobbies.
    /// </summary>
    public partial class LobbyManager : MonoBehaviour 
    {

        /// <summary>
        /// A dictionary containing joining NetworkConnections and the time they joined.
        /// </summary>
        private Dictionary<NetworkConnection, float> joiningPlayers = new();
        /// <summary>
        /// Has a NetworkConnection and the string lobbyID that the client is connected to.
        /// </summary>
        private Dictionary<NetworkConnection, string> connectionLobbyPair = new();
        /// <summary>
        /// Stores the list of player uids belonging to a specific connection.
        /// </summary>
        private Dictionary<NetworkConnection, List<string>> connectionsUIDs = new();

        private void ClientManagementAwake() 
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        }

        private void ClientManagementOnDestroy() 
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        }

        private void ClientManagementUpdate() 
        {
            if(!InstanceFinder.IsServerStarted)
                return;

            // Get the clients that are connected but not in lobby and put them in one.
            List<NetworkConnection> joinedPlayers = new(); // The list of players added this frame.
            foreach(NetworkConnection clientWithoutLobby in joiningPlayers.Keys) {   
                float timeJoined = joiningPlayers[clientWithoutLobby];
                if(Time.time - timeJoined > 10f) {
                    Debug.LogWarning($"Client {clientWithoutLobby.ClientId} has been joining for a long time, should do something abt it.");
                }             

                List<string> uids = GetPlayerUIDSFromConnection(clientWithoutLobby);
                if(uids.Count == 0) {
                    continue;
                }

                AddToLobby(clientWithoutLobby, uids);
                connectionsUIDs.Add(clientWithoutLobby, uids);
                joinedPlayers.Add(clientWithoutLobby);
            }

            foreach(NetworkConnection joinedConnection in joinedPlayers) {
                joiningPlayers.Remove(joinedConnection);
            }
        }

        /// <summary>
        /// Add a client to a lobby. If the lobby add attempt fails, the client will be disconnected and
        ///   told to retry.
        /// </summary>
        /// <param name="client">The new client to be added to the lobby</param>
        /// <param name="playerUIDs">The players connecting with the client</param>
        public void AddToLobby(NetworkConnection client, List<string> playerUIDs) 
        {
            if(!InstanceFinder.IsServerStarted)
                throw new InvalidOperationException("Can't add client to lobby, server isn't started.");

            void AbortJoin(string reason) 
            {
                SendLobbyMessage(null, LobbyMessageType.ACTION, LME_CMD_FORCE_DISCONNECT + reason, recipients: new() { client });
                BLog.Log($"Aborted adding client {client} to lobby. Reason: {reason}");
                connectionLobbyPair.Remove(client);
            }

            if(connectionLobbyPair.ContainsKey(client)) {
                AbortJoin("Already in a lobby.");
                return;
            }

            BLog.Log($"Searching for a lobby for client {client}:", "Lobby", 2);
            GameLobby lobbyToJoin = GetBestFitLobby(client, playerUIDs);        

            connectionLobbyPair.Add(client, lobbyToJoin.ID);

            if(!lobbyToJoin.AddAll(playerUIDs)) {
                AbortJoin("Failed to join lobby.");
                return;
            }

            UpdateLobby(lobbyToJoin.ID, LobbyUpdateReason.PLAYER_JOIN);
            BLog.Log($"Added client {client} to lobby \"{lobbyToJoin.ID}\"", "Lobby", 0);
        }

        /// <summary>
        /// Remove a client from a lobby with the option to provide a reason.
        /// </summary>
        /// <param name="client">The client to remove from their lobby</param>
        /// <param name="reason">The reason why the client is being removed.</param>
        public void RemoveFromLobby(NetworkConnection client, string reason = "") 
        {
            if(!InstanceFinder.IsServerStarted)
                throw new InvalidOperationException("Can't remove client from lobby, server isn't started.");
            if(!connectionLobbyPair.ContainsKey(client))
                throw new InvalidOperationException($"Can't remove client \"{client}\" from lobby, they're not in one.");
            if(!connectionsUIDs.ContainsKey(client))
                throw new InvalidOperationException($"Can't remove client \"{client}\" from lobby, they don't have any uids assigned to them.");

            GameLobby lobbyToLeave = GetLobby(client);

            List<string> uids = connectionsUIDs[client];
            foreach(string uid in uids) {
                lobbyToLeave.Remove(uid);
            }

            connectionLobbyPair.Remove(client);
            
            BLog.Log($"Removed client {client.ClientId} from lobby \"{lobbyToLeave.ID}\" for reason \"{reason}\"", "Lobby", 0);

            UpdateLobby(lobbyToLeave.ID, LobbyUpdateReason.PLAYER_LEAVE);

            if(lobbyToLeave.PlayerCount == 0)
                DeleteLobby(lobbyToLeave.ID);
        }
    
        public GameLobby GetBestFitLobby(NetworkConnection client, List<string> players) 
        {
            List<string> lobbiesToJoin = GetProspectiveLobbies(client, players);
            // TODO: Select from prospective lobbies by certain conditions? Like ping or region?
            return GetLobby(lobbiesToJoin[0]);
        }

        // TODO: Better lobby search algorithm, maybe make an HTTPS request to the server for matchmaking
        public List<string> GetProspectiveLobbies(NetworkConnection client, List<string> players) 
        {
            GameLobby lobbyToJoin = null;
            foreach(string id in lobbies.Keys) {
                GameLobby lobby = lobbies[id];
                // TODO: Add other determining factors like game state
                bool joinable = lobby.Joinable();
                BLog.Log($"  Found \"{id}\" in state {lobby.State}. Joinable: {joinable}", "Lobby", 2);
                if(joinable) {
                    lobbyToJoin = lobby;
                    break;
                }
            }

            // No lobbies to join, create a new one
            if(lobbyToJoin == null)
                lobbyToJoin = CreateLobby();

            return new List<string>() { lobbyToJoin.ID };
        }

        private List<string> GetPlayerUIDSFromConnection(NetworkConnection connection) 
        {
            List<string> uids = new();
            foreach(PlayerData pd in PlayerDataRegistry.Instance.GetAllData()) {
                if(!pd.HasData<NetworkIdentifierData>())
                    continue;

                NetworkIdentifierData nid = pd.GetData<NetworkIdentifierData>();

                if(nid.clientID == connection.ClientId) {
                    uids.Add(pd.GetUID());
                }
            }
            return uids;
        }

#region Events
        public void ServerManager_OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args) 
        {
            switch(args.ConnectionState) {
                case RemoteConnectionState.Started:
                    if(joiningPlayers.ContainsKey(connection))
                        throw new InvalidOperationException("Cannot handle join operation, the NetworkConnection is already joining!");

                    joiningPlayers.Add(connection, Time.time);
                    break;
                case RemoteConnectionState.Stopped:
                    RemoveFromLobby(connection, "Client disconnected.");
                    break;
            }
        }
        
#endregion

    }
}