using FishNet.Transporting;
using UnityEngine;

namespace EMullen.Networking 
{
    [CreateAssetMenu(fileName = "NetworkConfig", menuName = "Networking/Create New Network Config")]
    public class NetworkConfiguration : ScriptableObject 
    {
        public bool isServer;
        public bool isClient;

        public Transport transport;

        public string serverBindAddress;
        public IPAddressType ipAddressType;
        public string clientAddress;
        public ushort port;
    }
}