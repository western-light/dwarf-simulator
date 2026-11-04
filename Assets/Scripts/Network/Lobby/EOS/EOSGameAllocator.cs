using System.Threading.Tasks;
using PurrNet;
using PurrNet.Lobby;
using PurrNet.Logging;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Game allocator for EOS peer-to-peer sessions: the lobby owner hosts, and clients
    /// connect to the owner's ProductUserId through the EOS transport (EOS relays when
    /// a direct connection is not possible).
    /// </summary>
    [ProviderDependency("com.playeveryware.eos", "EOS Plugin for Unity")]
    [CreateAssetMenu(menuName = "PurrLobby/EOS/Game Allocator", fileName = "EOS Game Allocator", order = -204)]
    public class EOSGameAllocator : GameAllocatorProvider
    {
        [SerializeField, PurrScene] private string _gameScene;

        public override Task LoadGame(ILobby lobby)
        {
            return LoadGameScene(_gameScene);
        }

        public override Task<GameStartResponse> AllocateGame(ILobby lobby)
        {
            if (!EOSLobbyApi.isReady)
                return Task.FromResult(GameStartResponse.Failure(EOSLobbyApi.NotLoggedIn));

            var hostId = lobby?.owner?.id;
            if (string.IsNullOrEmpty(hostId))
                return Task.FromResult(GameStartResponse.Failure("The lobby has no owner to host the game."));

            return Task.FromResult(GameStartResponse.Success(new ConnectionInfo
            {
                serverAddress = hostId,
                hostId = hostId,
            }));
        }

        protected override bool ConfigureTransport(NetworkManager manager, ConnectionInfo connection, bool asHost)
        {
            if (string.IsNullOrEmpty(connection.serverAddress))
            {
                PurrLogger.LogError("Connection info has no host ProductUserId.");
                return false;
            }

            var transport = manager.transport as PurrNet.EOSTransport.EOSTransport
                            ?? GetOrAddComponent<PurrNet.EOSTransport.EOSTransport>(manager.gameObject);
            manager.transport = transport;

            transport.remoteProductUserId = connection.serverAddress;
            return true;
        }
    }
}
