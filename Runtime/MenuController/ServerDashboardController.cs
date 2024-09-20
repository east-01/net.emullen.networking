using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace EMullen.Networking {
    public class ServerDashboardController : MenuController
    {

        [SerializeField]
        private TMP_Text serverDashboardText;

        private void Update() 
        {
            void SetOutputText(string text) {
                serverDashboardText.text = text;
            }

            NetworkStateManager nsm = CoreManager.NetworkStateManager;
            if(nsm == null) {
                SetOutputText("No NetworkStateManager.");
                return;
            }
            if(nsm.ServerConnectionState != FishNet.Transporting.LocalConnectionState.Started) {
                SetOutputText($"Server status: {nsm.ServerConnectionState}");
                return;
            }

            SetOutputText(GetDashboardText());
        }

        public static string GetDashboardText() 
        {
            string text = "";
            void AddLine(string message) {
                text += message + "\n";
            }

            LobbyManager lm = NetSceneController.LobbyManager;

            AddLine($"Clients: {lm.ClientManager.Clients.Count}");
            AddLine($"Lobbies: {lm.LobbyCount}");
            foreach(GameLobby lobby in lm.LobbyObjects) {
                AddLine($"Lobby \"{lobby.ID}\"");
                AddLine($"  Players: {lobby.PlayerCount}");
                foreach(PlayerData pd in lobby.Players) {
                    AddLine("    " + pd.Summary);
                }
            }
            return text;
        }

    }
}