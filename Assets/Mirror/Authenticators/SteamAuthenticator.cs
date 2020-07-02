using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mirror.Steamworks
{
    public class SteamAuthenticator : NetworkAuthenticator
    {
        public AuthTicket ticket;
        private static readonly ILogger logger = LogFactory.GetLogger(typeof(SteamAuthenticator));

        #region Messages

        public class AuthRequestMessage : MessageBase
        {
            public ulong steamId;
            public byte[] ticket;
        }

        public class AuthResponseMessage : MessageBase
        {
            public int status;
        }

        #endregion

        #region Server

        /// <summary>
        /// Called on server from StartServer to initialize the Authenticator
        /// <para>Server message handlers should be registered in this method.</para>
        /// </summary>
        public override void OnStartServer()
        {
            // register a handler for the authentication request we expect from client
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
            SteamServer.OnValidateAuthTicketResponse += OnValidateAuthTicketResponse;
        }

        /// <summary>
        /// Called on server from OnServerAuthenticateInternal when a client needs to authenticate
        /// </summary>
        /// <param name="conn">Connection to client.</param>
        public override void OnServerAuthenticate(NetworkConnection conn) { }

        public void OnAuthRequestMessage(NetworkConnection conn, AuthRequestMessage msg)
        {
            if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Begin Authentication Session: {0}", msg.steamId);
            SteamServer.BeginAuthSession(msg.ticket, msg.steamId);
        }

        private void OnValidateAuthTicketResponse(SteamId steamId, SteamId ownerId, AuthResponse status)
        {
            foreach (KeyValuePair<int, SteamTransport.NetworkPlayer> client in SteamTransport.instance.Server.clients)
            {
                if (client.Value.info.Identity.SteamId == steamId)
                {
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication Request: {0} {1}", steamId, status);
                    NetworkConnection conn = NetworkServer.connections[client.Key];
                    if (status == AuthResponse.OK)
                    {
                        AuthResponseMessage authResponseMessage = new AuthResponseMessage();
                        conn.Send(authResponseMessage);
                        // Invoke the event to complete a successful authentication
                        OnServerAuthenticated.Invoke(conn);
                        return;
                    }
                    else
                    {
                        AuthResponseMessage authResponseMessage = new AuthResponseMessage();
                        conn.Send(authResponseMessage);
                        // must set NetworkConnection isAuthenticated = false
                        conn.isAuthenticated = false;
                        // disconnect the client after 1 second so that response message gets delivered
                        StartCoroutine(DelayedDisconnect(conn, 1));
                        return;
                    }
                }
            }
        }

        public IEnumerator DelayedDisconnect(NetworkConnection conn, float waitTime)
        {
            yield return new WaitForSeconds(waitTime);
            conn.Disconnect();
        }

        #endregion

        #region Client

        /// <summary>
        /// Called on client from StartClient to initialize the Authenticator
        /// <para>Client message handlers should be registered in this method.</para>
        /// </summary>
        public override void OnStartClient()
        {
            // register a handler for the authentication response we expect from server
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
        }

        /// <summary>
        /// Called on client from OnClientAuthenticateInternal when a client needs to authenticate
        /// </summary>
        /// <param name="conn">Connection of the client.</param>
        public override async void OnClientAuthenticate(NetworkConnection conn)
        {
            ticket = await SteamUser.GetAuthSessionTicketAsync();
            AuthRequestMessage authRequestMessage = new AuthRequestMessage()
            {
                steamId = SteamClient.SteamId.Value,
                ticket = ticket.Data
            };
            NetworkClient.Send(authRequestMessage);
        }

        public void OnAuthResponseMessage(NetworkConnection conn, AuthResponseMessage msg)
        {
            AuthResponse authResponse = (AuthResponse)msg.status;
            if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication Response: {0}", authResponse.ToString());
            if (authResponse == AuthResponse.OK)
            {
                // Invoke the event to complete a successful authentication
                OnClientAuthenticated.Invoke(conn);
            }
            else
            {
                conn.isAuthenticated = false;
                conn.Disconnect();
            }
        }

        #endregion
    }
}
