using System;
using System.Collections.Generic;
using System.Linq;
using EMullen.Core;
using EMullen.PlayerMgmt;
using FishNet;
using FishNet.Connection;
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
        /// Synchronized between client and server, has a NetworkConnection and the string lobbyID
        ///   that the client is connected to.
        /// </summary>
        private Dictionary<NetworkConnection, string> connectionLobbyPair = new();

        private void ClientManagementUpdate() 
        {
            if(!InstanceFinder.IsServerStarted)
                return;

            List<NetworkConnection> clientsWithoutLobbies = InstanceFinder.ClientManager.Clients.Values.ToList().Except(connectionLobbyPair.Keys).ToList();
            foreach(NetworkConnection clientWithoutLobby in clientsWithoutLobbies) {                
                List<string> uids = GetPlayerUIDSFromConnection(clientWithoutLobby);
                if(uids.Count == 0) {
                    continue;
                }
                AddToLobby(clientWithoutLobby, uids);
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

            GameLobby lobbyToLeave = GetLobby(client);
            lobbyToLeave.RemoveClientsPlayers(client, out bool yieldsEmptyLobby);

            connectionLobbyPair.Remove(client);
            
            BLog.Log($"Removed client {client} from lobby \"{lobbyToLeave.ID}\" for reason \"{reason}\"", "Lobby", 0);

            UpdateLobby(lobbyToLeave.ID, LobbyUpdateReason.PLAYER_LEAVE);

            if(yieldsEmptyLobby)
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
                bool joinable = lobby.OpenSlots > 0/* && lobby.State == LobbyState.WAITING_FOR_PLAYERS*/;
                BLog.Log($"  Found \"{id}\" with {lobby.OpenSlots} open slots in state {lobby.State}. Joinable: {joinable}", "Lobby", 2);
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

    }
}