using PurrNet.Lobby;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Host migration for EOS lobbies: EOS migrates lobby ownership when the owner leaves,
    /// and this re-points the transport at the new owner's ProductUserId before restarting.
    /// </summary>
    public class EOSLobbyConnection : HostMigrationLobbyConnection
    {
        [SerializeField] private PurrNet.EOSTransport.EOSTransport _transport;

        protected override void ConfigureTransportForHost(ILobby lobby, IPlayer host)
        {
            var transport = ResolveTransport();
            if (!transport)
                return;

            transport.remoteProductUserId = host.id;
        }

        private PurrNet.EOSTransport.EOSTransport ResolveTransport()
        {
            if (_transport)
                return _transport;
            if (networkManager && networkManager.transport is PurrNet.EOSTransport.EOSTransport t)
                _transport = t;
            return _transport;
        }
    }
}
