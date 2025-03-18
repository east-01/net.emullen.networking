using FishNet.Transporting;
using UnityEngine;

namespace EMullen.Networking 
{
    public class NetworkConfiguration
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

        public NetworkConfiguration(string tag, bool isServer, bool isClient, string serverBindAddress, IPAddressType iPAddressType, string clientAddress, ushort port) 
        {
            this.tag = tag;
            this.isServer = isServer;
            this.isClient = isClient;
            this.serverBindAddress = serverBindAddress;
            this.ipAddressType = iPAddressType;
            this.clientAddress = clientAddress;
            this.port = port;
        }
    }
}