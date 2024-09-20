using System.Collections;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using EMullen.Core;
using System;

namespace EMullen.Networking {
    /// <summary>
    /// The LobbyCommunicator is the client side of the lobby system. It has two 
    ///   major functions:
    /// 1. Initiating the connection to connect to a lobby (via the NetworkStateManager)
    /// 2. Acting as a delegate for the LobbyManager, see the reasoning for this in the
    ///      LobbyManager summary.
    /// </summary>
    public class LobbyCommunicator : MonoBehaviour
    {

        public static LobbyCommunicator Instance { get; private set;}

        [SerializeField]
        private BLogChannel logSettings;

        private bool retryUntilConnected = false;

        public string LobbyID { get; private set; }
        public LobbyData? LobbyData { get; private set; }

        public bool IsLocal = false;

        public Action ConfigureNetwork = () => {
            // if(DevSettings.IsDevelopment() || CoreManager.IsLocal)
            //     CoreManager.NetworkStateManager.UseLocalTransport();
            // else
            //     CoreManager.NetworkStateManager.UseGlobalTransport();
        };

        public Action StartNetwork = () => {
            // if(CoreManager.IsLocal)
            //     CoreManager.NetworkStateManager.StartHost();
            // else
            //     CoreManager.NetworkStateManager.StartClient();
        };

#region Events
        /// <summary>
        /// Event call for when the client joins a lobby.
        /// Invoked at the end of LobbyManager#AddToLobby
        /// </summary>
        /// <param name="lobbyID">Joined lobby ID</param>
        /// <param name="data">Initial LobbyData</param>
        public delegate void LobbyJoinedHandler(string lobbyID, LobbyData data);
        public event LobbyJoinedHandler LobbyJoinedEvent;
        public void DoNotUse_InvokeLobbyJoinedEvent(string lobbyID, LobbyData data) => LobbyJoinedEvent?.Invoke(lobbyID, data); // please beat me up for this i dont know how to make it better though
        /// <summary>
        /// Event call for when the client leaves a lobby. The lobby leave reason is also provided 
        ///   in case of non-standard lobby exit (ban or server shutdown).
        /// </summary>
        /// <param name="lobbyID">Left lobby ID</param>
        /// <param name="reason">The reason why the client left</param>
        public delegate void LobbyLeftHandler(string lobbyID, string reason);
        public event LobbyLeftHandler LobbyLeftEvent;
        public void DoNotUse_InvokeLobbyLeftEvent(string lobbyID, string reason) => LobbyLeftEvent?.Invoke(lobbyID, reason);
        /// <summary>
        /// Event call for when the server issues a message. 
        /// </summary>
        /// <param name="lobbyID">Message lobby ID</param>
        /// <param name="message">The message that the lobby sent</param>
        public delegate void LobbyMessageHandler(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message);
        public event LobbyMessageHandler LobbyMessageEvent;
        public void DoNotUse_InvokeLobbyMessageEvent(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message) => LobbyMessageEvent?.Invoke(lobbyID, sender, type, message);
        /// <summary>
        /// Event call for when the lobby is updated, reason for update is also provided.
        /// </summary>
        /// <param name="lobbyID">Updated lobby ID</param>
        /// <param name="newData">New LobbyData</param>
        /// <param name="reason">The reason why the lobby updated</param>
        public delegate void LobbyUpdateHandler(string lobbyID, LobbyData newData, LobbyUpdateReason reason);
        public event LobbyUpdateHandler LobbyUpdatedEvent;
        public void DoNotUse_InvokeLobbyUpdatedEvent(string lobbyID, LobbyData newData, LobbyUpdateReason reason) => LobbyUpdatedEvent?.Invoke(lobbyID, newData, reason);

        public delegate void CommunicationEndedHandler(string lobbyID, string reason);
        public event CommunicationEndedHandler CommunicationEndedEvent;
#endregion

        private void Awake() 
        {
            if(Instance != null) {
                Debug.LogWarning($"New LobbyCommunicator woke up while one already exists. Destroying gameObject \"{gameObject.name}\"");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(this); 
        }

        private void OnEnable() 
        { 
            LobbyJoinedEvent += LobbyCommunicator_LobbyJoinedEvent;
            LobbyLeftEvent += LobbyCommunicator_LobbyLeftEvent;
            LobbyMessageEvent += LobbyCommunicator_LobbyMessageEvent;
            LobbyUpdatedEvent += LobbyCommunicator_LobbyUpdatedEvent;

            InstanceFinder.ClientManager.OnRemoteConnectionState += ClientManager_OnClientRemoteConnectionState;
            InstanceFinder.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
        }

        private void OnDisable() 
        {
            LobbyJoinedEvent -= LobbyCommunicator_LobbyJoinedEvent;
            LobbyLeftEvent -= LobbyCommunicator_LobbyLeftEvent;
            LobbyMessageEvent -= LobbyCommunicator_LobbyMessageEvent;
            LobbyUpdatedEvent -= LobbyCommunicator_LobbyUpdatedEvent;

            InstanceFinder.ClientManager.OnRemoteConnectionState -= ClientManager_OnClientRemoteConnectionState;
            InstanceFinder.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
        }

#region Start/Stop communication

        public void StartCommunication(bool retryUntilConnected = true) 
        {
            this.retryUntilConnected = retryUntilConnected;

            BLog.Log("Starting communication.", logSettings);

            LobbyID = null;
            LobbyData = null;

            // Transport configurement and server starting
            ConfigureNetwork.Invoke();
        }

        public void StartCommunicationDelayed(bool retryUntilConnected = true, float delay = 0.1f) => StartCoroutine(StartCommunicationDelayedCoroutine(retryUntilConnected, delay));
        private IEnumerator StartCommunicationDelayedCoroutine(bool retryUntilConnected, float delay) 
        {
            yield return new WaitForSeconds(delay);
            StartCommunication(retryUntilConnected);
        }

        /// <summary>
        /// Stop communication with the lobby. If we are in a lobby when this method is executed we
        ///   will request that we're removed from the lobby and then the lobby will tell us to stop
        ///   communication.
        /// This means the method gets called twice for a full disconnect handshake, the first time
        ///   tells the lobby that we're disconnecting. Once we recieve the LobbyLeftEvent we will
        ///   call StopCommunication again.
        /// <param name="reason">The reason for stopping communication.</param>
        /// <param name="forceStop">Force stop host/client and clear LobbyID and LobbyData. This is
        ///   primarily used for when we've lost connection with the server, as we won't necessarily
        ///   get back the end of the RemoveFromLobby handshake.</param>
        /// </summary>
        public void StopCommunication(string reason = "", bool forceStop = false) 
        {
            BLog.Highlight($"Communication ended for reason \"{reason}\" lobby id: \"{LobbyID}\"");
            // First pass
            if(LobbyID != null) {
                LobbyManager.Instance.RemoveFromLobby(LobbyManager.Instance.LocalConnection, reason);
                if(!forceStop)
                    return;
            }

            NetworkController.Instance.StopNetwork();

            BLog.Log($"Left lobby \"{LobbyID}\"", logSettings, 0);
            
            LobbyID = null;
            LobbyData = null;                

            BLog.Highlight($"Communication ended for reason \"{reason}\"");

            CommunicationEndedEvent?.Invoke(LobbyID, reason);
        }
#endregion

#region Event handlers
        private void LobbyCommunicator_LobbyJoinedEvent(string lobbyID, LobbyData initialData) 
        {
            if(LobbyID != null) {
                Debug.LogWarning($"Recieved LobbyJoinEvent when we're already in a lobby (Existing LobbyID is \"{LobbyID}\")");
                return;
            }

            LobbyID = lobbyID;
            LobbyData = initialData;
            BLog.Log($"Joined lobby \"{lobbyID}\"", logSettings, 0);
        }

        private void LobbyCommunicator_LobbyLeftEvent(string lobbyID, string reason) 
        {
            if(LobbyID != lobbyID) {
                Debug.LogError($"Recieved LobbyLeftEvent when lobbyID's do not match. Current: \"{LobbyID}\" Incoming: \"{lobbyID}\"");
                return;
            }

            LobbyID = null;
            LobbyData = null;

            StopCommunication(reason);
        }

        private void LobbyCommunicator_LobbyMessageEvent(string lobbyID, NetworkConnection sender, LobbyMessageType type, string message) 
        {
            // We don't check if ID matches here because LME_CMD_FORCE_DISCONNECT messages do not contain lobbyID or sender.
            if(type == LobbyMessageType.ACTION && message.StartsWith(LobbyManager.LME_CMD_FORCE_DISCONNECT)) {
                StopCommunication(message.Replace(LobbyManager.LME_CMD_FORCE_DISCONNECT, ""));
            }
        }

        private void LobbyCommunicator_LobbyUpdatedEvent(string lobbyID, LobbyData newData, LobbyUpdateReason reason) 
        {
            if(LobbyID != lobbyID) {
                Debug.LogError($"Recieved LobbyUpdatedEvent when lobbyID's do not match. Current: \"{LobbyID}\" Incoming: \"{lobbyID}\"");
                return;
            }
            LobbyData = newData;
        }

        private void ClientManager_OnClientRemoteConnectionState(RemoteConnectionStateArgs args) 
        {
            if(!InstanceFinder.IsServerOnlyStarted && args.ConnectionState == RemoteConnectionState.Stopped) {
                StopCommunication("Client stopped.");
            }
        }

        private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args) 
        {
            if(args.ConnectionState == LocalConnectionState.Stopped && retryUntilConnected) {
                StartCommunicationDelayed(retryUntilConnected);
            } else if(args.ConnectionState == LocalConnectionState.Started) {
                retryUntilConnected = false;
            }
        }
#endregion

        public bool InLobby { get { return LobbyID != null && LobbyData.HasValue; } }

    }
}