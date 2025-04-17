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

namespace EMullen.Networking.Lobby {
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
    public partial class LobbyManager : MonoBehaviour
    {

        public static LobbyManager Instance { get; private set; }

        [SerializeField]
        private string defaultSceneName;
        [SerializeField]
        private string lobbySceneName;

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
        public int LobbyCount => lobbies.Count;

        /// <summary>
        /// Holds a dictionary of lobby ids and the list of scenelookupdatas they own.
        /// </summary>
        private Dictionary<string, List<SceneLookupData>> ownedScenes = new();
#endregion

#region Events
        /// <summary>
        /// Event call for when the lobby is updated, reason for update is also provided.
        /// </summary>
        /// <param name="lobbyID">Updated lobby ID</param>
        /// <param name="newData">New LobbyData</param>
        /// <param name="reason">The reason why the lobby updated</param>
        public delegate void LobbyUpdateHandler(string lobbyID, LobbyData newData, LobbyUpdateReason reason); 
        public event LobbyUpdateHandler LobbyUpdatedEvent;
        /// <summary>
        /// Event call for when the server issues a message. 
        /// </summary>
        /// <param name="lobbyID">Message lobby ID</param>
        /// <param name="message">The message that the lobby sent</param>
        public delegate void LobbyMessageHandler(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message);
        public event LobbyMessageHandler LobbyMessageEvent;
#endregion

        private void Awake() 
        {
            if(Instance != null) {
                Debug.LogWarning($"New LobbyManager woke up while one already exists. Destroying gameObject \"{gameObject.name}\"");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(this);

            ClientAwake();
        }

        private void OnDestroy()
        {
            ClientOnDestroy();   
        }

        private void Update()  
        {
            LobbyObjects.ForEach(lobby => lobby.Update());

            ClientManagementUpdate();
            ClientUpdate();
        }

        /// <summary>
        /// The function we use to create a lobby, override if you want to use custom lobby types.
        /// </summary>
        public Func<GameLobby> InstantiateLobbyAction = () => new GameLobby();

        /// <summary>
        /// Creates and adds a lobby to the server.
        /// </summary>
        /// <returns></returns>
        public GameLobby CreateLobby() 
        {
            GameLobby newLobby = InstantiateLobbyAction.Invoke();
            lobbies.Add(newLobby.ID, newLobby);
            BLog.Log($"Created lobby \"{newLobby.ID}\"", "Lobby", 0);
            return newLobby;
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
            BLog.Log($"Lobby \"{lobbyID}\" deleted.", "Lobby", 0);
        }

        /// <summary>
        /// Fires the TargetRpcLobbyUpdatedEvent for all clients connected to a specific lobby
        /// </summary>
        public void UpdateLobby(string lobbyID, LobbyUpdateReason reason) 
        {
            if(!InstanceFinder.IsServerStarted)
                throw new InvalidOperationException("Can't update lobby, server isn't started.");

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
            LobbyUpdatedEvent?.Invoke(lobbyID, lobbyData, reason);

            // Invoke event for clients to said lobby
            foreach(NetworkConnection client in lobby.Connections) {
                LobbyUpdateBroadcast broadcast = new(lobbyID, lobbyData, reason, null);
                InstanceFinder.ServerManager.Broadcast(client, broadcast);
            }
        }

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

#region Getters
        public bool HasLobby(string lobbyID) => lobbies.ContainsKey(lobbyID);

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

}