using System;
using System.Collections.Generic;
using EMullen.Core;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EMullen.Networking.Lobby 
{
    public partial class LobbyManager : MonoBehaviour 
    {

        public string LobbyID { get; private set; }
        public bool InLobby => LobbyID != null;
        public LobbyData? LobbyData { get; private set; }  

        public bool IsLocal = false;

        private void ClientAwake() 
        {
            InstanceFinder.ClientManager.RegisterBroadcast<LobbyUpdateBroadcast>(OnLobbyUpdate);
            LobbyUpdatedEvent += LobbyManager_LobbyUpdateEvent;
        }

        private void ClientOnDestroy() 
        {
            InstanceFinder.ClientManager.UnregisterBroadcast<LobbyUpdateBroadcast>(OnLobbyUpdate);            
            LobbyUpdatedEvent -= LobbyManager_LobbyUpdateEvent;
        }

        private void ClientUpdate() 
        {
            // if(InLobby && !InstanceFinder.IsClientStarted) {
            //     Debug.LogWarning("Handling sudden disconnect from lobby.");
            //     SceneManager.LoadScene(defaultSceneName, LoadSceneMode.Single);
            //     LobbyID = null;
            //     LobbyData = null;
            // }
        }

        private void OnLobbyUpdate(LobbyUpdateBroadcast broadcast, Channel channel) => LobbyUpdatedEvent?.Invoke(broadcast.lobbyID, broadcast.data, broadcast.reason);

        private void LobbyManager_LobbyUpdateEvent(string lobbyID, LobbyData newData, LobbyUpdateReason reason) 
        {
            switch(reason) {
                case LobbyUpdateReason.PLAYER_JOIN:

                    if(LobbyID != null) {
                        Debug.LogWarning($"Recieved LobbyJoinEvent when we're already in a lobby (Existing LobbyID is \"{LobbyID}\")");
                        return;
                    }

                    LobbyID = lobbyID;
                    LobbyData = newData;
                    BLog.Log($"Joined lobby \"{lobbyID}\"", "Lobby", 0);

                    break;

                case LobbyUpdateReason.PLAYER_LEAVE:

                    if(LobbyID != lobbyID) {
                        Debug.LogError($"Recieved LobbyLeftEvent when lobbyID's do not match. Current: \"{LobbyID}\" Incoming: \"{lobbyID}\"");
                        return;
                    }

                    LobbyID = null;
                    LobbyData = null;

                    break;

                default:

                    if(LobbyID != lobbyID) {
                        Debug.LogError($"Recieved LobbyUpdatedEvent when lobbyID's do not match. Current: \"{LobbyID}\" Incoming: \"{lobbyID}\"");
                        return;
                    }

                    LobbyData = newData;
                    
                    break;
            }
        }

        private void LobbyCommunicator_LobbyMessageEvent(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message) 
        {
            // We don't check if ID matches here because LME_CMD_FORCE_DISCONNECT messages do not contain lobbyID or sender.
            if(type == LobbyMessageType.ACTION && message.StartsWith(LobbyManager.LME_CMD_FORCE_DISCONNECT)) {
                // StopCommunication(message.Replace(LobbyManager.LME_CMD_FORCE_DISCONNECT, ""));
                NetworkController.Instance.StopNetwork();
            }
        }
    }
}