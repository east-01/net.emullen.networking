using System;
using FishNet.Connection;
using UnityEngine;

namespace EMullen.Networking {
    /// <summary>
    /// Uses the MenuController's popup menu system to provide important lobby messages as popups.
    /// </summary>
    public class LobbyPopupDriver : MonoBehaviour
    {
        
        private void Awake() 
        {
            CoreManager.LobbyCommunicator.CommunicationEndedEvent += LobbyCommunicator_CommunicationEndedEvent;
        }


        private void OnDestroy() 
        {
            CoreManager.LobbyCommunicator.CommunicationEndedEvent -= LobbyCommunicator_CommunicationEndedEvent;
        }

        private void LobbyCommunicator_LobbyLeftEvent(string lobbyID, string reason)
        {
            PopupMenuController.Instance.Open(PopupMenuController.POPUP_GROUP_ID_SINGLE_CONFIRM, "Disconnected from lobby", reason);
        }

        private void LobbyCommunicator_LobbyMessageEvent(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message) 
        {
            if(type == LobbyMessageType.ACTION && message.StartsWith(LobbyManager.LME_CMD_FORCE_DISCONNECT)) {
                string reason = message.Replace(LobbyManager.LME_CMD_FORCE_DISCONNECT, "");
                if(reason != "Client stopped communication.") // should be fired for this for real will update soon
                    PopupMenuController.Instance.Open(PopupMenuController.POPUP_GROUP_ID_SINGLE_CONFIRM, "Disconnected from lobby", reason);
            }
        } 

        private void LobbyCommunicator_CommunicationEndedEvent(string lobbyID, string reason)
        {
            BLog.Highlight($"Communication ended bc \"{reason}\"");
            if(reason.Length > 0)
                PopupMenuController.Instance.Open(PopupMenuController.POPUP_GROUP_ID_SINGLE_CONFIRM, "Disconnected from lobby", reason);
        }

    }
}