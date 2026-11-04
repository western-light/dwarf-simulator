using PurrNet.Lobby;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>A lobby member. Its id is the EOS ProductUserId string.</summary>
    public class EOSPlayer : LobbyPlayerBase
    {
        public override IMetadata userData => _metadata;

        private readonly EOSMetadata _metadata;

        internal EOSPlayer(EOSLobby lobby, string productUserId, string displayName, bool isOwner, bool isLocal)
            : base(productUserId, displayName, isOwner)
        {
            _metadata = new EOSMetadata(lobby, isPlayerMetadata: true, isLocalPlayer: isLocal);
        }

        internal EOSMetadata GetMetadata() => _metadata;

        internal void SetDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == displayName)
                return;

            displayName = name;
            NotifyUpdated();
        }
    }
}
