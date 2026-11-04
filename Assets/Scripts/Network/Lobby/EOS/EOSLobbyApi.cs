using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>Attribute keys reserved by the EOS provider. Never collide with encoded metadata keys.</summary>
    internal static class EOSLobbyKeys
    {
        /// <summary>Lobby attribute: display name of the lobby.</summary>
        public const string Name = "PURR_NAME";

        /// <summary>Lobby attribute: "0" once the owner closes the lobby to new players.</summary>
        public const string Joinable = "PURR_JOINABLE";

        /// <summary>Member attribute: the player's display name (EOS Connect has no shared profile name).</summary>
        public const string DisplayName = "PURR_DISPLAY_NAME";

        /// <summary>Member attribute: the player's recent chat messages (EOS lobbies have no chat).</summary>
        public const string Chat = "PURR_CHAT";

        private const string MetadataPrefix = "M_";

        /// <summary>EOS caps attribute keys at 64 characters; hex doubles the length.</summary>
        private const int MaxMetadataKeyBytes = (LobbyInterface.LOBBYMODIFICATION_MAX_ATTRIBUTE_LENGTH - 2) / 2;

        /// <summary>
        /// Metadata keys are stored as uppercase hex so they survive EOS whatever it does to key
        /// casing and punctuation (PurrLobby itself writes keys such as "_game.status").
        /// </summary>
        public static bool TryEncodeMetadataKey(string key, out string attributeKey)
        {
            var bytes = Encoding.UTF8.GetBytes(key);
            if (bytes.Length > MaxMetadataKeyBytes)
            {
                Debug.LogWarning($"[EOSLobby] Metadata key `{key}` is longer than {MaxMetadataKeyBytes} bytes and cannot be stored in an EOS lobby.");
                attributeKey = null;
                return false;
            }

            var sb = new StringBuilder(MetadataPrefix, MetadataPrefix.Length + bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("X2"));
            attributeKey = sb.ToString();
            return true;
        }

        public static bool TryDecodeMetadataKey(string attributeKey, out string key)
        {
            key = null;
            if (attributeKey == null || !attributeKey.StartsWith(MetadataPrefix, System.StringComparison.OrdinalIgnoreCase))
                return false;

            var hex = attributeKey.Substring(MetadataPrefix.Length);
            if (hex.Length == 0 || hex.Length % 2 != 0)
                return false;

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                    return false;
            }

            key = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public static bool IsKey(string attributeKey, string reservedKey) =>
            string.Equals(attributeKey, reservedKey, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Thin async layer over the EOS Lobby interface. EOS reports results through callbacks
    /// fired from <c>EOSManager.Tick</c> on the main thread; these wrappers turn them into
    /// Tasks so the providers can use async/await.
    /// </summary>
    internal static class EOSLobbyApi
    {
        public static LobbyInterface lobbies => EOSManager.Instance.GetEOSLobbyInterface();

        public static ProductUserId localUserId => EOSManager.Instance.GetProductUserId();

        public static bool isReady
        {
            get
            {
                var userId = localUserId;
                return lobbies != null && userId != null && userId.IsValid();
            }
        }

        public const string NotLoggedIn =
            "EOS is not ready: make sure an EOSManager is in the scene and the EOS session provider logged in.";

        public static Task<(Result result, string lobbyId)> CreateLobby(CreateLobbyOptions options)
        {
            var tcs = new TaskCompletionSource<(Result, string)>();
            lobbies.CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) =>
            {
                if (Common.IsOperationComplete(info.ResultCode))
                    tcs.TrySetResult((info.ResultCode, (string)info.LobbyId));
            });
            return tcs.Task;
        }

        public static Task<Result> JoinLobbyById(string lobbyId)
        {
            var tcs = new TaskCompletionSource<Result>();
            var options = new JoinLobbyByIdOptions
            {
                LobbyId = lobbyId,
                LocalUserId = localUserId,
                PresenceEnabled = false,
            };
            lobbies.JoinLobbyById(ref options, null, (ref JoinLobbyByIdCallbackInfo info) =>
            {
                if (Common.IsOperationComplete(info.ResultCode))
                    tcs.TrySetResult(info.ResultCode);
            });
            return tcs.Task;
        }

        public static Task<Result> LeaveLobby(string lobbyId, ProductUserId userId)
        {
            var tcs = new TaskCompletionSource<Result>();
            var options = new LeaveLobbyOptions { LobbyId = lobbyId, LocalUserId = userId };
            lobbies.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo info) =>
            {
                if (Common.IsOperationComplete(info.ResultCode))
                    tcs.TrySetResult(info.ResultCode);
            });
            return tcs.Task;
        }

        public static Task<Result> KickMember(string lobbyId, ProductUserId userId, ProductUserId target)
        {
            var tcs = new TaskCompletionSource<Result>();
            var options = new KickMemberOptions { LobbyId = lobbyId, LocalUserId = userId, TargetUserId = target };
            lobbies.KickMember(ref options, null, (ref KickMemberCallbackInfo info) =>
            {
                if (Common.IsOperationComplete(info.ResultCode))
                    tcs.TrySetResult(info.ResultCode);
            });
            return tcs.Task;
        }

        /// <summary>Builds a modification with <paramref name="edit"/> and sends it in one UpdateLobby call.</summary>
        public static Task<Result> UpdateLobby(string lobbyId, ProductUserId userId, System.Func<LobbyModification, Result> edit)
        {
            var modOptions = new UpdateLobbyModificationOptions { LobbyId = lobbyId, LocalUserId = userId };
            var created = lobbies.UpdateLobbyModification(ref modOptions, out var modification);
            if (created != Result.Success)
                return Task.FromResult(created);

            var edited = edit(modification);
            if (edited != Result.Success)
            {
                modification.Release();
                return Task.FromResult(edited);
            }

            var tcs = new TaskCompletionSource<Result>();
            var options = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            lobbies.UpdateLobby(ref options, null, (ref UpdateLobbyCallbackInfo info) =>
            {
                if (!Common.IsOperationComplete(info.ResultCode))
                    return;
                modification.Release();
                tcs.TrySetResult(info.ResultCode);
            });
            return tcs.Task;
        }

        public static Task<Result> Find(LobbySearch search)
        {
            var tcs = new TaskCompletionSource<Result>();
            var options = new LobbySearchFindOptions { LocalUserId = localUserId };
            search.Find(ref options, null, (ref LobbySearchFindCallbackInfo info) =>
            {
                if (Common.IsOperationComplete(info.ResultCode))
                    tcs.TrySetResult(info.ResultCode);
            });
            return tcs.Task;
        }

        /// <summary>Local snapshot of a lobby the user is in. The caller must Release() it.</summary>
        public static bool TryCopyDetails(string lobbyId, ProductUserId userId, out LobbyDetails details)
        {
            var options = new CopyLobbyDetailsHandleOptions { LobbyId = lobbyId, LocalUserId = userId };
            return lobbies.CopyLobbyDetailsHandle(ref options, out details) == Result.Success && details != null;
        }

        public static Result SetStringAttribute(LobbyModification modification, string key, string value)
        {
            if (value == null)
            {
                var remove = new LobbyModificationRemoveAttributeOptions { Key = key };
                return modification.RemoveAttribute(ref remove);
            }

            var add = new LobbyModificationAddAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = value },
                Visibility = LobbyAttributeVisibility.Public,
            };
            return modification.AddAttribute(ref add);
        }

        public static Result SetStringMemberAttribute(LobbyModification modification, string key, string value)
        {
            if (value == null)
            {
                var remove = new LobbyModificationRemoveMemberAttributeOptions { Key = key };
                return modification.RemoveMemberAttribute(ref remove);
            }

            var add = new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = value },
                Visibility = LobbyAttributeVisibility.Public,
            };
            return modification.AddMemberAttribute(ref add);
        }

        /// <summary>Applies a batch of metadata writes (null value = removal), encoding the keys.</summary>
        public static Result ApplyMetadataWrites(IReadOnlyDictionary<string, string> writes,
            System.Func<string, string, Result> setAttribute)
        {
            foreach (var kvp in writes)
            {
                var attributeKey = kvp.Key;
                if (!IsReservedKey(kvp.Key) && !EOSLobbyKeys.TryEncodeMetadataKey(kvp.Key, out attributeKey))
                    continue;

                var result = setAttribute(attributeKey, kvp.Value);
                if (result != Result.Success)
                    return result;
            }

            return Result.Success;
        }

        public static Dictionary<string, string> ReadLobbyAttributes(LobbyDetails details)
        {
            var result = new Dictionary<string, string>();
            var countOptions = new LobbyDetailsGetAttributeCountOptions();
            uint count = details.GetAttributeCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var options = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = i };
                if (details.CopyAttributeByIndex(ref options, out var attribute) == Result.Success)
                    AddAttribute(result, attribute);
            }

            return result;
        }

        public static Dictionary<string, string> ReadMemberAttributes(LobbyDetails details, ProductUserId member)
        {
            var result = new Dictionary<string, string>();
            var countOptions = new LobbyDetailsGetMemberAttributeCountOptions { TargetUserId = member };
            uint count = details.GetMemberAttributeCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var options = new LobbyDetailsCopyMemberAttributeByIndexOptions { TargetUserId = member, AttrIndex = i };
                if (details.CopyMemberAttributeByIndex(ref options, out var attribute) == Result.Success)
                    AddAttribute(result, attribute);
            }

            return result;
        }

        /// <summary>Splits raw attributes into decoded user metadata, leaving reserved keys out.</summary>
        public static Dictionary<string, string> ExtractMetadata(Dictionary<string, string> attributes)
        {
            var metadata = new Dictionary<string, string>();
            foreach (var kvp in attributes)
            {
                if (EOSLobbyKeys.TryDecodeMetadataKey(kvp.Key, out var key))
                    metadata[key] = kvp.Value;
            }

            return metadata;
        }

        public static string GetReserved(Dictionary<string, string> attributes, string reservedKey)
        {
            foreach (var kvp in attributes)
            {
                if (EOSLobbyKeys.IsKey(kvp.Key, reservedKey))
                    return kvp.Value;
            }

            return null;
        }

        private static bool IsReservedKey(string key) =>
            key == EOSLobbyKeys.Name || key == EOSLobbyKeys.Joinable ||
            key == EOSLobbyKeys.DisplayName || key == EOSLobbyKeys.Chat;

        private static void AddAttribute(Dictionary<string, string> result, Attribute? attribute)
        {
            var data = attribute?.Data;
            if (!data.HasValue)
                return;

            string key = data.Value.Key;
            if (string.IsNullOrEmpty(key))
                return;

            result[key] = ValueToString(data.Value.Value);
        }

        private static string ValueToString(AttributeDataValue value)
        {
            switch (value.ValueType)
            {
                case AttributeType.String: return value.AsUtf8;
                case AttributeType.Int64: return value.AsInt64?.ToString();
                case AttributeType.Double: return value.AsDouble?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case AttributeType.Boolean: return value.AsBool == true ? "1" : "0";
                default: return null;
            }
        }
    }
}
