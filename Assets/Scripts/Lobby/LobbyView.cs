using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.UI;
using TMPro;
using UnityEngine;
using PurrNet.Lobby;

namespace DwarfSimulator.Lobby
{
    public class LobbyView : MonoBehaviour
    {
        public const string LOBBY_STATUS_STRING = "LOBBY_STATUS_STRING";
        public const string LOBBY_STATUS_DETAILS_STRING = "LOBBY_STATUS_DETAILS_STRING";
        public const string LOBBY_CONN_INFO = GameStartKeys.ConnInfo;


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

        private ILobby _lobby;
        private bool _lobbyConnected;
        private bool _hasLocalMicEnabled;
        private float _allReadyTimer;
        private bool _wasAllReady;
        private bool _gameStarted;
        private bool _closingLobby;
        private string _localPlayerId;
        private bool _readyStateInitialized;
        private bool _wasReady = true;
        private bool _lobbyEventsUnsubscribed;

        private GameOrchestrator _orchestrator;

#if PURR_VOICE
        public bool localMicEnabled => _hasLocalMicEnabled;
        public event Action<bool> onLocalMicEnabledChanged;
#endif


        public void Setup(ILobby lobby, GameOrchestrator orchestrator)
        {
            UnsubscribeLobbyEvents();
            DisconnectFromLobby();
            ClearPlayerEntries();

            _orchestrator = orchestrator;
            _allReadyTimer = _timeToStartGame;
            _readyStateInitialized = false;
            _lobbyEventsUnsubscribed = false;
            _gameStarted = false;
            _wasAllReady = false;
            _closingLobby = false;
            _localPlayerId = lobby?.localPlayer?.id;
            ResetStatusLabels();

            // _playerPlaceholderPool ??= new UIPool<Transform>(_playerPlaceholderPrefab.transform, _playerContent);

            _lobby = lobby;
            _orchestrator.activeLobby = lobby;

            // RenderPlayerList(lobby);

            // Destruction is terminal and replays on subscription. Observe it before
            // replaying player joins, which can otherwise start a transient connection
            // to a lobby that was already destroyed while this view was loading.
            _lobby.onLobbyDestroyed += OnLobbyDestroyed;
            if (_closingLobby)
                return;

            _lobby.onPlayerJoined += OnPlayerJoined;
            _lobby.onPlayerLeft += OnPlayerLeft;
            _lobby.onPlayerUpdated += OnPlayerUpdated;
            _lobby.onOwnerChanged += OnOwnerChanged;

            if (_lobby.lobbyData.TryGetData(LOBBY_STATUS_STRING, out var lobbyStatus))
                OnMetadata(LOBBY_STATUS_STRING, lobbyStatus);
            if (_lobby.lobbyData.TryGetData(LOBBY_STATUS_DETAILS_STRING, out var lobbyStatusDetails))
                OnMetadata(LOBBY_STATUS_DETAILS_STRING, lobbyStatusDetails);
            if (_lobby.lobbyData.TryGetData(LOBBY_CONN_INFO, out var lobbyConnInfo))
                OnMetadata(LOBBY_CONN_INFO, lobbyConnInfo);

            _lobby.lobbyData.onDataChanged += OnMetadata;

            // _chat.Setup(lobby);
            _lobbyCode.text = lobby.joinCode;

            _microphoneFeature.SetActive(false);
            ConnectToLobby(lobby);
            // UpdateMicGraphics();


            showRock();
            _lobbyManager.GoToLobby();
        }

        void OnEnable()
        {
            _publicToggle.onToggled += OnToggleVisibility;
        }

        void OnDisable()
        {
            _publicToggle.onToggled -= OnToggleVisibility;
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

        public void CopyLobbyCodeToClipboard()
        {
            GUIUtility.systemCopyBuffer = _lobby.joinCode;
            // Toaster.Push("Lobby Code", "Code copied to clipboard!");
        }

        private void OnToggleVisibility(bool isOn)
        {
            if (_lobby == null || !_lobby.isOwner)
                return;

            _lobby.SetIsLobbyJoinable(isOn);
        }

        public void LeaveLobby()
        {
            if (_closingLobby)
                return;

            _closingLobby = true;
            var lobby = _lobby;
            ClearActiveLobby();
            DisconnectFromLobby();
            lobby?.LeaveLobby();
            // PopMe();
        }

        private void showRock()
        {
            int i = 0;
            foreach(var rock in _rocks)
            {
                i++;
                rock.SetActive(_lobby.maxPlayers < i ? true : false);
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
            if (!_lobbyConnected)
                return;

            // Clear this first because stopping the transport can synchronously
            // invoke lobby/provider callbacks.
            _lobbyConnected = false;
            if (_lobby != null && _lobbyConnection)
                _lobbyConnection.LeftLobby(_lobby);

            _lobbyManager.GoToMainMenu();
        }

        private readonly Dictionary<IPlayer, PlayerEntry> _uiPlayerEntry = new();

        private void ClearPlayerEntries()
        {
            foreach (var entry in _uiPlayerEntry.Values)
            {
                if (entry)
                    Destroy(entry.gameObject);
            }
            _uiPlayerEntry.Clear();
        }

        private void OnMetadata(string key, string value)
        {
            Debug.Log("Metadata //////");
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

        private void JoinGame(string rawConnectionInfo)
        {
            // TODO :
            // LaunchGame(async loadingView =>
            // {
            //     if (!GameStartKeys.TryReadConnectionInfo(rawConnectionInfo, out var info))
            //         throw new Exception("Received malformed connection info from the lobby.");

            //     loadingView.Setup("Loading game ...");
            //     var orchestrator = _orchestrator;
            //     await orchestrator.gameAllocator.LoadGame(lobby);
            //     orchestrator.gameAllocator.Connect(info, false);
            // }).Forget("[LobbyView] JoinGame failed");
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
            ClearActiveLobby();
            DisconnectFromLobby();
        }

        private void ClearActiveLobby()
        {
            if (_orchestrator != null && _orchestrator.activeLobby == _lobby)
                _orchestrator.activeLobby = null;
        }

        private void OnPlayerJoined(IPlayer player)
        {
            var localPlayer = _lobby?.localPlayer;
            if (localPlayer != null)
                _localPlayerId = localPlayer.id;

            // TODO :
            // RenderPlayerList(_lobby);
            ConnectToLobby(_lobby);

            if (_lobbyConnected)
                _lobbyConnection.OnPlayerRegistered(player);
        }

        private void OnPlayerLeft(IPlayer player)
        {
            if (player != null && !string.IsNullOrEmpty(_localPlayerId) && player.id == _localPlayerId)
            {
                CloseLobbyView();
                return;
            }

            if (_uiPlayerEntry.Remove(player, out var entry) && entry)
                Destroy(entry.gameObject);

            // TODO :
            // RenderPlayerList(_lobby);

            if (_lobbyConnected)
                _lobbyConnection.OnPlayerUnregistered(player);
        }

        private void ConnectToLobby(ILobby lobby)
        {
            if (!_closingLobby && _connectToPurrnetInLobby && _lobby.localPlayer != null &&
                !_lobbyConnected && _lobbyConnection)
            {
                // Mark connected first because provider setup can synchronously replay
                // a terminal event and ask this view to disconnect again.
                _lobbyConnected = true;
                try
                {
                    _lobbyConnection.JoinedLobby(lobby);
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
            if (_lobbyConnected && _lobbyConnection)
                _lobbyConnection.OnHostChanged(_lobby, host, host == _lobby.localPlayer);
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
    }
}
