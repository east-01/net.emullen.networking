using UnityEngine;

namespace EMullen.Networking.Lobby 
{
    public abstract class LobbyState 
    {
        
        protected GameLobby gameLobby;
        private float initTime;
        public float TimeInState => Time.time-initTime;

        public LobbyState(GameLobby gameLobby) 
        {
            this.gameLobby = gameLobby;
            this.initTime = Time.time;
        }

        public virtual void Update() {}
        /// <summary>
        /// Check for a condition that means we need to move to a new state.
        /// </summary>
        /// <returns>The lobby state that we'll be transitioning to. Return null if no new state 
        ///   selected.</returns>
        public abstract LobbyState CheckForStateChange();

    }
}