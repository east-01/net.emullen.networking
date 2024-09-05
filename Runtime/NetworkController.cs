using System;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using TMPro;
using UnityEngine;

namespace EMullen.Networking {
    /// <summary>
    /// The Network Controller is a component that should always exist during the lifetime of the
    ///   game. It allows you to handle networked connections without a NetworkBehaviour- meaning 
    ///   it always persists.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkController : MonoBehaviour
    {

        public static NetworkController Instance { get; private set; }

        private NetworkManager networkManager;

        [SerializeField]
        private NetworkConfiguration networkConfig;

        [SerializeField] 
        private LocalConnectionState serverConnectionState;
        public LocalConnectionState ServerConnectionState => serverConnectionState;
        [SerializeField] 
        private LocalConnectionState clientConnectionState;
        public LocalConnectionState ClientConnectionState => clientConnectionState;

        [SerializeField]
        private KeyCode debugCanvasKeyCode = KeyCode.F3;
        [SerializeField]
        private GameObject debugCanvas;
        [SerializeField]
        private TMP_Text clientStatusText;
        [SerializeField]
        private TMP_Text serverStatusText;

        /// <summary>
        /// Used for communcation started/ended events, the update method checks if the connected 
        /// </summary>
        private bool trackedConnectionStatus = false;

#region Convenience fields
        /// <summary>
        /// The least-restrictive of connection statuses, returns true if the server's status is
        ///   anything but LocalConnectionState.Stopped
        /// </summary>
        public bool IsServerActive => ServerConnectionState != LocalConnectionState.Stopped;
        /// <summary>
        /// Checks if the servers's status is LocalConnectionState.Started
        /// </summary>
        public bool IsServerStarted => ServerConnectionState == LocalConnectionState.Started;

        /// <summary>
        /// The least-restrictive of connection statuses, returns true if the client's status is
        ///   anything but LocalConnectionState.Stopped
        /// </summary>
        public bool IsClientActive => ClientConnectionState != LocalConnectionState.Stopped;
        /// <summary>
        /// Checks if the client's status is LocalConnectionState.Started
        /// </summary>
        public bool IsClientStarted => ClientConnectionState == LocalConnectionState.Started;
        /// <summary>
        /// Checks if the client is stated and we have a local connection to refer to.
        /// </summary>
        public bool IsClientConnected => IsClientConnected && LocalConnection != null /* TODO: This needs to be if our local connection exists */;

        public NetworkConnection LocalConnection => null; // TODO: This needs to be added
#endregion

#region Events
        public delegate void ConnectedHandler();
        public event ConnectedHandler ConnectedEvent;
        public delegate void DisconnectedHandler(string message = null);
        public event DisconnectedHandler DisconnectedEvent;
#endregion

        void Start() 
        {
            Debug.LogWarning("This is not going to work without finishing NetworkController#LocalConnection field");

            networkManager = GetComponent<NetworkManager>();

            networkManager.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
        }

        private void OnDestroy()
        {
            if (networkManager == null)
                return;

            networkManager.ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
        }

        void Update()
        {
            if(Input.GetKeyDown(KeyCode.F3)) {
                debugCanvas.SetActive(!debugCanvas.activeSelf);
            }

            if(trackedConnectionStatus == false && IsClientConnected) {
                trackedConnectionStatus = true;
                ConnectedEvent?.Invoke();
            } else if(trackedConnectionStatus == true && !IsClientConnected) {
                trackedConnectionStatus = false;
                DisconnectedEvent?.Invoke();
            }
        }

#region Server/Client Start and Stop
        /// <summary>
        /// Starts the network using the currently set NetworkConfiguration.
        /// </summary>
        public void StartNetwork() 
        {
            if(IsServerActive || IsClientActive) {
                string connections = String.Join(", ", new string[] {IsServerActive ? "Server" : "", IsClientActive ? "Client" : ""});
                Debug.LogError($"Can't start network. Some connections ({connections}) are active.");
                return;
            }

            if(networkConfig == null) {
                Debug.LogError("Can't start network. Network configuration is null.");
                return;
            }

            networkConfig.transport.SetServerBindAddress(networkConfig.serverBindAddress, networkConfig.ipAddressType);
            networkConfig.transport.SetClientAddress(networkConfig.clientAddress);
            networkConfig.transport.SetPort(networkConfig.port);

            networkManager.TransportManager.Transport = networkConfig.transport;

            if(networkConfig.isServer)
                networkManager.ServerManager.StartConnection();

            if(networkConfig.isClient)
                networkManager.ClientManager.StartConnection();

        }

        /// <summary>
        /// Stops the network
        /// </summary>
        public void StopNetwork(string message = null) 
        {
            if(IsServerActive)
                networkManager.ServerManager.StopConnection(true);
            
            if(IsClientActive)
                networkManager.ClientManager.StopConnection();

            if(message != null)
                Debug.Log("Stop message: " + message);
        }
#endregion

#region Events
        private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
        {
            serverConnectionState = args.ConnectionState;
            if(serverConnectionState != LocalConnectionState.Stopped) {
                serverStatusText.gameObject.SetActive(true);
                serverStatusText.text = "Server: " + serverConnectionState;
            } else {
                serverStatusText.gameObject.SetActive(false);
            }
        }

        private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
        {
            clientConnectionState = args.ConnectionState;
            if(clientConnectionState != LocalConnectionState.Stopped) {
                clientStatusText.gameObject.SetActive(true);
                clientStatusText.text = "Client: " + clientConnectionState;
            } else {
                clientStatusText.gameObject.SetActive(false);
            }
        }
#endregion

    }
}