using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Agoxandr.Utils;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Mirror.Steamworks
{
    public class SteamTransport : Transport
    {
        [Header("Relay Socket")]
        public bool useRelay = false;
        public ulong targetSteamId;
        [Header("Normal Socket")]
        public ushort port = 7777;
        [Header("Auth Server")]
        public ushort steamPort = 7778;
        public ushort queryPort = 7779;
        public string serverName = "Test server";
        internal bool isServer;
        private bool serverActive;
        public static SteamTransport instance;
        public ServerManager Server { get; private set; }
        public ClientManager Client { get; private set; }

        private static readonly ILogger logger = LogFactory.GetLogger(typeof(SteamTransport));

        private void Awake()
        {
            instance = this;
        }

        public class NetworkPlayer
        {
            public Connection connection;
            public ConnectionInfo info;

            public NetworkPlayer(Connection connection, ConnectionInfo info, int index)
            {
                connection.UserData = index;
                this.connection = connection;
                this.info = info;
            }
        }

        public class ServerManager : SocketManager
        {
            public Dictionary<int, NetworkPlayer> clients = new Dictionary<int, NetworkPlayer>();
            private int index = 0;

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                base.OnConnected(connection, info);
                clients.Add(++index, new NetworkPlayer(connection, info, index));
                instance.OnServerConnected.Invoke(index);
                if (Connected.Count == 1)
                {
                    EventManager.OnUpdated += OnUpdated;
                }
            }

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                base.OnConnecting(connection, info);
            }

            public override void OnConnectionChanged(Connection connection, ConnectionInfo info)
            {
                base.OnConnectionChanged(connection, info);
                if (logger.LogEnabled()) logger.Log($"Server {Socket} {connection} {info.State}");
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                if (Connected.Count == 1)
                {
                    EventManager.OnUpdated -= OnUpdated;
                }
                instance.OnServerDisconnected.Invoke((int)connection.UserData);
                base.OnDisconnected(connection, info);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                byte[] buffer = new byte[size];
                Marshal.Copy(data, buffer, 0, size);
                instance.OnServerDataReceived.Invoke((int)connection.UserData, new ArraySegment<byte>(buffer), channel);
            }

            private void OnUpdated()
            {
                Receive();
            }
        }

        public class ClientManager : ConnectionManager
        {
            public override void OnConnected(ConnectionInfo info)
            {
                base.OnConnected(info);
                instance.OnClientConnected.Invoke();
                EventManager.OnUpdated += OnUpdated;
            }

            public override void OnConnecting(ConnectionInfo info)
            {
                base.OnConnecting(info);
            }

            public override void OnConnectionChanged(ConnectionInfo info)
            {
                base.OnConnectionChanged(info);
                if (logger.LogEnabled()) logger.Log($"Client {Connection} {info.State}");
            }

            public override void OnDisconnected(ConnectionInfo data)
            {
                EventManager.OnUpdated -= OnUpdated;
                instance.OnClientDisconnected.Invoke();
                base.OnDisconnected(data);
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                byte[] buffer = new byte[size];
                Marshal.Copy(data, buffer, 0, size);
                instance.OnClientDataReceived.Invoke(new ArraySegment<byte>(buffer), channel);
            }

            private void OnUpdated()
            {
                Receive();
            }
        }

        public override bool Available()
        {
            if (isServer)
            {
                return SteamServer.IsValid;
            }
            else
            {
                return SteamClient.IsLoggedOn;
            }
        }

        public override void ClientConnect(string address)
        {
            if (useRelay) Client = SteamNetworkingSockets.ConnectRelay<ClientManager>(targetSteamId);
            else Client = SteamNetworkingSockets.ConnectNormal<ClientManager>(NetAddress.From(address, port));
        }

        public override bool ClientConnected()
        {
            return Client.Connected;
        }

        public override void ClientDisconnect()
        {
            if (Client != null)
            {
                Client.Connection.Flush();
                Client.Connection.Close();
            }
        }

        public override bool ClientSend(int channelId, ArraySegment<byte> segment)
        {
            Client.Connection.SendMessage(segment.Array, segment.Offset, segment.Count, (SendType)channelId);
            return true;
        }

        public override int GetMaxPacketSize(int channelId = 8)
        {
            switch (channelId)
            {
                case (int)SendType.Unreliable:
                case (int)SendType.NoDelay:
                    return 1200;
                case (int)SendType.Reliable:
                case (int)SendType.NoNagle:
                    return 1048576;
                default:
                    throw new NotSupportedException();
            }
        }

        public override bool ServerActive()
        {
            return serverActive;
        }

        public override bool ServerDisconnect(int connectionId)
        {
            return Server.clients[connectionId].connection.Close();
        }

        public override string ServerGetClientAddress(int connectionId)
        {
            return Server.clients[connectionId].connection.ConnectionName;
        }

        public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment)
        {
            for (int i = 0; i < connectionIds.Count; i++)
            {
                Server.clients[connectionIds[i]].connection.SendMessage(segment.Array, segment.Offset, segment.Count, (SendType)channelId);
            }
            return true;
        }

        public void ServerLogOn()
        {
            isServer = true;
            Initialize(port, steamPort, queryPort, serverName, NetworkManager.singleton.maxConnections);
        }

        private void Initialize(ushort gamePort, ushort steamPort, ushort queryPort, string serverName, int maxPlayers)
        {
            SteamServerInit init = new SteamServerInit("Conquest", "Conquest")
            {
                GamePort = gamePort,
                SteamPort = steamPort,
                QueryPort = queryPort,
                VersionString = "1.0",
                Secure = true
            };

            try
            {
                SteamServer.Init(923440, init);
            }
            catch (System.Exception e)
            {
                logger.LogWarning(e.Message);

            }
            SteamServer.ServerName = serverName;
            SteamServer.MaxPlayers = maxPlayers;
            SteamServer.Passworded = false;
            SteamServer.DedicatedServer = true;
            SteamServer.LogOnAnonymous();
            if (logger.LogEnabled()) logger.Log("Logged on as Anonymous");
        }

        public override void ServerStart()
        {
            if (useRelay) Server = SteamNetworkingSockets.CreateRelaySocket<ServerManager>();
            else Server = SteamNetworkingSockets.CreateNormalSocket<ServerManager>(NetAddress.AnyIp(port));
            serverActive = true;
        }

        public override void ServerStop()
        {
            if (Server != null)
            {
                for (int i = 0; i < Server.Connected.Count; i++)
                {
                    Server.Connected[i].Flush();
                    Server.Connected[i].Close();
                }
                Server.Close();
                SteamServer.Shutdown();
                Server = null;
            }
        }

        public override Uri ServerUri()
        {
            throw new NotImplementedException();
        }

        public override void Shutdown()
        {
            if (isServer)
            {
                if (serverActive)
                {
                    ServerStop();
                }
            }
            else
            {
                if (Client != null)
                {
                    if (Client.Connected)
                    {
                        ClientDisconnect();
                    }
                }
            }
        }
    }
}
