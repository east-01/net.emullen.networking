using FishNet.Transporting;
using UnityEngine;

namespace EMullen.Networking 
{
    [CreateAssetMenu(fileName = "NetworkConfig", menuName = "Networking/Create New Network Config")]
    public class NetworkConfiguration : ScriptableObject 
    {
        /// <summary>
        /// Used by the NetworkController general NetworkConfiguration storage to identify this
        ///   config instance.
        /// </summary>
        public string tag;

        public bool isServer;
        public bool isClient;

        public string serverBindAddress;
        public IPAddressType ipAddressType;
        public string clientAddress;
        public ushort port;
    }
}