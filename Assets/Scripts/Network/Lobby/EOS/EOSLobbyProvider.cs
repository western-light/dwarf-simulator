using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PurrNet.Lobby;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Lobby provider backed by EOS lobbies. The EOS lobby id doubles as the join code.
    /// Public lobbies are advertised to searches in <see cref="_bucketId"/>; private ones are
    /// invite-only and reachable only through their id (JoinLobbyById).
    /// </summary>
    [ProviderDependency("com.playeveryware.eos", "EOS Plugin for Unity")]
    [CreateAssetMenu(menuName = "PurrLobby/EOS/Lobby Provider", fileName = "EOS Lobby Provider", order = -204)]
    public class EOSLobbyProvider : LobbyProvider
    {
        [Tooltip("Default max players for newly created lobbies.")]
        [SerializeField] private int _maxPlayers = 4;

        [Tooltip("Searches only see lobbies with the same bucket. Change it to keep builds or game modes apart.")]
        [SerializeField] private string _bucketId = "dwarf-simulator";

        [Tooltip("Upper bound on lobbies returned by one search (EOS allows up to 200).")]
        [SerializeField, Range(1, LobbyInterface.MAX_SEARCH_RESULTS)] private int _maxSearchResults = 50;

        public override int maxPlayer => _maxPlayers;

        public override LobbyCapabilities capabilities => LobbyCapabilities.All;

        public override async Task<LobbyResponse> CreateLobby(LobbySettings settings)
        {
            if (!EOSLobbyApi.isReady)
                return LobbyResponse.Failure(EOSLobbyApi.NotLoggedIn);

            var maxPlayers = settings.maxPlayers > 0 ? settings.maxPlayers : _maxPlayers;
            var localUserId = EOSLobbyApi.localUserId;

            var options = new CreateLobbyOptions
            {
                LocalUserId = localUserId,
                MaxLobbyMembers = (uint)maxPlayers,
                PermissionLevel = settings.visibility == LobbyVisibility.Public
                    ? LobbyPermissionLevel.Publicadvertised
                    : LobbyPermissionLevel.Inviteonly,
                PresenceEnabled = false,
                AllowInvites = true,
                BucketId = _bucketId,
                DisableHostMigration = false,
                EnableRTCRoom = false,
                // Join codes are lobby ids, so private lobbies must stay joinable by id.
                EnableJoinById = true,
            };

            var (result, lobbyId) = await EOSLobbyApi.CreateLobby(options);
            if (result != Result.Success)
                return LobbyResponse.Failure($"EOS lobby creation failed: {result}");

            // Publish name and initial metadata before handing the lobby out, so the lobby is
            // complete for searches from the start.
            var initialWrites = new Dictionary<string, string>
            {
                [EOSLobbyKeys.Joinable] = "1",
            };
            if (!string.IsNullOrEmpty(settings.name))
                initialWrites[EOSLobbyKeys.Name] = settings.name;
            if (settings.metadata != null)
            {
                foreach (var kvp in settings.metadata)
                    initialWrites[kvp.Key] = kvp.Value;
            }

            var updated = await EOSLobbyApi.UpdateLobby(lobbyId, localUserId, modification =>
                EOSLobbyApi.ApplyMetadataWrites(initialWrites,
                    (key, value) => EOSLobbyApi.SetStringAttribute(modification, key, value)));
            if (updated != Result.Success)
                Debug.LogWarning($"[{name}] Setting the initial lobby attributes failed: {updated}");

            return LobbyResponse.Success(new EOSLobby(lobbyId, localUserId, LocalDisplayName()));
        }

        public override async Task<LobbyResponse> JoinLobby(string lobbyId)
        {
            if (!EOSLobbyApi.isReady)
                return LobbyResponse.Failure(EOSLobbyApi.NotLoggedIn);

            if (string.IsNullOrWhiteSpace(lobbyId))
                return LobbyResponse.Failure("Invalid EOS lobby id.");

            var result = await EOSLobbyApi.JoinLobbyById(lobbyId.Trim());
            if (result != Result.Success)
                return LobbyResponse.Failure(DescribeJoinResult(result));

            var lobby = new EOSLobby(lobbyId.Trim(), EOSLobbyApi.localUserId, LocalDisplayName());

            // Private lobbies can always be joined by id, so the joinable flag is enforced here.
            if (!lobby.isLobbyJoinable)
            {
                lobby.LeaveLobby();
                return LobbyResponse.Failure("The lobby is not accepting new players.");
            }

            return LobbyResponse.Success(lobby);
        }

        public override Task<LobbyResponse> JoinLobbyByCode(string code) => JoinLobby(code);

        public override async Task<LobbyResponse> JoinRandom(LobbyQuery query = null)
        {
            var list = await QueryLobbies(query);

            if (!list.success)
                return LobbyResponse.Failure(list.error);

            if (list.lobbies == null || list.lobbies.Count == 0)
                return LobbyResponse.Failure("No matching lobbies found.");

            // A listed lobby can fill up or close before we get there; try the next one.
            var lastError = "No matching lobbies found.";
            foreach (var info in list.lobbies)
            {
                var joined = await JoinLobby(info.id);
                if (joined.success)
                    return joined;
                lastError = joined.error;
            }

            return LobbyResponse.Failure(lastError);
        }

        public override async Task<LobbyCollectionResponse> QueryLobbies(LobbyQuery query = null)
        {
            if (!EOSLobbyApi.isReady)
                return LobbyCollectionResponse.Failure(EOSLobbyApi.NotLoggedIn);

            var searchOptions = new CreateLobbySearchOptions { MaxResults = (uint)_maxSearchResults };
            var created = EOSLobbyApi.lobbies.CreateLobbySearch(ref searchOptions, out var search);
            if (created != Result.Success)
                return LobbyCollectionResponse.Failure($"EOS lobby search failed: {created}");

            try
            {
                var filtered = AddSearchParameters(search, query);
                if (filtered != Result.Success)
                    return LobbyCollectionResponse.Failure($"EOS lobby search failed: {filtered}");

                var found = await EOSLobbyApi.Find(search);
                // NotFound just means an empty result.
                if (found == Result.NotFound)
                    return LobbyCollectionResponse.Success(new List<LobbyInfo>());
                if (found != Result.Success)
                    return LobbyCollectionResponse.Failure($"EOS lobby search failed: {found}");

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint count = search.GetSearchResultCount(ref countOptions);
                var lobbies = new List<LobbyInfo>();

                for (uint i = 0; i < count; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (search.CopySearchResultByIndex(ref copyOptions, out var details) != Result.Success)
                        continue;

                    try
                    {
                        if (TryReadLobbyInfo(details, out var info) && MatchesClientSideFilters(info, query))
                            lobbies.Add(info);
                    }
                    finally
                    {
                        details.Release();
                    }
                }

                SortByNearValues(lobbies, query);
                return LobbyCollectionResponse.Success(lobbies);
            }
            finally
            {
                search.Release();
            }
        }

        private Result AddSearchParameters(LobbySearch search, LobbyQuery query)
        {
            // EOS needs at least one parameter; the bucket also scopes results to this game.
            var result = SetParameter(search, LobbyInterface.SEARCH_BUCKET_ID, _bucketId, ComparisonOp.Equal);
            if (result != Result.Success)
                return result;

            result = SetParameter(search, EOSLobbyKeys.Joinable, "1", ComparisonOp.Equal);
            if (result != Result.Success)
                return result;

            if (query?.stringFilters == null)
                return Result.Success;

            // Equality runs server-side. Ordering comparisons on strings and numerical filters
            // run client-side, since metadata is stored as strings.
            foreach (var filter in query.stringFilters)
            {
                if (filter.op != FilterComparison.Equal && filter.op != FilterComparison.NotEqual)
                    continue;

                if (!EOSLobbyKeys.TryEncodeMetadataKey(filter.key, out var key))
                    continue;

                result = SetParameter(search, key, filter.value,
                    filter.op == FilterComparison.Equal ? ComparisonOp.Equal : ComparisonOp.Notequal);
                if (result != Result.Success)
                    return result;
            }

            return Result.Success;
        }

        private static Result SetParameter(LobbySearch search, string key, string value, ComparisonOp op)
        {
            var options = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = key, Value = value },
                ComparisonOp = op,
            };
            return search.SetParameter(ref options);
        }

        private static bool TryReadLobbyInfo(LobbyDetails details, out LobbyInfo lobbyInfo)
        {
            lobbyInfo = default;

            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (details.CopyInfo(ref infoOptions, out var info) != Result.Success || !info.HasValue)
                return false;

            var attributes = EOSLobbyApi.ReadLobbyAttributes(details);
            var maxMembers = (int)info.Value.MaxMembers;
            string id = info.Value.LobbyId;

            lobbyInfo = new LobbyInfo
            {
                id = id,
                code = id,
                name = EOSLobbyApi.GetReserved(attributes, EOSLobbyKeys.Name),
                playerCount = maxMembers - (int)info.Value.AvailableSlots,
                maxPlayers = maxMembers,
                joinable = true,
                metadata = EOSLobbyApi.ExtractMetadata(attributes),
            };
            return true;
        }

        private static bool MatchesClientSideFilters(LobbyInfo info, LobbyQuery query)
        {
            if (query == null)
                return true;

            if (query.slotsAvailable > 0 && info.maxPlayers - info.playerCount < query.slotsAvailable)
                return false;

            if (query.stringFilters != null)
            {
                foreach (var filter in query.stringFilters)
                {
                    info.metadata.TryGetValue(filter.key, out var value);
                    if (!Compare(string.CompareOrdinal(value, filter.value), filter.op))
                        return false;
                }
            }

            if (query.numericalFilters != null)
            {
                foreach (var filter in query.numericalFilters)
                {
                    if (!info.metadata.TryGetValue(filter.key, out var raw) || !long.TryParse(raw, out var value))
                        return false;
                    if (!Compare(value.CompareTo(filter.value), filter.op))
                        return false;
                }
            }

            return true;
        }

        private static bool Compare(int comparison, FilterComparison op)
        {
            switch (op)
            {
                case FilterComparison.Equal: return comparison == 0;
                case FilterComparison.NotEqual: return comparison != 0;
                case FilterComparison.LessThan: return comparison < 0;
                case FilterComparison.GreaterThan: return comparison > 0;
                case FilterComparison.LessThanOrEqual: return comparison <= 0;
                case FilterComparison.GreaterThanOrEqual: return comparison >= 0;
                default: return true;
            }
        }

        private static void SortByNearValues(List<LobbyInfo> lobbies, LobbyQuery query)
        {
            if (query?.nearValueSorts == null || query.nearValueSorts.Count == 0)
                return;

            long Distance(LobbyInfo info, NearValueSort sort) =>
                info.metadata.TryGetValue(sort.key, out var raw) && long.TryParse(raw, out var value)
                    ? System.Math.Abs(value - sort.target)
                    : long.MaxValue;

            lobbies.Sort((a, b) =>
            {
                foreach (var sort in query.nearValueSorts)
                {
                    int cmp = Distance(a, sort).CompareTo(Distance(b, sort));
                    if (cmp != 0)
                        return cmp;
                }
                return 0;
            });
        }

        private static string LocalDisplayName()
        {
            var orchestrator = GameOrchestrator.active;
            return orchestrator && orchestrator.sessionProvider ? orchestrator.sessionProvider.playerName : null;
        }

        private static string DescribeJoinResult(Result result)
        {
            switch (result)
            {
                case Result.NotFound:
                    return "That lobby no longer exists.";
                case Result.LobbyTooManyPlayers:
                    return "The lobby is full.";
                case Result.LobbyNotAllowed:
                    return "You are not allowed to join that lobby.";
                default:
                    return $"Failed to join the lobby ({result}).";
            }
        }
    }
}
