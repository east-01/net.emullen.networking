using System;
using System.Collections.Generic;
using System.Linq;
using EMullen.Core;
using EMullen.PlayerMgmt;
using EMullen.SceneMgmt;
using FishNet;
using FishNet.Component.Prediction;
using FishNet.Connection;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EMullen.Networking.Lobby 
{
    /// <summary>
    /// This class acts as a delegate for scenes to the GameLobby by providing utilities useful for
    ///   what you'd expect in games:
    ///   - Move all players to a map
    ///   - Move players out of a map
    /// </summary>
    public class GameLobbySceneManager 
    {

        public GameLobby Lobby { get; private set;}

        public SceneLookupData MapSceneData { get; private set; }
        public Scene? MapScene { get; private set; }

        public List<SceneLookupData> OwnedScenes => LobbyManager.Instance.GetOwnedScenes(Lobby.ID);

        public GameLobbySceneManager(GameLobby lobby) 
        {
            Lobby = lobby;

            InstanceFinder.SceneManager.OnLoadEnd += FishNetSceneManager_OnLoadEnd;
        }

        ~GameLobbySceneManager() 
        {
            InstanceFinder.SceneManager.OnLoadEnd -= FishNetSceneManager_OnLoadEnd;
        }

        private void FishNetSceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
        {
            Scene[] loadedScenes = args.LoadedScenes;

            foreach(Scene scene in loadedScenes) {
                SceneLookupData lookupData = scene.GetSceneLookupData();

                if(!LobbyManager.Instance.CanClaimOwnership(Lobby.ID, lookupData)) {
                    Debug.LogError($"Can't claim newly loaded scene \"{lookupData}\" because it already has an owner.");
                    return;
                }

                if(LobbyManager.Instance.ClaimScene(Lobby.ID, lookupData)) {
                    BLog.Log($"{Lobby.MessagePrefix}Claimed scene \"{lookupData}\"", "GameLobby", 0);
                    Lobby.ClaimedScene(lookupData);
                }
            }
        }

        /// <summary>
        /// Unload all of the scenes that this GameLobby owns.
        /// </summary>
        public void UnloadOwnedScenes() 
        {

        }

        /// <summary>
        /// Send all players to the specified scene with lookupData. If shouldTrack is false, the
        ///   player will load it as a local scene for themselves, disconnected from any server's
        ///   scenes.
        /// </summary>
        public void LoadPlayersScene(SceneLookupData lookupData)
        {
            SceneSyncBroadcast broadcast = new(new() { lookupData });

            foreach(string playerUID in Lobby.Players) {
                // Retrieve the network connection related to the players uid
                NetworkConnection playerConn = PlayerDataRegistry.Instance.GetPlayerData(playerUID).GetData<NetworkIdentifierData>().GetNetworkConnection();
                InstanceFinder.ServerManager.Broadcast(playerConn, broadcast);
            }
        }
    }
}