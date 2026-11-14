using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.UI;
using TMPro;
using UnityEngine;
using PurrNet.Lobby;
using PurrNet;
using PurrNet.Transports;
#if !EOS_DISABLE
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
#endif

namespace DwarfSimulator.Lobby
{
    public class LobbyView : MonoBehaviour
    {
        public const string LOBBY_STATUS_STRING = "LOBBY_STATUS_STRING";
        public const string LOBBY_STATUS_DETAILS_STRING = "LOBBY_STATUS_DETAILS_STRING";
        public const string LOBBY_CONN_INFO = GameStartKeys.ConnInfo;
        public const string LOBBY_PUBLIC = "LOBBY_PUBLIC";

        [SerializeField] private int _minPlayerCount = 2;
        [Space]

        [SerializeField] private List<GameObject> _rocks;
        [SerializeField] private GameObject _prefabPerso;
        [SerializeField] private TMP_InputField _lobbyCode;
        [SerializeField] ToggleElement _publicToggle;
        [Space]


        [SerializeField] private LobbyManager _lobbyManager;

        [SerializeField] private ButtonElement _microphoneButton;
        [SerializeField] private TMP_Text _microphoneText;
        [SerializeField] private GameObject _microphoneFeature;
        [Space]
        [SerializeField] private float _timeToStartGame = 3f;
        [SerializeField] private TMP_Text _lobbyStatus;
        [SerializeField] private TMP_Text _lobbyStatusDetails;
        [Space]
        [SerializeField] private bool _connectToPurrnetInLobby = true;
        [SerializeField] private LobbyConnectionProvider _lobbyConnection;
        [SerializeField] private NetworkManager _networkManager;

        private ILobby _lobby;
        private bool _lobbyConnected;
        private bool _hasLocalMicEnabled;
        private float _allReadyTimer;
        private bool _wasAllReady;
        private bool _gameStarted;
        private bool _loadingGame;
        private bool _closingLobby;
        private string _localPlayerId;
        private bool _readyStateInitialized;
        private bool _wasReady = true;
        private bool _lobbyEventsUnsubscribed;
        private string _connectedHostId;
        private bool _isPublic;
        private bool _syncingPublicToggle;
        private ILobby _visibilityChangeLobby;

        private GameOrchestrator _orchestrator;

        public ILobby lobby => _lobby;
        private readonly Dictionary<IPlayer, GameObject> _uiPlayerEntry = new();

#if PURR_VOICE
        public bool localMicEnabled => _hasLocalMicEnabled;
        public event Action<bool> onLocalMicEnabledChanged;
#endif


        public void Setup(ILobby lobby, GameOrchestrator orchestrator)
        {
            if (lobby == null || !orchestrator)
                throw new ArgumentException("A lobby and an orchestrator are required.");

            if (_lobby != null && !ReferenceEquals(_lobby, lobby))
                LeaveLobby();

            UnsubscribeLobbyEvents();
            DisconnectFromLobby();
            ClearPlayerEntries();

            _orchestrator = orchestrator;
            _allReadyTimer = _timeToStartGame;
            _readyStateInitialized = false;
            _lobbyEventsUnsubscribed = false;
            _gameStarted = false;
            _loadingGame = false;
            _wasAllReady = false;
            _closingLobby = false;
            _localPlayerId = lobby?.localPlayer?.id;
            ResetStatusLabels();

            _lobby = lobby;
            _orchestrator.activeLobby = lobby;
            _isPublic = lobby.lobbyData.GetData(LOBBY_PUBLIC) == "1";
            RefreshPublicToggle();
            Debug.Log($"[Lobby] Lobby={lobby.id}, local EOS={lobby.localPlayer?.id}, host EOS={lobby.owner?.id}, members={lobby.players.Count}", this);

            // Destruction is terminal and replays on subscription. Observe it before
            // replaying player joins, which can otherwise start a transient connection
            // to a lobby that was already destroyed while this view was loading.
            _lobby.onLobbyDestroyed += OnLobbyDestroyed;
            if (_closingLobby)
                return;

            _lobby.onPlayerLeft += OnPlayerLeft;
            _lobby.onPlayerUpdated += OnPlayerUpdated;
            _lobby.onOwnerChanged += OnOwnerChanged;
            _lobby.onPlayerJoined += OnPlayerJoined;

            _lobby.lobbyData.onDataChanged += OnMetadata;

            showRock();
            RenderPlayerList(lobby);
            _lobbyCode.text = lobby.joinCode;
            _microphoneFeature.SetActive(false);
            ConnectToLobby(lobby);
            _lobbyManager.GoToLobby();

            if (_lobby.lobbyData.TryGetData(LOBBY_STATUS_STRING, out var lobbyStatus))
                OnMetadata(LOBBY_STATUS_STRING, lobbyStatus);
            if (_lobby.lobbyData.TryGetData(LOBBY_STATUS_DETAILS_STRING, out var lobbyStatusDetails))
                OnMetadata(LOBBY_STATUS_DETAILS_STRING, lobbyStatusDetails);
            if (_lobby.lobbyData.TryGetData(LOBBY_CONN_INFO, out var lobbyConnInfo))
                OnMetadata(LOBBY_CONN_INFO, lobbyConnInfo);

        }

        void OnEnable()
        {
            RefreshPublicToggle();
            _publicToggle.onToggled += OnToggleVisibility;
        }

        void OnDisable()
        {
            _publicToggle.onToggled -= OnToggleVisibility;
        }

        private void OnDestroy()
        {
            // The game scene keeps membership in the shared orchestrator.
            _closingLobby = true;
            UnsubscribeLobbyEvents();
            DisconnectFromLobby();
            if (!_loadingGame)
            {
                ClearActiveLobby();
                _lobby?.LeaveLobby();
            }
            _lobby = null;
            ClearPlayerEntries();
        }


        private void Update()
        {
            if (_lobby?.localPlayer == null || !_lobby.localPlayer.isOwner)
                return;

            if (_gameStarted)
                return;

            bool allReady = _lobby.players.Count > 0;

            foreach (var player in _lobby.players)
            {
                if (!player.isReady)
                {
                    allReady = false;
                    break;
                }
            }

            // TODO : 
            // if (allReady)
            // {
            //     if (!_wasAllReady)
            //     {
            //         _lobbyStatus.text = "STARTING GAME";
            //         _lobby.lobbyData.SetData(LOBBY_STATUS_STRING, _lobbyStatus.text);
            //         _wasAllReady = true;
            //     }

            //     _allReadyTimer -= Time.deltaTime;

            //     if (_allReadyTimer < 0)
            //     {
            //         _gameStarted = true;
            //         _allReadyTimer = 0f;

            //         _lobbyStatusDetails.text = $"LOADING ...";
            //         _lobby.lobbyData.SetData(LOBBY_STATUS_DETAILS_STRING, _lobbyStatusDetails.text);

            //         StartGame();
            //         return;
            //     }

            //     int secondsLeft = Mathf.CeilToInt(_allReadyTimer);
            //     var text = $"{secondsLeft} ...";

            //     if (text != _lobbyStatusDetails.text)
            //     {
            //         _lobbyStatusDetails.text = text;
            //         _lobby.lobbyData.SetData(LOBBY_STATUS_DETAILS_STRING, text);
            //     }
            // }
            // else if (_wasAllReady)
            // {
            //     _wasAllReady = false;
            //     _allReadyTimer = _timeToStartGame;
            //     ResetStatusLabels();
            //     _lobby.lobbyData.SetData(LOBBY_STATUS_STRING, _lobbyStatus.text);
            //     _lobby.lobbyData.SetData(LOBBY_STATUS_DETAILS_STRING, _lobbyStatusDetails.text);
            // }
        }

        public void ButtonStartGame()
        {
            StartGame();
        }

        private bool CanStartGame()
        {
            return _lobby != null && !_closingLobby && !_gameStarted &&
                   _lobby.isOwner && _lobby.players.Count >= _minPlayerCount;
        }

        private void SetLoadingStatus()
        {
            if (_lobby.isOwner)
            {
                _lobbyStatusDetails.text = $"LOADING ...";
                _lobby.lobbyData.SetData(LOBBY_STATUS_DETAILS_STRING, _lobbyStatusDetails.text);
            }
        }

        public void CopyLobbyCodeToClipboard()
        {
            GUIUtility.systemCopyBuffer = _lobby.joinCode;
            // Toaster.Push("Lobby Code", "Code copied to clipboard!");
        }

        private void OnToggleVisibility(bool isOn)
        {
            if (_syncingPublicToggle)
                return;

            if (_lobby == null || _closingLobby || _gameStarted || !_lobby.isOwner ||
                ReferenceEquals(_visibilityChangeLobby, _lobby))
            {
                RefreshPublicToggle();
                return;
            }

            ChangeVisibilityAsync(_lobby, isOn).Forget("[Lobby] Visibility change failed");
        }

        private void RefreshPublicToggle()
        {
            if (!_publicToggle)
                return;

            _syncingPublicToggle = true;
            try
            {
                _publicToggle.value = _isPublic;
            }
            finally
            {
                _syncingPublicToggle = false;
            }
        }

        private async Task ChangeVisibilityAsync(ILobby targetLobby, bool isPublic)
        {
            _visibilityChangeLobby = targetLobby;
            // Display the confirmed value until EOS accepts the change.
            RefreshPublicToggle();
            try
            {
                await SetEOSVisibilityAsync(targetLobby, isPublic);
                if (!this || _closingLobby || !ReferenceEquals(_lobby, targetLobby))
                    return;

                _isPublic = isPublic;
                targetLobby.lobbyData.SetData(LOBBY_PUBLIC, isPublic ? "1" : "0");
                Debug.Log($"[Lobby] Visibility={(isPublic ? "public (1)" : "private (0)")}", this);
            }
            finally
            {
                if (ReferenceEquals(_visibilityChangeLobby, targetLobby))
                    _visibilityChangeLobby = null;
                if (this && ReferenceEquals(_lobby, targetLobby))
                    RefreshPublicToggle();
            }
        }

        private static async Task SetEOSVisibilityAsync(ILobby lobby, bool isPublic)
        {
#if !EOS_DISABLE
            var api = EOSManager.Instance.GetEOSLobbyInterface();
            var options = new UpdateLobbyModificationOptions
            {
                LobbyId = lobby.id,
                LocalUserId = EOSManager.Instance.GetProductUserId()
            };
            if (api == null)
                throw new InvalidOperationException("EOS lobbies are not initialized.");

            var result = api.UpdateLobbyModification(ref options, out var modification);
            if (result != Result.Success)
                throw new InvalidOperationException($"Cannot edit lobby visibility: {result}");

            try
            {
                var permission = new LobbyModificationSetPermissionLevelOptions
                {
                    PermissionLevel = isPublic ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly
                };
                result = modification.SetPermissionLevel(ref permission);
                if (result != Result.Success)
                    throw new InvalidOperationException($"Cannot set lobby visibility: {result}");

                var completion = new TaskCompletionSource<Result>();
                var update = new UpdateLobbyOptions { LobbyModificationHandle = modification };
                api.UpdateLobby(ref update, null, (ref UpdateLobbyCallbackInfo info) =>
                {
                    if (Common.IsOperationComplete(info.ResultCode))
                        completion.TrySetResult(info.ResultCode);
                });
                result = await completion.Task;
                if (result != Result.Success)
                    throw new InvalidOperationException($"EOS rejected the visibility change: {result}");
            }
            finally
            {
                modification.Release();
            }
#else
            throw new NotSupportedException("EOS is disabled; lobby visibility cannot be changed.");
#endif
        }

        public void LeaveLobby()
        {
            if (_closingLobby)
                return;

            var lobby = _lobby;
            CloseLobbyView();
            lobby?.LeaveLobby();
        }

        private void showRock()
        {
            for (int i = 0; i < _rocks.Count; i++)
            {
                if (_rocks[i])
                    _rocks[i].SetActive(i < _lobby.maxPlayers);
            }
        }

        private void UnsubscribeLobbyEvents()
        {
            if (_lobbyEventsUnsubscribed || _lobby == null)
                return;

            _lobbyEventsUnsubscribed = true;
            _lobby.onPlayerJoined -= OnPlayerJoined;
            _lobby.onPlayerLeft -= OnPlayerLeft;
            _lobby.onPlayerUpdated -= OnPlayerUpdated;
            _lobby.onLobbyDestroyed -= OnLobbyDestroyed;
            _lobby.onOwnerChanged -= OnOwnerChanged;
            _lobby.lobbyData.onDataChanged -= OnMetadata;
        }

        private void DisconnectFromLobby()
        {
            // Clear this first because stopping the transport can synchronously
            // invoke lobby/provider callbacks.
            bool wasConnected = _lobbyConnected;
            _lobbyConnected = false;
            _connectedHostId = null;

            var manager = ResolveNetworkManager();
            // EOS StopListening clears its host-loopback flag. Stop the local
            // client first, while Disconnect can still recognise that loopback.
            if (manager && manager.transport)
                manager.StopClient();

            // LeftLobby also cancels the provider's pending automatic restarts.
            if (wasConnected && _lobby != null && _lobbyConnection)
                _lobbyConnection.LeftLobby(_lobby);

            if (manager && manager.transport)
            {
                if (manager.serverState != ConnectionState.Disconnected &&
                    manager.serverState != ConnectionState.Disconnecting)
                    manager.StopServer();
                manager.onClientConnectionState -= OnLobbyClientConnectionState;
                manager.onServerConnectionState -= OnLobbyServerConnectionState;
                manager.onPlayerJoined -= OnNetworkPlayerJoined;
            }
        }

        private NetworkManager ResolveNetworkManager()
        {
            if (!_networkManager && _lobbyConnection)
            {
                _networkManager = _lobbyConnection.GetComponent<NetworkManager>();
                if (!_networkManager)
                    _networkManager = _lobbyConnection.GetComponentInParent<NetworkManager>();
            }
            return _networkManager;
        }

        private void OnLobbyClientConnectionState(ConnectionState state)
        {
            Debug.Log($"[Lobby] Client={state}, local EOS={_lobby?.localPlayer?.id}, host EOS={_connectedHostId}", this);
        }

        private void OnLobbyServerConnectionState(ConnectionState state)
        {
            Debug.Log($"[Lobby] Listening={state}, local EOS={_lobby?.localPlayer?.id}", this);
        }

        private void OnNetworkPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            Debug.Log($"[Lobby] PurrNet player registered={player}, asServer={asServer}, local EOS={_localPlayerId}", this);
        }

        private void OnMetadata(string key, string value)
        {
            if (_lobby == null || _closingLobby)
                return;

            if (key == LOBBY_PUBLIC)
            {
                _isPublic = value == "1";
                RefreshPublicToggle();
                return;
            }

            if (_lobby.isOwner)
                return;

            switch (key)
            {
                case LOBBY_STATUS_STRING:
                {
                    _lobbyStatus.text = value;
                    break;
                }
                case LOBBY_STATUS_DETAILS_STRING:
                {
                    _lobbyStatusDetails.text = value;
                    break;
                }
                case LOBBY_CONN_INFO:
                {
                    JoinGame(value);
                    break;
                }
            }
        }

        public void StartGame()
        {
            if (!CanStartGame())
                return;

            _gameStarted = true;
            SetLoadingStatus();
            var startingLobby = _lobby;
            LaunchGame(async loadingView =>
            {
                var orchestrator = _orchestrator;
                var joinInfo = await orchestrator.gameAllocator.AllocateGame(startingLobby);
                if (!this || _closingLobby || !ReferenceEquals(_lobby, startingLobby))
                    return;

                if (!joinInfo.success)
                    throw new Exception(joinInfo.error);

                GameStartKeys.PublishConnectionInfo(startingLobby, joinInfo.connection);

                // TODO :
                // loadingView.Setup("Loading game ...");
                _loadingGame = true;
                DisconnectFromLobby();
                await orchestrator.gameAllocator.LoadGame(startingLobby);
                if (ReferenceEquals(orchestrator.activeLobby, startingLobby))
                    orchestrator.gameAllocator.Connect(joinInfo.connection, true);
            }).Forget("[LobbyView] StartGame failed");
        }

        private void JoinGame(string rawConnectionInfo)
        {
            if (_gameStarted || _closingLobby || _lobby == null || string.IsNullOrEmpty(rawConnectionInfo))
                return;

            _gameStarted = true;
            var joiningLobby = _lobby;
            LaunchGame(async loadingView =>
            {
                if (!GameStartKeys.TryReadConnectionInfo(rawConnectionInfo, out var info))
                    throw new Exception("Received malformed connection info from the lobby.");

                // TODO :
                // loadingView.Setup("Loading game ...");
                var orchestrator = _orchestrator;
                _loadingGame = true;
                DisconnectFromLobby();
                await orchestrator.gameAllocator.LoadGame(joiningLobby);
                if (ReferenceEquals(orchestrator.activeLobby, joiningLobby))
                    orchestrator.gameAllocator.Connect(info, false);
            }).Forget("[LobbyView] JoinGame failed");
        }

        private async Task LaunchGame(Func<LoadingView, Task> flow)
        {
            LoadingView loadingView = null;

            // TODO : Add loading screen
            try
            {
                // loadingView = parentStack.Push<LoadingView>();
                // loadingView.Setup("Allocating game ...");

                await flow(loadingView);
            }
            catch (Exception e)
            {
                Toaster.PushError("Failed to start game", e);
                Debug.LogError("Failed to start game");
                Debug.LogException(e, _orchestrator.gameAllocator);
                if (this && !_closingLobby)
                    ResetStartState();
            }
            finally
            {
                // if (loadingView)
                //     loadingView.PopMe();
            }
        }

        private void ResetStartState()
        {
            ResetStatusLabels();
            _wasAllReady = false;
            _gameStarted = false;
            _loadingGame = false;
            _allReadyTimer = _timeToStartGame;
            if (_lobby?.isOwner == true)
                _lobby.lobbyData.RemoveData(LOBBY_CONN_INFO);
            ConnectToLobby(_lobby);
            OnOwnerChanged(_lobby?.owner);
            // _lobby?.localPlayer?.SetReady(false);
        }

        private void OnLobbyDestroyed()
        {
            CloseLobbyView();
        }

        private void CloseLobbyView()
        {
            if (_closingLobby)
                return;

            _closingLobby = true;
            UnsubscribeLobbyEvents();
            ClearActiveLobby();
            DisconnectFromLobby();
            _lobby = null;
            _localPlayerId = null;
            _isPublic = false;
            RefreshPublicToggle();
            ClearPlayerEntries();
            if (_lobbyManager)
            {
                _lobbyManager.showMainMenu();
                _lobbyManager.GoToMainMenu();
            }
        }

        private void ClearActiveLobby()
        {
            if (_orchestrator != null && _orchestrator.activeLobby == _lobby)
                _orchestrator.activeLobby = null;
        }

        private void OnPlayerJoined(IPlayer player)
        {
            if (_closingLobby || _lobby == null)
                return;
            var localPlayer = _lobby?.localPlayer;
            if (localPlayer != null)
                _localPlayerId = localPlayer.id;

            ConnectToLobby(_lobby);

            if (_lobbyConnected)
                _lobbyConnection.OnPlayerRegistered(player);

            Debug.Log($"[Lobby] EOS member={player.id}, local EOS={_localPlayerId}, members={_lobby.players.Count}", this);
            RenderPlayerList(_lobby);
        }

        private void OnPlayerLeft(IPlayer player)
        {
            if (_closingLobby || _lobby == null)
                return;
            if (player != null && !string.IsNullOrEmpty(_localPlayerId) && player.id == _localPlayerId)
            {
                CloseLobbyView();
                return;
            }

            if (_uiPlayerEntry.Remove(player, out var entry) && entry)
            {
                entry.SetActive(false);
                Destroy(entry.gameObject);
            }

            RenderPlayerList(_lobby);

            if (_lobbyConnected)
                _lobbyConnection.OnPlayerUnregistered(player);
        }

        private void ConnectToLobby(ILobby lobby)
        {
            if (!_closingLobby && !_loadingGame && _connectToPurrnetInLobby && lobby?.localPlayer != null &&
                !_lobbyConnected && _lobbyConnection)
            {
                // Mark connected first because provider setup can synchronously replay
                // a terminal event and ask this view to disconnect again.
                _lobbyConnected = true;
                try
                {
                    var manager = ResolveNetworkManager();
                    if (manager)
                    {
                        manager.onClientConnectionState -= OnLobbyClientConnectionState;
                        manager.onServerConnectionState -= OnLobbyServerConnectionState;
                        manager.onPlayerJoined -= OnNetworkPlayerJoined;
                        manager.onClientConnectionState += OnLobbyClientConnectionState;
                        manager.onServerConnectionState += OnLobbyServerConnectionState;
                        manager.onPlayerJoined += OnNetworkPlayerJoined;
                    }
                    _lobbyConnection.JoinedLobby(lobby);
                    // Membership can become available after the owner event was
                    // replayed. Bootstrap explicitly once both are known.
                    OnOwnerChanged(lobby.owner);
                }
                catch
                {
                    _lobbyConnected = false;
                    throw;
                }
            }
        }

        private void OnPlayerUpdated(IPlayer player)
        {
            UpdateLocalPlayerData(_lobby);
        }

        private void UpdateLocalPlayerData(ILobby lobby)
        {
            bool localPlayerReady = lobby.localPlayer?.isReady == true;

            if (!_readyStateInitialized || _wasReady != localPlayerReady)
            {
                // TODO : 
                // _readyButtonText.text = localPlayerReady ? "Unready" : "Ready";
                // var color = localPlayerReady ? _readyColor : _unreadyColor;
                // ThemeColors.Set(_readyButton, color,
                //     localPlayerReady ? _readyHover : _unreadyHover,
                //     localPlayerReady ? _readyColorTone : _unreadyColorTone,
                //     localPlayerReady ? _readyHoverTone : _unreadyHoverTone);
                // ThemeColors.Set(_readyButtonText, localPlayerReady ? _readyTextColor : _unreadyTextColor);
                _wasReady = localPlayerReady;
                _readyStateInitialized = true;
            }
        }

        private void OnOwnerChanged(IPlayer host)
        {
            if (_closingLobby || _loadingGame || host == null || !_lobbyConnected || !_lobbyConnection ||
                _connectedHostId == host.id)
                return;

            _connectedHostId = host.id;
            var manager = ResolveNetworkManager();
            if (manager && manager.transport)
                manager.StopClient();

            // The provider stops the server next. Do not replay this for the same
            // owner: doing so tears down a host which has just started listening.
            _lobbyConnection.OnHostChanged(_lobby, host, host.id == _lobby.localPlayer?.id);
        }

#if PURR_VOICE
        public void EnableMicrophoneFeature()
        {
            _microphoneFeature.SetActive(true);
        }
#endif

        public void ToggleMicrophone()
        {
            _hasLocalMicEnabled = !_hasLocalMicEnabled;
#if PURR_VOICE
            onLocalMicEnabledChanged?.Invoke(_hasLocalMicEnabled);
#endif
            UpdateMicGraphics();
        }

        private void UpdateMicGraphics()
        {
            _microphoneText.text = _hasLocalMicEnabled ? "<icon=microphone>" : "<icon=microphone_off>";

            // TODO :
            // var color = _hasLocalMicEnabled ? _unmutedColor : _mutedColor;
            // var highlight = _hasLocalMicEnabled ? _unmutedHover : _mutedHover;

            // ThemeColors.Set(_microphoneButton, color, highlight,
            //     _hasLocalMicEnabled ? _unmutedColorTone : _mutedColorTone,
            //     _hasLocalMicEnabled ? _unmutedHoverTone : _mutedHoverTone);
            // color.contrast = true;
            // ThemeColors.Set(_microphoneText, color);
        }

        private void ResetStatusLabels()
        {
            _lobbyStatus.SetText("WAITING FOR PLAYERS");
            _lobbyStatusDetails.SetText("READY UP!");
        }

        private void RenderPlayerList(ILobby lobby)
        {
            int i = 0;
            foreach (var player in lobby.players)
            {
                if (i >= _rocks.Count)
                    break;
                if (!_uiPlayerEntry.ContainsKey(player))
                    CreatePlayerEntry(player, i);
                else if (_uiPlayerEntry[player])
                    _uiPlayerEntry[player].transform.SetParent(_rocks[i].transform, false);
                i++;
            }

            UpdateLocalPlayerData(lobby);
        }

        private void CreatePlayerEntry(IPlayer player, int index)
        {
            var entry = Instantiate(_prefabPerso, _rocks[index].transform);
            entry.transform.localPosition = Vector3.zero;
            // entry.Setup(_lobby, player, OnKickPlayer);
            _uiPlayerEntry.Add(player, entry);
        }

        private void ClearPlayerEntries()
        {
            foreach (var entry in _uiPlayerEntry.Values)
            {
                if (entry)
                {
                    entry.SetActive(false);
                    Destroy(entry.gameObject);
                }
            }
            _uiPlayerEntry.Clear();
        }

        // private void OnKickPlayer(IPlayer target)
        // {
        //     _lobby.KickPlayer(target);
        // }
    }
}
