using System.Collections.Generic;
using EMullen.Core;
using EMullen.SceneMgmt;
using FishNet;
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
            BLog.Log($"{Lobby.MessagePrefix}Claimed scene \"{lookupData}\"", LobbyManager.Instance.logSettingsGameLobby, 0);
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

    }
}