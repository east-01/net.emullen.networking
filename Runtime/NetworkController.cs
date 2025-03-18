using System;
using System.Collections.Generic;
using EMullen.Core;
using EMullen.PlayerMgmt;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
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
        public NetworkConfiguration NetworkConfig { 
            get { return networkConfig; }
            set { networkConfig = value;}
        }

        [SerializeField]
        private List<NetworkConfiguration> networkConfigList;
        
        [Space]

        [SerializeField] 
        private LocalConnectionState serverConnectionState;
        public LocalConnectionState ServerConnectionState => serverConnectionState;
        [SerializeField] 
        private LocalConnectionState clientConnectionState;
        public LocalConnectionState ClientConnectionState => clientConnectionState;

        [Space]
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
        // public bool IsServerActive => (ServerConnectionState & LocalConnectionState.Stopped) != LocalConnectionState.Stopped;
        public bool IsServerActive => ServerConnectionState != 0;
        // / <summary>
        /// Checks if the servers's status is LocalConnectionState.Started
        /// </summary>
        public bool IsServerStarted => ServerConnectionState == LocalConnectionState.Started;

        /// <summary>
        /// The least-restrictive of connection statuses, returns true if the client's status is
        ///   anything but LocalConnectionState.Stopped
        /// </summary>
        public bool IsClientActive => ClientConnectionState != 0;
        /// <summary>
        /// Checks if the client's status is LocalConnectionState.Started
        /// </summary>
        public bool IsClientStarted => ClientConnectionState == LocalConnectionState.Started;
        /// <summary>
        /// Checks if the client is stated and we have a local connection to refer to.
        /// </summary>
        public bool IsClientConnected => InstanceFinder.IsClientStarted && LocalConnection != null /* TODO: This needs to be if our local connection exists */;

        public NetworkConnection LocalConnection => InstanceFinder.ClientManager.Connection;
#endregion

#region Events
        public delegate void ConnectedHandler();
        public event ConnectedHandler ConnectedEvent;
        public delegate void DisconnectedHandler(string message = null);
        public event DisconnectedHandler DisconnectedEvent;
#endregion

        private void Awake() 
        {
            if(Instance != null) {
                Debug.LogError($"The NetworkController singleton already exists but another is trying to instantiate. Deleting gameObject \"{gameObject.name}\"");
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start() 
        {
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

            networkManager.TransportManager.Transport.SetServerBindAddress(networkConfig.serverBindAddress, networkConfig.ipAddressType);
            networkManager.TransportManager.Transport.SetClientAddress(networkConfig.clientAddress);
            networkManager.TransportManager.Transport.SetPort(networkConfig.port);

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

        public bool CanStartNetwork() => !IsServerActive && !IsClientActive;

        public bool IsNetworkRunning() 
        {
            if(networkConfig.isServer && !IsServerStarted)
                return false;

            if(networkConfig.isClient && !IsClientConnected)
                return false;

            return true;
        }
#endregion

#region Events
        private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
        {
            serverConnectionState = args.ConnectionState;
            if(serverStatusText != null) {
                if(serverConnectionState != LocalConnectionState.Stopped) {
                    serverStatusText.gameObject.SetActive(true);
                    serverStatusText.text = "Server: " + serverConnectionState;
                } else {
                    serverStatusText.gameObject.SetActive(false);
                }
            }
        }

        private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
        {
            clientConnectionState = args.ConnectionState;
            if(clientStatusText != null) {
                if(clientConnectionState != LocalConnectionState.Stopped) {
                    clientStatusText.gameObject.SetActive(true);
                    clientStatusText.text = "Client: " + clientConnectionState;
                } else {
                    clientStatusText.gameObject.SetActive(false);
                }
            }
        }
#endregion

#region NetworkConfiguration Repository
        /// <summary>
        /// Retrieve a NetworkConfiguration object by its tag from the networkConfigList on the
        ///   NetworkController singleton.
        /// </summary>
        /// <param name="tag">The tag string in the target NetworkConfiguration</param>
        /// <returns>The target NetworkConfiguration if it's in the networkConfigList</returns>
        public NetworkConfiguration GetNetworkConfiguration(string tag) 
        {
            foreach(NetworkConfiguration config in networkConfigList) {
                if(config.tag == tag)
                    return config;
            }
            Debug.LogError($"NetworkConfiguration with tag \"{tag}\" not in NetworkController#networkConfigList");
            return null;
        }
#endregion

    }

    public static class ExtensionMethods
    {
        public static NetworkConnection GetNetworkConnection(this NetworkManager networkManager, int connectionId)
        {
            if(!InstanceFinder.IsServerStarted) {
                Debug.LogError("GetNetworkConnection can only be used on server instances.");
                return null;
            }
            if (networkManager == null) {
                Debug.LogError("NetworkManager is not initialized.");
                return null;
            }

            // Local connection case
            if(connectionId == -1) {
                if(InstanceFinder.IsClientStarted) {
                    return networkManager.ClientManager.Connection;
                } else {
                    Debug.LogError("Can't get NetworkConnection for connection ID -1, the client isn't started.");
                    return null;
                }
            }

            // Search for connection in server manager's clients dictionary
            if (networkManager.ServerManager.Clients.TryGetValue(connectionId, out NetworkConnection connection)) {
                return connection;
            } else {
                Debug.LogWarning($"No NetworkConnection found for connection ID: {connectionId}");
                return null;
            }
        }

        public static NetworkConnection GetNetworkConnection(this NetworkIdentifierData networkIdentifierData) 
        {
            return InstanceFinder.NetworkManager.GetNetworkConnection(networkIdentifierData.clientID);
        }
    }
}