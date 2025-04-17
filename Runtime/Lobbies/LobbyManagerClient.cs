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
        }

        private void ClientOnDestroy() 
        {
            InstanceFinder.ClientManager.UnregisterBroadcast<LobbyUpdateBroadcast>(OnLobbyUpdate);            
        }

        private void ClientUpdate() 
        {
            if(InLobby && !InstanceFinder.IsClientStarted) {
                Debug.LogWarning("Handling sudden disconnect from lobby.");
                SceneManager.LoadScene(defaultSceneName, LoadSceneMode.Single);
                LobbyID = null;
                LobbyData = null;
            }
        }

        private void OnLobbyUpdate(LobbyUpdateBroadcast broadcast, Channel channel) 
        {
            switch(broadcast.reason) {
                case LobbyUpdateReason.PLAYER_JOIN:

                    if(LobbyID != null) {
                        Debug.LogWarning($"Recieved LobbyJoinEvent when we're already in a lobby (Existing LobbyID is \"{LobbyID}\")");
                        return;
                    }

                    LobbyID = broadcast.lobbyID;
                    LobbyData = broadcast.data;
                    BLog.Log($"Joined lobby \"{broadcast.lobbyID}\"", "Lobby", 0);

                    break;

                case LobbyUpdateReason.PLAYER_LEAVE:

                    if(LobbyID != broadcast.lobbyID) {
                        Debug.LogError($"Recieved LobbyLeftEvent when lobbyID's do not match. Current: \"{LobbyID}\" Incoming: \"{broadcast.lobbyID}\"");
                        return;
                    }

                    LobbyID = null;
                    LobbyData = null;

                    break;

                default:

                    if(LobbyID != broadcast.lobbyID) {
                        Debug.LogError($"Recieved LobbyUpdatedEvent when lobbyID's do not match. Current: \"{LobbyID}\" Incoming: \"{broadcast.lobbyID}\"");
                        return;
                    }

                    LobbyData = broadcast.data;

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