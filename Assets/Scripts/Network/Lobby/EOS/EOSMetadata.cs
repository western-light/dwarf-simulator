using PurrNet.Lobby;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Metadata backed by EOS lobby attributes. Lobby metadata maps to lobby attributes
    /// (owner-only); player metadata maps to the local member's attributes. Writes are
    /// batched by <see cref="EOSLobby"/> into a single UpdateLobby call per frame.
    /// </summary>
    public class EOSMetadata : LobbyMetadataBase
    {
        private readonly EOSLobby _lobby;
        private readonly bool _isPlayerMetadata;
        private readonly bool _isLocalPlayer;

        internal EOSMetadata(EOSLobby lobby, bool isPlayerMetadata, bool isLocalPlayer)
        {
            _lobby = lobby;
            _isPlayerMetadata = isPlayerMetadata;
            _isLocalPlayer = isLocalPlayer;
        }

        protected override bool CanWrite => _isPlayerMetadata ? _isLocalPlayer : _lobby.isOwner;

        protected override void PushChange(string key, string value)
        {
            if (_isPlayerMetadata)
                _lobby.QueueMemberWrite(key, value);
            else
                _lobby.QueueLobbyWrite(key, value);
        }
    }
}
