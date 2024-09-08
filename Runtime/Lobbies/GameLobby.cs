using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Connection;
using FishNet.Demo.AdditiveScenes;
using FishNet.Managing.Scened;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.SceneManagement;
using EMullen.Core;

namespace EMullen.Networking {
    /// <summary>
    /// The GameLobby resides on the server and will delegate what to do with the players.
    /// Clients will get information about the lobby via the LobbyData struct.
    /// </summary>
    public class GameLobby 
    {

        public GameLobby() {}

        public static readonly float PLAYER_WAIT_TIME = 20;
        public static readonly float ROUND_END_TIME = 15;
        public static readonly float MAP_PICK_TIME = 3;
        public static readonly KeyCode FORCE_MAP_PICK_KEY = KeyCode.F4;

        private LobbyManager manager;
        private string id;

        private LobbyState _state;
        public LobbyState state { 
            get { return _state; }
            set {
                BLog.Log($"{MessagePrefix}Setting state to {value}", LogChannel.GameLobby, 1);
                LobbyState prevState = _state;
                _state = value;
                timeInState = 0;
                LobbyStateChanged(prevState, _state);
                if(manager.HasLobby(id))
                    manager.UpdateLobby(id, LobbyUpdateReason.STATE_CHANGE);
            }
        }
        private float timeInState;

        // private Dictionary<NetworkConnection, PlayerData> players = new(); // Players in lobby
        private List<PlayerData> players = new();

        /* Scene related */
        private SceneLookupData mapSceneData;

        /* Game related */
        private KartLevel? level;
        private bool CanAutoSelectLevel { get { return CoreManager.IsMultiplayer && !DevSettings.Settings.ManualLobbyPlayerWaitSwitch; } }
        public bool forceMapPick = false;
        private GameplayManager gameplayManager;

        public GameLobby(LobbyManager manager, string id) 
        {
            this.manager = manager;
            this.id = id;

            BLog.Log($"Initialized lobby \"{id}\"", LogChannel.GameLobby, 0);
            SceneController.Instance.SceneRegisteredEvent += SceneDelegate_SceneRegistered;
            SceneController.Instance.SceneWillDeregisterEvent += SceneDelegate_SceneWillDeregister;
            SceneController.Instance.SceneDeregisteredEvent += SceneDelegate_SceneDeregistered;

            state = LobbyState.WAITING_FOR_PLAYERS;
            level = null;

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if(CoreManager.IsLocal && SceneNames.IsMapScene(sceneName)) {
                state = LobbyState.RACING;
                level = CoreManager.LevelAtlas.SearchEnumBySceneName(sceneName);
            }
        }

        public void Delete() 
        {
            BLog.Log($"{MessagePrefix}Deleting self...", LogChannel.GameLobby, 0);

            SceneController.Instance.SceneRegisteredEvent -= SceneDelegate_SceneRegistered;
            SceneController.Instance.SceneWillDeregisterEvent -= SceneDelegate_SceneWillDeregister;
            SceneController.Instance.SceneDeregisteredEvent -= SceneDelegate_SceneDeregistered;

            if(PlayerCount > 0) {
                // TODO: Add disconnect message
                MovePlayersToLobby();
            }

            if(MapScene != null) {
                NetSceneController.Instance.UnloadSceneAsServer(mapSceneData);
            }
        }

        public void Update() 
        {
            // State management
            timeInState += Time.deltaTime;

            if(Input.GetKeyDown(FORCE_MAP_PICK_KEY))
                forceMapPick = true;

            if(PlayerCount == 0) {
                NetSceneController.LobbyManager.DeleteLobby(ID);
            }

            CheckState();
        }  

#region State Management
        /// <summary>
        /// Checks the current state and attempts to escalate to the next state.
        /// Can only escalate state once per frame.
        /// </summary>
        public void CheckState() 
        {
            switch(state) {
                case LobbyState.WAITING_FOR_PLAYERS:
                    bool timePassed = CanAutoSelectLevel && timeInState >= PLAYER_WAIT_TIME;
                    bool noAvailableSpace = CanAutoSelectLevel && !DevSettings.Settings.ManualLobbyPlayerWaitSwitch && OpenSlots == 0;
                    if(Input.GetKeyDown(FORCE_MAP_PICK_KEY) || 
                    noAvailableSpace || 
                    timePassed || 
                    CoreManager.IsLocal) {
                        BLog.Log($"Advanced to map selection via ForceMapPick: {Input.GetKeyDown(FORCE_MAP_PICK_KEY)}, noAvailableSpace: {noAvailableSpace}, timePassed: {timePassed}, local: {CoreManager.IsLocal}", LogChannel.GameLobby);
                        state = LobbyState.MAP_SELECTION;
                    }
                    break;
                case LobbyState.MAP_SELECTION:
                    bool autoSelectValid = CanAutoSelectLevel && timeInState >= MAP_PICK_TIME;
                    if(level == null && (autoSelectValid || forceMapPick)) {
                        forceMapPick = false;
                        KartLevel? selectedLevel;
                        if(DevSettings.Settings.OverrideMapPick)
                            selectedLevel = DevSettings.Settings.Map;
                        else 
                            selectedLevel = LevelAtlas.PickRandomLevel();

                        SetLevel(selectedLevel.Value);
                    } else if(level != null && MapScene != null/* && gameplayManager != null*/) {
                        MovePlayersToMap();                    
                        state = LobbyState.RACING;
                    }
                    break;
                case LobbyState.RACING:
                    if(gameplayManager == null)
                        return;

                    if(gameplayManager.RaceManager.Phase == RacePhase.FINISHED) {
                        AwardPoints();
                        state = LobbyState.POST_RACE;
                    }
                    break;
                case LobbyState.POST_RACE:
                    if(mapSceneData == null || !NetSceneController.Instance.IsSceneRegistered(mapSceneData)) {
                        MovePlayersToLobby();
                        state = LobbyState.WAITING_FOR_PLAYERS;
                    } else if(NetSceneController.Instance.GetSceneElements(mapSceneData).Clients.Count == 0) {
                        state = LobbyState.WAITING_FOR_PLAYERS;
                    } else if(CoreManager.IsMultiplayer && timeInState >= ROUND_END_TIME) {
                        MovePlayersToLobby();
                        state = LobbyState.WAITING_FOR_PLAYERS;
                    }
                    break;
            }
        }

        private void LobbyStateChanged(LobbyState prev, LobbyState current) 
        {
            if(current == LobbyState.MAP_SELECTION) {
                if(level != null)
                    Debug.LogWarning($"Entering map selection while the level isn't null, still on level \"{level}\"");
            }
        }
#endregion

#region Player Management
        /// <summary>
        /// Add a player to this GameLobby, the players' lobby ID and other elements are NOT changed
        ///   by this method, so it is important that all of that is in order before this method is
        ///   called. The lobbyID is changed in LobbyManager#AddToLobby.
        /// </summary>
        /// <param name="conn">The connection that is going to be added to this lobby.</param>
        /// <param name="data">The data that this individual player has</param>
        /// <returns>Success status</returns>
        public bool AddPlayer(NetworkConnection conn, PlayerData data) 
        {
            string currentLobbyID = NetSceneController.LobbyManager.GetLobbyID(conn);
            if(currentLobbyID != null && currentLobbyID != ID) {
                BLog.Log($"{MessagePrefix} Failed to add player {data.Summary} to lobby \"{id}\" thir lobby id doesn't match!", LogChannel.GameLobby, 0);
                return false;
            }

            data.connection = conn;
            players.Add(data);
            BLog.Log($"{MessagePrefix}Added player {data.Summary} to lobby \"{id}\"", LogChannel.GameLobby, 0);
            return true;
        }

        /// <summary>
        /// Add a list of players to the lobby along with a single connection, this is used for
        ///   local splitscreen players connecting to a multiplayer lobby.
        /// </summary>
        /// <param name="conn">The connection that is going to be added to this lobby.</param>
        /// <param name="players">The list of player's data that will be added to the lobby.</param>
        /// <returns>Success status</returns>
        public bool AddPlayers(NetworkConnection conn, List<PlayerData> players) {
            foreach(PlayerData pd in players) {
                if(!AddPlayer(conn, pd))
                    return false;
            }
            return true;
        }

        public void RemovePlayer(PlayerData data) 
        {
            if(!players.Contains(data)) {
                Debug.LogError($"Can't remove playerdata \"{data}\" from lobby \"{ID}\", they're not in it.");
                return;
            }
            BLog.Log($"{MessagePrefix}Removed player {data.Summary} from lobby \"{id}\"", LogChannel.GameLobby, 0);
            players.Remove(data);
        }

        public void RemoveClientsPlayers(NetworkConnection client, out bool yieldsEmptyLobby) {
            List<PlayerData> clientsPlayers = GetClientsPlayers(client);
            yieldsEmptyLobby = PlayerCount-clientsPlayers.Count == 0;
            clientsPlayers.ForEach(pd => RemovePlayer(pd));
        }

        /// <summary>
        /// Moves all players in map scene back to lobby.
        /// </summary>
        public void MovePlayersToLobby() 
        {
            foreach(PlayerData othersData in players) {
                NetSceneController.Instance.TargetRpcLoadScene(othersData.connection, new(SceneNames.MENU_LOBBY), false);
            }
        }

        public void MovePlayersToMap() 
        {
            if(mapSceneData == null) {
                Debug.LogError("Can't move players to map, map scene data is null.");
                return;
            }
            if(!NetSceneController.Instance.IsSceneRegistered(mapSceneData)) {
                Debug.LogError("Can't move players to map, the scene isn't registered");
                return;
            }

            BLog.Log($"{MessagePrefix}Sending {players.Count} player(s) to map, is server: {InstanceFinder.IsServer} is client: {InstanceFinder.IsClient}", LogChannel.GameLobby, 0);
            foreach(NetworkConnection client in Connections) {
                NetSceneController.Instance.AddClientToScene(client, mapSceneData);
            }
        }
#endregion

#region Administrative
        /// <summary>
        /// End the current round. Currently does multiple admin things:
        ///   1. Award points
        ///   2. Recall players to lobby
        /// The map will automatically delete once all players are removed.
        /// </summary>
        public void CompleteRound() 
        {
            AwardPoints();

            MovePlayersToLobby();
        }

        /// <summary>
        /// Gets the placements dictionary from the RaceManager and adds the points awarded to each PlayerData.
        /// </summary>
        public void AwardPoints() 
        {
            if(gameplayManager == null) {
                Debug.LogError("Can't award points, the gameplay manager is null.");
                return;
            }
            if(gameplayManager.RaceManager.Phase != RacePhase.FINISHED) {
                Debug.LogError("Can't award points, the RaceManager's phase isn't FINISHED");
                return;
            }

            SyncDictionary<string, RacePlacementData> placements = gameplayManager.RaceManager.GetPlacements();
            for(int i = 0; i < players.Count; i++) {
                PlayerData data = players[i];
                if(!placements.ContainsKey(data.uuid)) {
                    Debug.LogWarning($"Tried to award points to \"{data.Summary}\" but they aren't in the placements dictionary.");
                    continue;
                }
                data.points += placements[data.uuid].pointsAwarded;
            }
        }
#endregion

#region Scene/Level Management
        /// <summary>
        /// Set the level. Will load the corresponding scene on the server.
        /// </summary>
        public void SetLevel(KartLevel level) 
        {   
            if(this.level != null) {
                Debug.LogError("Can't set level, one already exists");
                return;
            }
            BLog.Log($"{MessagePrefix}Picked level {level} and requesting map scene.", LogChannel.GameLobby, 0);
            this.level = level;

            SceneLookupData newMapLookupData = new(CoreManager.LevelAtlas.RetrieveData(level).sceneName);
            NetSceneController.Instance.LoadSceneAsServer(newMapLookupData);
        }

        public void SceneDelegate_SceneRegistered(SceneLookupData lookupData) 
        {
            SceneElements elements = NetSceneController.Instance.GetSceneElements(lookupData);
            if(elements.HasOwner) {
                Debug.LogError($"Can't claim newly registered scene \"{lookupData}\" because it already has an owner.");
                return;
            }

            if(SceneNames.IsMapScene(lookupData.Name)) {
                mapSceneData = lookupData;

                GameplayManager gameplayManager = elements.GameplayManager;
                if(gameplayManager != null) {
                    RegisterGameplayManager(gameplayManager);
                } else {
                    Debug.LogError("Can't register gameplay manager, it's null.");
                    return;
                }
            } else 
                return;

            elements.Owner = this;
            elements.DeleteOnLastClientRemove = SceneNames.IsMapScene(lookupData.Name);
            NetSceneController.Instance.SetSceneElements(lookupData, elements);

            BLog.Log($"{MessagePrefix}Claimed scene \"{lookupData}\"", LogChannel.GameLobby, 0);
        }

        public void SceneDelegate_SceneWillDeregister(SceneLookupData lookupData) 
        {
            if(lookupData == mapSceneData) {
                DeregisterGameplayManager();
            }
        }

        public void SceneDelegate_SceneDeregistered(SceneLookupData lookupData) 
        {
            if(lookupData == mapSceneData) {
                mapSceneData = null;
            }
        }

        private void RegisterGameplayManager(GameplayManager gm) 
        {
            gameplayManager = gm;
            gameplayManager.SetGameLobby(this);
        }

        private void DeregisterGameplayManager() 
        {
            level = null;

            if(gameplayManager == null) {
                Debug.LogError("Can't deregister GameplayManager because it is null.");
                return;
            }
        }
#endregion

        public List<PlayerData> GetClientsPlayers(NetworkConnection client) 
        {
            List<PlayerData> clientsPlayers = new List<PlayerData>();
            foreach(PlayerData pd in players) {
                if(pd.connection == client) {
                    clientsPlayers.Add(pd);
                }
            }
            return clientsPlayers;
        }

        public LobbyData Data { get {
            List<PlayerData> players = new();
            foreach(PlayerData data in this.players) { players.Add(data); }

            return new() {
                players = players,
                state = this.state,
                timeInState = this.timeInState
            };
        } }

        public string ID { get { return id; } }
        public string MessagePrefix { get { return $"({ID}) "; } }
        public SceneLookupData MapSceneData { get { return mapSceneData; } }
        public LobbyState State { get { return state; } }
        public Scene? MapScene { get { 
            if(mapSceneData is null || !NetSceneController.Instance.IsSceneRegistered(mapSceneData))
                return null;
            return NetSceneController.Instance.GetSceneElements(mapSceneData).Scene;
        } }

        public GameplayManager GameplayManager { get { return gameplayManager; } }
        public KartLevel? Level { get { return level; } }

        public List<PlayerData> Players { get { return players; } }
        public List<NetworkConnection> Connections { get {
            List<NetworkConnection> connections = new();
            foreach(PlayerData pd in players) {
                if(!connections.Contains(pd.connection))
                    connections.Add(pd.connection);
            }
            return connections;
        } }
        public int PlayerCount { get { return players.Count; } }

        public int OpenSlots { get { return CoreManager.Instance.PlayerLimit - players.Count; } }

        public MenuLobbyController MenuLobbyController { get {
            MenuLobbyController[] lobbyControllers = GameObject.FindObjectsOfType<MenuLobbyController>();
            if(lobbyControllers.Length != 1) {
                Debug.LogError($"Found != 1 MenuLobbyControllers ({lobbyControllers.Length})");
                return null;
            }
            return lobbyControllers[0];
        } }

    }

    [Serializable]
    public struct LobbyData 
    {
        public List<PlayerData> players;
        public LobbyState state;
        public float timeInState;
    }

    [Serializable]
    public enum LobbyState 
    {
        WAITING_FOR_PLAYERS, 
        MAP_SELECTION, 
        RACING, // The lobby is in game
        POST_RACE
    }

    public enum LobbyUpdateReason 
    {
        NONE,
        STATE_CHANGE,
        PLAYER_JOIN,
        PLAYER_LEAVE
    }
}