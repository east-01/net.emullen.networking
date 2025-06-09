using System;
using EMullen.Core;
using FishNet.Managing;
using FishNet.Transporting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EMullen.Networking 
{
    [DefaultExecutionOrder(1)]
    public class NetworkConfigurator : MenuController.MenuController 
    {
        
        [SerializeField]
        private TMP_Text titleText;
        [SerializeField]
        private float statusShowTime = 5;
        [SerializeField]
        private TMP_Text statusText;
        [SerializeField]
        private TMP_InputField address;
        [SerializeField]
        private TMP_InputField port;
        [SerializeField]
        private Toggle isServerToggle;
        [SerializeField]
        private Toggle isClientToggle;

        private float statusClearTime;

        protected override void Opened() 
        {
            base.Opened();
            address.text = "localhost";
            port.text = "7000";
            isServerToggle.isOn = true;
            isClientToggle.isOn = true;
        }

        private void Update() 
        {
            if(Time.time > statusClearTime)
                statusText.text = "";
        }

#region UI Element Callbacks
        /// <summary>
        /// Callback from submit button/password onSubmit to submit the username and password
        /// </summary>
        public void Submit() 
        {   
            ushort port = 0;
            if(!ushort.TryParse(this.port.text, out port)) {
                ShowStatusText($"Invalid port {this.port.text}", true);
                return;
            }

            NetworkController.Instance.NetworkConfig = new(
                "user_generated",
                isServerToggle.isOn,
                isClientToggle.isOn,
                isServerToggle.isOn ? "0.0.0.0" : "",
                IPAddressType.IPv4,
                address.text,
                port
            );

            Close();
        }        
#endregion

#region UI Controls
        public void SetStatusText(string message, bool isError = false) 
        {
            statusText.color = isError ? Color.red : Color.green;
            statusText.text = message;
        }

        public void ShowStatusText(string message, bool isError = false) 
        {
            SetStatusText(message, isError);
            statusClearTime = Time.time + statusShowTime;
        }
#endregion

    }
}