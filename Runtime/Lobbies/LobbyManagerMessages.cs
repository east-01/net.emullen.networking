using System;
using System.Collections.Generic;
using System.Linq;
using EMullen.Core;
using FishNet;
using FishNet.Connection;
using UnityEngine;

namespace EMullen.Networking.Lobby 
{
    public partial class LobbyManager : MonoBehaviour 
    {

        public const string LME_CMD_REQUEST_FORCE_MAP_PICK = "#LOBBY_COMMAND#REQUEST_FORCE_MAP_PICK#";
        public const string LME_CMD_REQUEST_LOBBY_MOVE = "#LOBBY_COMMAND#REQUEST_LOBBY_MOVE";
        public const string LME_CMD_FORCE_DISCONNECT = "#LOBBY_COMMAND#FORCE_DISCONNECT#";

        [Serializable]
        public enum LobbyMessageType { PLAINTEXT, ACTION }

        /// <summary>
        /// Send a lobby message. This will go to each recipient and invoke LobbyCommunicator#LobbyMessageEvent with the following paramenters.
        /// The lobbyID isn't required for this method to work, as long as you provide a recipients list.
        /// </summary>
        /// <param name="lobbyID">The messaged lobby id</param>
        /// <param name="type">The type of message</param>
        /// <param name="message">The message itself</param>
        /// <param name="recipients">The recipients for the message, if left null the message will go to everyone in the lobby. When populated
        ///                            the message will only go to those connections.</param>
        public void SendLobbyMessage(string lobbyID, LobbyMessageType type, string message, NetworkConnection sender = null, List<NetworkConnection> recipients = null) 
        {
            BLog.Highlight("Sending message " + message);

            LobbyMessageBroadcast broadcast = new LobbyMessageBroadcast(lobbyID, type, message, sender);

            if(InstanceFinder.IsServerStarted) {
                recipients ??= GetLobby(lobbyID).Connections;
                foreach(NetworkConnection recipient in recipients) {
                    InstanceFinder.ServerManager.Broadcast(recipient, broadcast);
                }
            } else if(InstanceFinder.IsClientStarted) {
                if(recipients != null)
                    throw new InvalidOperationException("Can't send lobby message to specific list of recipients, we are client only.");

                InstanceFinder.ClientManager.Broadcast(broadcast);
            }
        }
    }
}