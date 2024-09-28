using UnityEngine;

namespace EMullen.Networking.Lobby 
{
    public abstract class LobbyState 
    {
    
        private float initTime;
        public float TimeInState => Time.time-initTime;

        public LobbyState() 
        {
            initTime = Time.time;
        }

        public virtual void Update() {}
        /// <summary>
        /// Check for a condition that means we need to move to a new state.
        /// </summary>
        /// <returns>The lobby state that we'll be transitioning to.</returns>
        public abstract LobbyState CheckForStateChange();
        public abstract string GetID();

    }
}