using Steamworks;
using UnityEngine;

namespace Mirror.Steamworks
{
    public class SteamServerManager
    {
        public static SteamServerManager instance;

        public void Initialize(ushort gamePort, ushort steamPort, ushort queryPort, string serverName, int maxPlayers)
        {
            try
            {
                System.IO.File.WriteAllText("steam_appid.txt", "923440");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Couldn't write steam_appid.txt: " + e.Message);
            }

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
                Debug.LogWarning(e.Message);
            }
            SteamServer.ServerName = serverName;
            SteamServer.MaxPlayers = maxPlayers;
            SteamServer.Passworded = false;
            SteamServer.DedicatedServer = true;

            SteamServer.OnSteamServersConnected += OnSteamServersConnected;
            SteamServer.OnValidateAuthTicketResponse += OnValidateAuthTicketResponse;
            SteamServer.LogOnAnonymous();
        }

        private void OnSteamServersConnected()
        {
            NetworkManager.singleton.SetupServerContinue();
        }

        public void ValidatePlayer(ulong steamId, byte[] data)
        {
            Debug.Log(SteamServer.BeginAuthSession(data, steamId));
        }

        public void OnValidateAuthTicketResponse(SteamId steamID, SteamId ownerId, AuthResponse status)
        {

        }
    }
}
