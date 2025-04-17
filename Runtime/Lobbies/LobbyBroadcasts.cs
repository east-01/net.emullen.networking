using FishNet.Broadcast;
using FishNet.Connection;

namespace EMullen.Networking.Lobby 
{
    public struct LobbyUpdateBroadcast : IBroadcast 
    {
        public string lobbyID;
        public LobbyData data;
        public LobbyUpdateReason reason;
        public string message;

        public LobbyUpdateBroadcast(string lobbyID, LobbyData data, LobbyUpdateReason reason, string message) 
        {
            this.lobbyID = lobbyID;
            this.data = data;
            this.reason = reason;
            this.message = message;
        }
    }

    public struct LobbyMessageBroadcast : IBroadcast 
    {
        public string lobbyID;
        public LobbyManager.LobbyMessageType type;
        public string message;
        public NetworkConnection sender;

        public LobbyMessageBroadcast(string lobbyID, LobbyManager.LobbyMessageType type, string message, NetworkConnection sender) 
        {
            this.lobbyID = lobbyID;
            this.type = type;
            this.message = message;
            this.sender = sender;
        }
    }
}