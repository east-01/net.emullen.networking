using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.SceneManagement;
using EMullen.Core;
using EMullen.SceneMgmt;
using EMullen.PlayerMgmt;
using System.Collections.ObjectModel;

namespace EMullen.Networking.Lobby {
    /// <summary>
    /// The GameLobby resides on the server and will delegate what to do with the players.
    /// Clients will get information about the lobby via the LobbyData struct.
    /// </summary>
    public class GameLobby 
    {

        public static int PlayerLimit = 8;
        public static readonly List<string> LobbyNames = new() { "Champ", "Craig", "Jeremiah", "Gabrial", "Sun", "Time", "Anchor", "Age" };

        public GameLobbySceneManager GLSceneManager { get; private set; }

        public string ID { get; private set; }
        public string MessagePrefix => $"({ID}) ";

        private LobbyState state;
        public LobbyState State { 
            get { return state; }
            set {
                BLog.Log($"{MessagePrefix}Setting state to {value.GetType()}", LobbyManager.Instance.LogSettingsGameLobby, 1);
                LobbyState prevState = state;
                state = value;
                LobbyStateChangedEvent?.Invoke(prevState, state);
                if(LobbyManager.Instance.HasLobby(ID))
                    LobbyManager.Instance.UpdateLobby(ID, LobbyUpdateReason.STATE_CHANGE);
            }
        }

        private readonly List<string> players = new();
        public ReadOnlyCollection<string> Players => players.AsReadOnly(); 

        public int PlayerCount => players.Count;
        public int OpenSlots => PlayerLimit - players.Count;

        public List<PlayerData> PlayerDatas => players.Select(uid => PlayerDataRegistry.Instance.GetPlayerData(uid)).ToList();
        public List<NetworkConnection> Connections => PlayerDatas.Select(pd => pd.GetData<NetworkIdentifierData>().GetNetworkConnection()).Distinct().ToList();

#region Events
        public delegate void LobbyStateChanged(LobbyState prevState, LobbyState newState);
        public event LobbyStateChanged LobbyStateChangedEvent;
#endregion

        public GameLobby() 
        {
            GLSceneManager = new GameLobbySceneManager(this);
            ID = GenerateLobbyID();

            BLog.Log($"Initialized lobby \"{ID}\"", LobbyManager.Instance.LogSettingsGameLobby, 0);
        }

        public void Delete() 
        {
            BLog.Log($"{MessagePrefix}Deleting self...", LobbyManager.Instance.LogSettingsGameLobby, 0);
        }

        public void Update() 
        {
            if(State != null) {
                State.Update();
                LobbyState newState = State.CheckForStateChange();
                if(newState != null) {
                    State = newState;
                }
            }
        }  

        /// <summary>
        /// Searches for a lobby id that isn't taken.
        /// </summary>
        public static string GenerateLobbyID() 
        {
            if(!InstanceFinder.IsServerStarted) {
                Debug.LogWarning("Generated a lobby id as a client, not sure why you need it ¯\\_(ツ)_/¯");
                return "Lobby";
            }
            for(int attempt = 0; attempt < LobbyNames.Count; attempt++) {
                string selection = LobbyNames[UnityEngine.Random.Range(0, LobbyNames.Count)];
                if(LobbyManager.Instance.GetLobby(selection) == null)
                    return selection;
            }
            Debug.LogWarning("Ran out of new lobby ids!");
            return "Lobby";
        }

#region Player Management
        /// <summary>
        /// Add a player to this GameLobby, the players' lobby ID and other elements are NOT changed
        ///   by this method, so it is important that all of that is in order before this method is
        ///   called. The lobbyID is changed in LobbyManager#AddToLobby.
        /// </summary>
        /// <param name="conn">The connection that is going to be added to this lobby.</param>
        /// <param name="data">The data that this individual player has</param>
        /// <returns>Success status</returns>
        public bool Add(string playerUID) 
        {
            if(!PlayerDataRegistry.Instance.Contains(playerUID)) {
                BLog.Log($"{MessagePrefix} Failed to add player {playerUID} to lobby, they dont have PlayerData.", LobbyManager.Instance.LogSettingsGameLobby, 0);
                return false;
            }
            PlayerData pd = PlayerDataRegistry.Instance.GetPlayerData(playerUID);

            string currentLobbyID = LobbyManager.Instance.GetLobbyID(pd.GetData<NetworkIdentifierData>().GetNetworkConnection());
            if(currentLobbyID != null && currentLobbyID != ID) {
                BLog.Log($"{MessagePrefix} Failed to add player {playerUID} to lobby \"{ID}\" thir lobby id doesn't match!", LobbyManager.Instance.LogSettingsGameLobby, 0);
                return false;
            }

            players.Add(playerUID);
            BLog.Log($"{MessagePrefix}Added player {playerUID} to lobby \"{ID}\"", LobbyManager.Instance.LogSettingsGameLobby, 0);
            return true;
        }

        /// <summary>
        /// Add a list of players to the lobby along with a single connection, this is used for
        ///   local splitscreen players connecting to a multiplayer lobby.
        /// </summary>
        /// <param name="conn">The connection that is going to be added to this lobby.</param>
        /// <param name="playerUIDs">The list of player's data that will be added to the lobby.</param>
        /// <returns>Success status</returns>
        public bool AddAll(List<string> playerUIDs) {
            foreach(string uid in playerUIDs) {
                if(!Add(uid))
                    return false;
            }
            return true;
        }

        public void Remove(string playerUID) 
        {
            if(!players.Contains(playerUID)) {
                Debug.LogError($"Can't remove player {playerUID} from lobby \"{ID}\", they're not in it.");
                return;
            }
            BLog.Log($"{MessagePrefix}Removed player {playerUID} from lobby \"{ID}\"", LobbyManager.Instance.LogSettingsGameLobby, 0);
            players.Remove(playerUID);
        }

        public void RemoveClientsPlayers(NetworkConnection client, out bool yieldsEmptyLobby) {
            List<string> toRemove = PlayerDataRegistry.Instance.GetAllData().ToList().Where(data => data.HasData<NetworkIdentifierData>() && data.GetData<NetworkIdentifierData>().clientID == client.ClientId).Select(pd => pd.GetUID()).ToList();            
            yieldsEmptyLobby = PlayerCount-toRemove.Count == 0;
            toRemove.ForEach(uid => Remove(uid));
        }
#endregion

        public LobbyData Data { get {
            return new() {
                playerUIDs = players,
                stateTypeString = this.State != null ? this.State.GetType().Name : "null",
                timeInState = State != null ? State.TimeInState : -1
            };
        } }
    } 

    [Serializable]
    public struct LobbyData 
    {
        public List<string> playerUIDs;
        public string stateTypeString;
        public float timeInState;
    }

    public enum LobbyUpdateReason 
    {
        NONE,
        STATE_CHANGE,
        PLAYER_JOIN,
        PLAYER_LEAVE
    }
}