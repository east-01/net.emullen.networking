using System;
using System.Collections.Generic;
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
        public Scene? MapScene { get { 
            if(MapSceneData is null || !NetSceneController.Instance.IsSceneRegistered(MapSceneData))
                return null;
            return NetSceneController.Instance.GetSceneElements(MapSceneData).Scene;
        } }

        public List<SceneLookupData> OwnedScenes => LobbyManager.Instance.GetOwnedScenes(Lobby.ID);

        public GameLobbySceneManager(GameLobby lobby) 
        {
            Lobby = lobby;
            SceneController.Instance.SceneRegisteredEvent += SceneDelegate_SceneRegistered;
            SceneController.Instance.SceneWillDeregisterEvent += SceneDelegate_SceneWillDeregister;
            SceneController.Instance.SceneDeregisteredEvent += SceneDelegate_SceneDeregistered;

        }

        ~GameLobbySceneManager() 
        {
            SceneController.Instance.SceneRegisteredEvent -= SceneDelegate_SceneRegistered;
            SceneController.Instance.SceneWillDeregisterEvent -= SceneDelegate_SceneWillDeregister;
            SceneController.Instance.SceneDeregisteredEvent -= SceneDelegate_SceneDeregistered;

        }

        /// <summary>
        /// Unload all of the scenes that this GameLobby owns.
        /// </summary>
        public void UnloadOwnedScenes() 
        {

        }

        public void SceneDelegate_SceneRegistered(SceneLookupData lookupData) 
        {
            if(!LobbyManager.Instance.CanClaimOwnership(Lobby.ID, lookupData)) {
                Debug.LogError($"Can't claim newly registered scene \"{lookupData}\" because it already has an owner.");
                return;
            }

            LobbyManager.Instance.ClaimScene(Lobby.ID, lookupData);
            BLog.Log($"{Lobby.MessagePrefix}Claimed scene \"{lookupData}\"", LobbyManager.Instance.LogSettingsGameLobby, 0);
        }

        public void SceneDelegate_SceneWillDeregister(SceneLookupData lookupData) 
        {
            
        }

        public void SceneDelegate_SceneDeregistered(SceneLookupData lookupData) 
        {
            if(!InstanceFinder.IsServerStarted)
                return;

            if(LobbyManager.Instance.GetOwner(lookupData) == Lobby.ID) {
                NetSceneController.Instance.UnloadSceneAsServer(lookupData);
            }
        }

        /// <summary>
        /// Send all players to the specified scene with lookupData. If shouldTrack is false, the
        ///   player will load it as a local scene for themselves, disconnected from any server's
        ///   scenes.
        /// </summary>
        public void SendAllPlayersToScene(SceneLookupData lookupData, bool shouldTrack = true)
        {
            if(!NetSceneController.Instance.IsSceneRegistered(lookupData)) {
                Debug.LogError($"Can't sent players to scene \"{lookupData}\" it is not registered.");
                return;
            }

            foreach(string playerUID in Lobby.Players) {
                NetworkConnection playerConn = PlayerDataRegistry.Instance.GetPlayerData(playerUID).GetData<NetworkIdentifierData>().GetNetworkConnection();
                if(shouldTrack) {
                    NetSceneController.Instance.AddClientToScene(playerConn, lookupData);
                } else {
                    NetSceneController.Instance.TargetRpcLoadScene(playerConn, lookupData, false);
                }
            }
        }
    }
}