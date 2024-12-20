using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using System.Linq;
using FishNet.Transporting;
using EMullen.SceneMgmt;
using EMullen.Core;
using EMullen.PlayerMgmt;
using FishNet;
using EMullen.Networking.Lobby;
using FishNet.Managing.Scened;
using UnityEngine.SceneManagement;
using System.Collections;

namespace EMullen.Networking {
    /// <summary>
    /// The LobbyManager acts mainly as the server side for the lobby system, some client side features
    ///   provided. It orchestrates lobbies, keeps track of all existing lobbies on the server and the 
    ///   clients that are connected to them. It is also responsible for accepting client connections 
    ///   and distributing them into lobbies.
    /// The LobbyManager will issue events to the client via the LobbyCommunicator. The reason
    ///   why the events are in the LobbyCommunicator and not here is because the LobbyCommunicator
    ///   is a core script that always persists. Since the LobbyManager is a NetworkBehaviour
    ///   we won't have access to it when the server/client isn't enabled.
    /// </summary>
    public class LobbyManager : NetworkBehaviour
    {

        public static LobbyManager Instance { get; private set; }

        [SerializeField]
        private string defaultSceneName;
        [SerializeField]
        private string lobbySceneName;

        [SerializeField]
        private BLogChannel logSettings;
        [SerializeField]
        private BLogChannel logSettingsGameLobby;
        public BLogChannel LogSettingsGameLobby => logSettingsGameLobby;

#region Server side fields
        /// <summary>
        /// Server side ONLY, holds a string lobbyID and the GameLobby object.
        /// Clients should access lobby information through the LobbyData struct, this is handled 
        ///   in the LobbyCommunicator.
        /// </summary>
        // [SyncObject]
        // private readonly SyncDictionary<string, GameLobby> lobbies = new();
        private Dictionary<string, GameLobby> lobbies = new();
        public List<GameLobby> LobbyObjects => lobbies.Values.ToList();
        /// <summary>
        /// Synchronized between client and server, has a NetworkConnection and the string lobbyID
        ///   that the client is connected to.
        /// </summary>
        // [SyncObject]
        // private readonly SyncDictionary<NetworkConnection, string> connectionLobbyPair = new();
        private readonly SyncDictionary<NetworkConnection, string> connectionLobbyPair = new();
        public int LobbyCount => lobbies.Count;

        /// <summary>
        /// Holds a dictionary of lobby ids and the list of scenelookupdatas they own.
        /// </summary>
        private Dictionary<string, List<SceneLookupData>> ownedScenes = new();
#endregion

#region Client side fields
        /// <summary>
        /// A float value representing the last time we added all local players to the lobby.
        /// </summary>
        private float lastAddTime;
#endregion

#region Initializers
        private void Awake() 
        {
            if(Instance != null) {
                Debug.LogWarning($"New LobbyCommunicator woke up while one already exists. Destroying gameObject \"{gameObject.name}\"");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BLog.Highlight("INSTANTIATED LOBBY MANAGER");

            InstantiateLobbyAction = InstantiateLobby;
            lastAddTime = -1;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
        }

        public override void OnStopServer() 
        {
            base.OnStopServer();
            ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            // Client side view of the client disconnecting from the server unexpectedly. This forces
            //   the client to perform disconnected actions.
            // The "server side view" is in LobbyManager#ServerManager_OnRemoteConnectionState
            if(LobbyCommunicator.Instance.InLobby) {
                LobbyCommunicator.Instance.StopCommunication("Lost connection with server.", true);
                UnityEngine.SceneManagement.SceneManager.LoadScene(defaultSceneName, LoadSceneMode.Single);
            }
        }
#endregion

        private void Update()  
        {
            LobbyObjects.ForEach(lobby => lobby.Update());

            if(InstanceFinder.IsClientStarted && LobbyCommunicator.Instance.LobbyData == null && (lastAddTime == -1 || Time.time - lastAddTime > 10f)) {
                BLog.Highlight("Is client started: " + InstanceFinder.IsClientStarted + " or is host: " + InstanceFinder.IsHostStarted);
                AddToLobby(LocalConnection, PlayerManager.Instance.LocalPlayers.Where(lp => lp != null).Select(lp => lp.UID).ToList());            
                BLog.Highlight("Added all");
                lastAddTime = Time.time;
            }

            // if(Input.GetKeyDown(KeyCode.I)) {
            //     BLog.Highlight(ServerDashboardController.GetDashboardText());
            // }
        }

        /// <summary>
        /// The function we use to create a lobby, override if you want to use custom lobby types.
        /// </summary>
        public Func<GameLobby> InstantiateLobbyAction;

        /// <summary>
        /// Creates and adds a lobby to the server.
        /// </summary>
        /// <returns></returns>
        public GameLobby CreateLobby() 
        {
            GameLobby newLobby = InstantiateLobbyAction.Invoke();
            lobbies.Add(newLobby.ID, newLobby);
            BLog.Log($"Created lobby \"{newLobby.ID}\"", logSettings, 0);
            return newLobby;
        }

        /// <summary>
        /// Default InstantiateLobbyAction, creates a default lobby.
        /// </summary>
        public GameLobby InstantiateLobby() {
            return new GameLobby();
        }
        
        [Server]
        public void DeleteLobby(string lobbyID) 
        {
            if(!lobbies.ContainsKey(lobbyID)) {
                Debug.LogError($"Can't delete lobby \"{lobbyID}\" it doesn't exist.");
                return;
            }
            GameLobby lobby = lobbies[lobbyID];
            if(lobby.PlayerCount > 0) {
                Debug.LogError("Can't delete lobby, it's not empty.");
                return;
            }
            lobbies.Remove(lobbyID);
            lobby.Delete();
            BLog.Log($"Lobby \"{lobbyID}\" deleted.", logSettings, 0);
        }

#region Client add/remove
        /// <summary>
        /// Add a client to a lobby. If the lobby add attempt fails, the client will be disconnected and
        ///   told to retry.
        /// </summary>
        /// <param name="client">The new client to be added to the lobby</param>
        /// <param name="playerUIDs">The players connecting with the client</param>
        public void AddToLobby(NetworkConnection client, List<string> playerUIDs) 
        {
            if(!IsServerInitialized) {
                ServerRpcAddToLobby(client, playerUIDs);
                return;
            }

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

            BLog.Log($"Searching for a lobby for client {client}:", logSettings, 2);
            GameLobby lobbyToJoin = GetBestFitLobby(client, playerUIDs);        

            connectionLobbyPair.Add(client, lobbyToJoin.ID);

            if(!lobbyToJoin.AddAll(playerUIDs)) {
                AbortJoin("Failed to join lobby.");
                return;
            }

            if(IsServerInitialized && !IsHostStarted)
                TargetRpcAddedToLobby(client, lobbyToJoin.ID, lobbyToJoin.Data);
            else if(IsHostStarted)
                LobbyCommunicator.Instance.DoNotUse_InvokeLobbyJoinedEvent(lobbyToJoin.ID, lobbyToJoin.Data);

            UpdateLobby(lobbyToJoin.ID, LobbyUpdateReason.PLAYER_JOIN);
            BLog.Log($"Added client {client} to lobby \"{lobbyToJoin.ID}\"", logSettings, 0);
        }
        [ServerRpc(RequireOwnership = false)]
        public void ServerRpcAddToLobby(NetworkConnection newClient, List<string> playerUIDs) => AddToLobby(newClient, playerUIDs);
        /// <summary> Used to issue LobbyJoinedEvent to the client's LobbyCommunicator. </summary>
        [TargetRpc]
        public void TargetRpcAddedToLobby(NetworkConnection client, string lobbyID, LobbyData initialData) => LobbyCommunicator.Instance.DoNotUse_InvokeLobbyJoinedEvent(lobbyID, initialData);

        /// <summary>
        /// Remove a client from a lobby with the option to provide a reason.
        /// </summary>
        /// <param name="client">The client to remove from their lobby</param>
        /// <param name="reason">The reason why the client is being removed.</param>
        public void RemoveFromLobby(NetworkConnection client, string reason = "") 
        {
            BLog.Highlight("RemoveFromLobbyCalled");
            if(!IsServerInitialized) {
                ServerRpcRemoveFromLobby(client);
                return;
            }

            BLog.Highlight("Remove from lobby is on server");

            if(!connectionLobbyPair.ContainsKey(client)) {
                Debug.LogError($"Can't remove client \"{client}\" from lobby, they're not in one.");
                return;
            }
            BLog.Highlight("Player leaving lobby");
            GameLobby lobbyToLeave = GetLobby(client);
            lobbyToLeave.RemoveClientsPlayers(client, out bool yieldsEmptyLobby);

            connectionLobbyPair.Remove(client);
            
            if(IsServerInitialized && !IsHostStarted) {
                TargetRpcRemovedFromLobby(client, lobbyToLeave.ID, reason);
                BLog.Highlight("called target removed from lobby");
            } else if(IsHostStarted)
                LobbyCommunicator.Instance.DoNotUse_InvokeLobbyLeftEvent(lobbyToLeave.ID, reason);

            if(!yieldsEmptyLobby)
                UpdateLobby(lobbyToLeave.ID, LobbyUpdateReason.PLAYER_LEAVE);

            BLog.Log($"Removed client {client} from lobby \"{lobbyToLeave.ID}\" for reason \"{reason}\"", logSettings, 0);

            if(lobbyToLeave.PlayerCount == 0) {
                DeleteLobby(lobbyToLeave.ID);
            }
        }
        [ServerRpc(RequireOwnership = false)]
        public void ServerRpcRemoveFromLobby(NetworkConnection client, string reason = "") { RemoveFromLobby(client, reason); }
        /// <summary> Used to issue LobbyLeftEvent to the client's LobbyCommunicator. </summary>
        [TargetRpc]
        public void TargetRpcRemovedFromLobby(NetworkConnection client, string lobbyID, string reason) => LobbyCommunicator.Instance.DoNotUse_InvokeLobbyLeftEvent(lobbyID, reason); 
#endregion

#region Events
        public void ServerManager_OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args) 
        {
            // Server side view of the client disconnecting from the server unexpectedly. This removes
            //   the client from the server only, the client is responsible for resetting themselves
            //   if this happens.
            // The "client side view" is in LobbyManager#OnStopClient
            if(args.ConnectionState == RemoteConnectionState.Stopped && GetLobbyID(connection) != null) {
                RemoveFromLobby(connection, "Lost connection.");
            }
        }
#endregion

#region Lobby Updating
        /// <summary>
        /// Fires the TargetRpcLobbyUpdatedEvent for all clients connected to a specific lobby
        /// </summary>
        [Server]
        public void UpdateLobby(string lobbyID, LobbyUpdateReason reason) 
        {
            if(lobbyID == null) {
                Debug.LogError("Can't update lobby because lobbyID is null.");
                return;
            }
            if(!lobbies.ContainsKey(lobbyID)) {
                Debug.LogError($"Couldn't update lobby \"{lobbyID}\" because it's not registered in the LobbyManager.");
                return;
            }
            GameLobby lobby = lobbies[lobbyID];
            LobbyData lobbyData = lobby.Data;

            // Invoke event on server
            LobbyCommunicator.Instance.DoNotUse_InvokeLobbyUpdatedEvent(lobbyID, lobbyData, reason);

            // Invoke event for clients to said lobby
            foreach(NetworkConnection client in lobby.Connections) {
                if(!Observers.Contains(client))
                    continue;
                TargetRpcLobbyUpdatedEvent(client, lobbyID, lobbyData, reason);
            }
        }

        [TargetRpc]
        public void TargetRpcLobbyUpdatedEvent(NetworkConnection conn, string lobbyID, LobbyData lobbyData, LobbyUpdateReason reason) => LobbyCommunicator.Instance.DoNotUse_InvokeLobbyUpdatedEvent(lobbyID, lobbyData, reason);
#endregion

#region Lobby Messages
        /// <summary>
        /// Send a lobby message. This will go to each recipient and invoke LobbyCommunicator#LobbyMessageEvent with the following paramenters.
        /// The lobbyID isn't required for this method to work, as long as you provide a recipients list.
        /// </summary>
        /// <param name="lobbyID">The messaged lobby id</param>
        /// <param name="type">The type of message</param>
        /// <param name="message">The message itself</param>
        /// <param name="recipients">The recipients for the message, if left null the message will go to everyone in the lobby. When populated
        ///                            the message will only go to those connections.</param>
        public void SendLobbyMessage(string lobbyID, LobbyMessageType type, string message, NetworkConnection sender = null, List<NetworkConnection> recipients = null, bool sendOnlyToServer = false) 
        {
            if(!IsServerInitialized) {
                ServerRpcSendLobbyMessage(lobbyID, base.LocalConnection, type, message, recipients);
                return;
            }

            BLog.Highlight("Sending message " + message);

            // Issue message to server
            LobbyCommunicator.Instance.DoNotUse_InvokeLobbyMessageEvent(lobbyID, sender, type, message);

            // Issue message to recipients
            if(!sendOnlyToServer) {
                recipients ??= new(GetLobby(lobbyID).Connections);
                recipients.ForEach(recipient => TargetRpcRecievedLobbyMessage(recipient, lobbyID, base.LocalConnection, type, message));
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRpcSendLobbyMessage(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message, List<NetworkConnection> recipients = null, bool sendOnlyToServer = false) 
        {
            if(sender == null || !sender.IsValid) {
                Debug.LogError("Can't pass on LobbyMessage, sender is invalid.");
                SendLobbyMessage(null, LobbyMessageType.ACTION, LME_CMD_FORCE_DISCONNECT + "Invalid message sent, sender is invalid.", sender);
                return;
            }
            // TODO: Enforce permissions (i.e. clients aren't allowed to issue action commands)
            SendLobbyMessage(lobbyID, type, message, sender: sender, recipients: recipients, sendOnlyToServer);
        }

        [TargetRpc]
        public void TargetRpcRecievedLobbyMessage(NetworkConnection client, string lobbyID, NetworkConnection sender, LobbyMessageType type, string message) => LobbyCommunicator.Instance.DoNotUse_InvokeLobbyMessageEvent(lobbyID, sender, type, message);
#endregion

#region Lobby scene ownership
        public List<SceneLookupData> GetOwnedScenes(string lobbyID) 
        {
            if(!ownedScenes.ContainsKey(lobbyID)) {
                ownedScenes.Add(lobbyID, new());
            }
            return ownedScenes[lobbyID];
        }

        public bool CanClaimOwnership(string lobbyID, SceneLookupData sceneLookupData)  
        {
            return HasLobby(lobbyID) && GetOwner(sceneLookupData) == null;
        }

        public string GetOwner(SceneLookupData sceneLookupData) 
        {
            foreach(string lobbyID in ownedScenes.Keys) {
                if(ownedScenes[lobbyID].Contains(sceneLookupData))
                    return lobbyID;
            }
            return null;
        }

        public bool ClaimScene(string lobbyID, SceneLookupData sceneLookupData) 
        {
            if(!CanClaimOwnership(lobbyID, sceneLookupData)) {
                return false;
            }
            List<SceneLookupData> scenes = GetOwnedScenes(lobbyID);
            scenes.Add(sceneLookupData);
            ownedScenes[lobbyID] = scenes;
            return true;
        }

        public void UnclaimScene(SceneLookupData sceneLookupData) {
            if(GetOwner(sceneLookupData) == null)
                return;

            string ownerID = GetOwner(sceneLookupData);
            List<SceneLookupData> scenes = GetOwnedScenes(ownerID);
            scenes.Remove(sceneLookupData);
            ownedScenes[ownerID] = scenes;
        }
#endregion

#region Matchmaking
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
                BLog.Log($"  Found \"{id}\" with {lobby.OpenSlots} open slots in state {lobby.State}. Joinable: {joinable}", logSettings, 2);
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

        
#endregion

#region Messages and Message handling
        public const string LME_CMD_REQUEST_FORCE_MAP_PICK = "#LOBBY_COMMAND#REQUEST_FORCE_MAP_PICK#";
        public const string LME_CMD_REQUEST_LOBBY_MOVE = "#LOBBY_COMMAND#REQUEST_LOBBY_MOVE";
        public const string LME_CMD_FORCE_DISCONNECT = "#LOBBY_COMMAND#FORCE_DISCONNECT#";
        /// <summary>
        /// Recieve commands from clients via the LobbyMessage system
        /// </summary>
        public void LobbyCommunicator_LobbyMessageEvent(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message) 
        {
            
        }
#endregion

#region Getters
        public bool HasLobby(string lobbyID) => lobbies.ContainsKey(lobbyID);

        /// <summary>
        /// Gets the lobby id for the LocalConnection. Is a shortcut for:
        /// <code> GetLobbyID(base.LocalConnection) </code>
        /// </summary>
        [Client]
        public string GetLobbyID() => GetLobbyID(base.LocalConnection);

        /// <summary>
        /// Gets the lobby id for a specified NetworkConnection
        /// </summary>
        public string GetLobbyID(NetworkConnection conn) 
        {
            if(!connectionLobbyPair.ContainsKey(conn))
                return null;
            return connectionLobbyPair[conn];
        }

        /// <summary>
        /// Gets the lobby that the NetworkConnection is currently in.
        /// </summary>
        [Server]
        public GameLobby GetLobby(NetworkConnection conn) 
        {
            string id = GetLobbyID(conn);
            if(id == null)
                return null;
            return GetLobby(id);
        }

        /// <summary>
        /// Gets the lobby with the specified id.
        /// </summary>
        [Server]
        public GameLobby GetLobby(string id) 
        {
            if(!lobbies.ContainsKey(id))
                return null;
            return lobbies[id];
        }
#endregion

    }

    [Serializable]
    public enum LobbyMessageType { PLAINTEXT, ACTION }
}