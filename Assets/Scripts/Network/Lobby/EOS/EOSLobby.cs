using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PurrNet.Lobby;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Wraps an EOS lobby as an <see cref="ILobby"/>. EOS owns the roster and migrates
    /// ownership when the owner leaves; every EOS notification for this lobby triggers a
    /// resync from the local LobbyDetails snapshot, which is diffed into the shared lobby
    /// events. Lobby metadata rides lobby attributes, player metadata rides member attributes.
    /// </summary>
    public class EOSLobby : LobbyBase<EOSPlayer>, System.IDisposable
    {
        public override string id => _lobbyId;
        public override int maxPlayers => _maxPlayers;
        public override IMetadata lobbyData => _lobbyMetadata;
        public override bool isLobbyJoinable => _joinable;
        public override ILobbyChat chat => _chat;

        private readonly string _lobbyId;
        private readonly ProductUserId _localUserId;
        private readonly string _localId;
        private readonly string _localDisplayName;
        private readonly EOSMetadata _lobbyMetadata;
        private readonly EOSChat _chat;

        private readonly ulong _memberStatusHandle;
        private readonly ulong _lobbyUpdateHandle;
        private readonly ulong _memberUpdateHandle;

        private int _maxPlayers;
        private bool _joinable = true;
        private bool _disposed;

        // Attribute writes waiting for the next UpdateLobby, and the batch currently in flight.
        // Both are overlaid on resyncs so a local write is not reverted before EOS confirms it.
        private readonly Dictionary<string, string> _queuedLobbyWrites = new();
        private readonly Dictionary<string, string> _queuedMemberWrites = new();
        private Dictionary<string, string> _inflightLobbyWrites;
        private Dictionary<string, string> _inflightMemberWrites;
        private bool _flushScheduled;

        internal EOSLobby(string lobbyId, ProductUserId localUserId, string localDisplayName)
        {
            _lobbyId = lobbyId;
            _localUserId = localUserId;
            _localId = localUserId.ToString();
            _localDisplayName = string.IsNullOrEmpty(localDisplayName) ? ShortId(_localId) : localDisplayName;

            _lobbyMetadata = new EOSMetadata(this, isPlayerMetadata: false, isLocalPlayer: false);
            _chat = new EOSChat(this);

            var lobbies = EOSLobbyApi.lobbies;

            var statusOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
            _memberStatusHandle = lobbies.AddNotifyLobbyMemberStatusReceived(ref statusOptions, null,
                (ref LobbyMemberStatusReceivedCallbackInfo info) =>
                    OnMemberStatus(info.LobbyId, info.TargetUserId, info.CurrentStatus));

            var updateOptions = new AddNotifyLobbyUpdateReceivedOptions();
            _lobbyUpdateHandle = lobbies.AddNotifyLobbyUpdateReceived(ref updateOptions, null,
                (ref LobbyUpdateReceivedCallbackInfo info) => OnLobbyChanged(info.LobbyId));

            var memberOptions = new AddNotifyLobbyMemberUpdateReceivedOptions();
            _memberUpdateHandle = lobbies.AddNotifyLobbyMemberUpdateReceived(ref memberOptions, null,
                (ref LobbyMemberUpdateReceivedCallbackInfo info) => OnLobbyChanged(info.LobbyId));

            Resync();

            // EOS Connect has no shared profile name, so each member publishes their own.
            QueueMemberWrite(EOSLobbyKeys.DisplayName, _localDisplayName);
        }

        public override void KickPlayer(IPlayer player)
        {
            if (player == null)
                return;

            if (!isOwner)
            {
                Debug.LogWarning("[EOSLobby] Only the owner can kick players.");
                return;
            }

            var target = ProductUserId.FromString(player.id);
            LogFailure(EOSLobbyApi.KickMember(_lobbyId, _localUserId, target), "kick player");
        }

        public override void SetIsLobbyJoinable(bool isJoinable)
        {
            if (!isOwner)
            {
                Debug.LogWarning("[EOSLobby] Only the owner can change joinability.");
                return;
            }

            if (_joinable == isJoinable)
                return;

            _joinable = isJoinable;
            QueueLobbyWrite(EOSLobbyKeys.Joinable, isJoinable ? "1" : "0");
        }

        public override void LeaveLobby()
        {
            if (_disposed)
                return;

            LogFailure(EOSLobbyApi.LeaveLobby(_lobbyId, _localUserId), "leave lobby");
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            var lobbies = EOSLobbyApi.lobbies;
            if (lobbies == null)
                return;

            lobbies.RemoveNotifyLobbyMemberStatusReceived(_memberStatusHandle);
            lobbies.RemoveNotifyLobbyUpdateReceived(_lobbyUpdateHandle);
            lobbies.RemoveNotifyLobbyMemberUpdateReceived(_memberUpdateHandle);
        }

        internal void QueueLobbyWrite(string key, string value)
        {
            _queuedLobbyWrites[key] = value;
            ScheduleFlush();
        }

        internal void QueueMemberWrite(string key, string value)
        {
            _queuedMemberWrites[key] = value;
            ScheduleFlush();
        }

        private void ScheduleFlush()
        {
            if (_flushScheduled || _disposed)
                return;

            _flushScheduled = true;
            FlushAsync().Forget("[EOSLobby] Failed to update lobby attributes");
        }

        private async Task FlushAsync()
        {
            try
            {
                // Wait a frame so several SetData calls in a row go out as one UpdateLobby.
                await Task.Yield();

                while (!_disposed && (_queuedLobbyWrites.Count > 0 || _queuedMemberWrites.Count > 0))
                {
                    _inflightLobbyWrites = new Dictionary<string, string>(_queuedLobbyWrites);
                    _inflightMemberWrites = new Dictionary<string, string>(_queuedMemberWrites);
                    _queuedLobbyWrites.Clear();
                    _queuedMemberWrites.Clear();

                    var lobbyWrites = _inflightLobbyWrites;
                    var memberWrites = _inflightMemberWrites;

                    var result = await EOSLobbyApi.UpdateLobby(_lobbyId, _localUserId, modification =>
                    {
                        var lobbyResult = EOSLobbyApi.ApplyMetadataWrites(lobbyWrites,
                            (key, value) => EOSLobbyApi.SetStringAttribute(modification, key, value));
                        if (lobbyResult != Result.Success)
                            return lobbyResult;

                        return EOSLobbyApi.ApplyMetadataWrites(memberWrites,
                            (key, value) => EOSLobbyApi.SetStringMemberAttribute(modification, key, value));
                    });

                    if (result != Result.Success)
                        Debug.LogWarning($"[EOSLobby] Updating lobby attributes failed: {result}");
                }
            }
            finally
            {
                _inflightLobbyWrites = null;
                _inflightMemberWrites = null;
                _flushScheduled = false;
            }

            // Nothing is pending any more: settle on EOS's authoritative state.
            Resync();
        }

        private void OnLobbyChanged(string lobbyId)
        {
            if (_disposed || lobbyId != _lobbyId)
                return;

            Resync();
        }

        private void OnMemberStatus(string lobbyId, ProductUserId target, LobbyMemberStatus status)
        {
            if (_disposed || lobbyId != _lobbyId)
                return;

            var targetId = target?.ToString();
            bool removed = status == LobbyMemberStatus.Left || status == LobbyMemberStatus.Disconnected ||
                           status == LobbyMemberStatus.Kicked;

            if (status == LobbyMemberStatus.Closed || (removed && targetId == _localId))
            {
                // We are no longer a member (kicked, lobby closed or connection lost).
                if (localPlayerInternal != null)
                    RemovePlayer(_localId, out _);
                RaiseLobbyDestroyed();
                Dispose();
                return;
            }

            if (removed && targetId != null)
            {
                _chat.Forget(targetId);
                RemovePlayer(targetId, out _);
            }

            Resync();
        }

        private void Resync()
        {
            if (_disposed)
                return;

            if (!EOSLobbyApi.TryCopyDetails(_lobbyId, _localUserId, out var details))
                return;

            try
            {
                var infoOptions = new LobbyDetailsCopyInfoOptions();
                if (details.CopyInfo(ref infoOptions, out var info) == Result.Success && info.HasValue)
                    _maxPlayers = (int)info.Value.MaxMembers;

                var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
                var ownerId = details.GetLobbyOwner(ref ownerOptions)?.ToString();

                SyncRoster(details, ownerId);

                var attributes = EOSLobbyApi.ReadLobbyAttributes(details);
                if (_inflightLobbyWrites == null && !_queuedLobbyWrites.ContainsKey(EOSLobbyKeys.Joinable))
                    _joinable = EOSLobbyApi.GetReserved(attributes, EOSLobbyKeys.Joinable) != "0";
                _lobbyMetadata.ReplaceFrom(WithPendingWrites(EOSLobbyApi.ExtractMetadata(attributes),
                    _inflightLobbyWrites, _queuedLobbyWrites));

                TryGetPlayerInternal(ownerId, out var owner);
                if (owner != ownerInternal)
                    SetOwner(owner);
            }
            finally
            {
                details.Release();
            }
        }

        private void SyncRoster(LobbyDetails details, string ownerId)
        {
            var countOptions = new LobbyDetailsGetMemberCountOptions();
            uint count = details.GetMemberCount(ref countOptions);
            var present = new HashSet<string>();

            for (uint i = 0; i < count; i++)
            {
                var memberOptions = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                var memberId = details.GetMemberByIndex(ref memberOptions);
                if (memberId == null)
                    continue;

                var id = memberId.ToString();
                present.Add(id);

                var attributes = EOSLobbyApi.ReadMemberAttributes(details, memberId);
                if (!TryGetPlayerInternal(id, out var player))
                {
                    bool isLocal = id == _localId;
                    var displayName = isLocal ? _localDisplayName
                        : EOSLobbyApi.GetReserved(attributes, EOSLobbyKeys.DisplayName) ?? ShortId(id);
                    player = new EOSPlayer(this, id, displayName, isOwner: id == ownerId, isLocal: isLocal);
                    ApplyMemberAttributes(player, attributes);
                    AddPlayer(player, isLocal);
                }
                else
                {
                    ApplyMemberAttributes(player, attributes);
                }
            }

            for (int i = playerList.Count - 1; i >= 0; i--)
            {
                var id = playerList[i].id;
                if (present.Contains(id))
                    continue;

                _chat.Forget(id);
                RemovePlayer(id, out _);
            }
        }

        private void ApplyMemberAttributes(EOSPlayer player, Dictionary<string, string> attributes)
        {
            var metadata = EOSLobbyApi.ExtractMetadata(attributes);

            if (player.id == _localId)
            {
                metadata = WithPendingWrites(metadata, _inflightMemberWrites, _queuedMemberWrites);
            }
            else
            {
                player.SetDisplayName(EOSLobbyApi.GetReserved(attributes, EOSLobbyKeys.DisplayName));
                _chat.OnChatAttribute(player, EOSLobbyApi.GetReserved(attributes, EOSLobbyKeys.Chat));
            }

            player.GetMetadata().ReplaceFrom(metadata);
        }

        private static Dictionary<string, string> WithPendingWrites(Dictionary<string, string> confirmed,
            Dictionary<string, string> inflight, Dictionary<string, string> queued)
        {
            Overlay(confirmed, inflight);
            Overlay(confirmed, queued);
            return confirmed;
        }

        private static void Overlay(Dictionary<string, string> target, Dictionary<string, string> writes)
        {
            if (writes == null)
                return;

            foreach (var kvp in writes)
            {
                if (IsReserved(kvp.Key))
                    continue;

                if (kvp.Value == null)
                    target.Remove(kvp.Key);
                else
                    target[kvp.Key] = kvp.Value;
            }
        }

        private static bool IsReserved(string key) =>
            key == EOSLobbyKeys.Name || key == EOSLobbyKeys.Joinable ||
            key == EOSLobbyKeys.DisplayName || key == EOSLobbyKeys.Chat;

        private static string ShortId(string productUserId) =>
            productUserId.Length > 8 ? $"Player {productUserId.Substring(productUserId.Length - 6)}" : productUserId;

        private static void LogFailure(Task<Result> operation, string action)
        {
            operation.ContinueWith(t =>
            {
                if (t.Result != Result.Success)
                    Debug.LogWarning($"[EOSLobby] Failed to {action}: {t.Result}");
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }
}
