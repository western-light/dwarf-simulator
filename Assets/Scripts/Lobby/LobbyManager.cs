using System;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using PurrNet.Lobby;
using PurrNet.UI;
using Unity.Cinemachine;
using UnityEditor.PackageManager;

namespace DwarfSimulator.Lobby
{
    public class LobbyManager : MonoBehaviour
    {
        [SerializeField] private GameOrchestrator _orchestrator;
        [SerializeField] private ViewStack _stack;

        [SerializeField] private GameObject _mainMenuContent;
        [SerializeField] private GameObject _joinMenuContent;
        [SerializeField] private TMP_InputField _codeField;
        [SerializeField] private string _lobbyName = "Dwarf_Simulator";
        [SerializeField] private int _maxPlayer = 4;
        [SerializeField] private CinemachineSplineDolly _splineDolly;
        [SerializeField] private int _dollyDotSettings = 0;
        [SerializeField] private int _dollyDotMain = 2;
        [SerializeField] private int _dollyDotLobby = 4;
        [SerializeField] private float _dollySpeed = 1;
        private float _cameraTargetPosition;

        private LobbyView lobbyView;

        private bool _cancelling;
        private bool _failed;
        private bool _unsubscribed;
        private MatchmakingTicket? _currentTicket;

        private LobbyProvider _subscribedProvider;
        private MatchmakingProvider _matchMakingProvider;


        void Awake()
        {
            _cameraTargetPosition = _dollyDotMain;
            showMainMenu();
        }

        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            UnsubscribeExternalJoin();

            if (_matchMakingProvider)
            {
                _matchMakingProvider.onStatusChanged -= OnStatusChanged;
                _matchMakingProvider.onMatchFound -= OnMatchFound;
                _matchMakingProvider.onMatchmakingError -= OnMatchmakingError;
            }
        }

        public void Initialize()
        {
            InitializeAsync().Forget("[LobbyManager] Initialize failed");

            _codeField.onSubmit.RemoveAllListeners();
            _codeField.text = "";
            _codeField.ActivateInputField();
            _codeField.onSubmit.AddListener(OnCodeSubmit);

            lobbyView = gameObject.GetComponent<LobbyView>();

            _matchMakingProvider = _orchestrator.matchmakingProvider;
            if (_matchMakingProvider)
            {
                _matchMakingProvider.onStatusChanged += OnStatusChanged;
                _matchMakingProvider.onMatchFound += OnMatchFound;
                _matchMakingProvider.onMatchmakingError += OnMatchmakingError;
            }
        }

        public async Task InitializeAsync()
        {
            // GameOrchestrator.active = _orchestrator;

            if (_orchestrator.sessionProvider)
                await _orchestrator.sessionProvider.Login(_stack);

            if (_orchestrator.lobbyProvider)
                await _orchestrator.lobbyProvider.Initialize();

            if (_orchestrator.matchmakingProvider)
                await _orchestrator.matchmakingProvider.Initialize();

            if (_orchestrator.gameAllocator)
                await _orchestrator.gameAllocator.Initialize();

            SubscribeExternalJoin();

            // _stack.Push<MainMenuView>().Setup(this, _orchestrator);
        }

        // Update is called once per frame
        void Update()
        {
            _splineDolly.CameraPosition = Mathf.MoveTowards(
                _splineDolly.CameraPosition,
                _cameraTargetPosition,
                _dollySpeed * Time.deltaTime
            );
        }

        public void showJoinMenu()
        {
            _mainMenuContent.SetActive(false);
            _joinMenuContent.SetActive(true);
        }

        public void showMainMenu()
        {
            _mainMenuContent.SetActive(true);
            _joinMenuContent.SetActive(false);
        }

        public void GoToSettings()
        {
            _cameraTargetPosition = _dollyDotSettings;
        }

        public void GoToMainMenu()
        {
            _cameraTargetPosition = _dollyDotMain;
        }

        public void GoToLobby()
        {
            _cameraTargetPosition = _dollyDotLobby;
        }

        public void CreateLobby()
        {

            // UiTask.Run(CreateLobbyAsync, "Lobby Creation Failed", _loadingOverlay, closeGuard: _closeParentView);
            UiTask.Run(CreateLobbyAsync, "Lobby Creation Failed");
        }

        private async Task CreateLobbyAsync()
        {
            var lobbySettings = new LobbySettings
            {
                maxPlayers = _maxPlayer,
                visibility = LobbyVisibility.Private,  //LobbyVisibility.Public
                name = _lobbyName
            };

            var response = await _orchestrator.lobbyProvider.CreateLobby(lobbySettings);
            
            if (!this)
                return;

            if (!response.success)
            {
                // Toaster.Push("Lobby Creation Failed", response.error, true);
                Debug.LogError("Lobby Creation Failed");
                return;
            }

            lobbyView.Setup(response.lobby, _orchestrator);
        }
        private void SubscribeExternalJoin()
        {
            UnsubscribeExternalJoin();

            if (_orchestrator.lobbyProvider)
            {
                _subscribedProvider = _orchestrator.lobbyProvider;
                _subscribedProvider.onExternalJoinRequested += OnExternalJoinRequested;
            }
        }

        private void UnsubscribeExternalJoin()
        {
            if (_subscribedProvider)
                _subscribedProvider.onExternalJoinRequested -= OnExternalJoinRequested;
            _subscribedProvider = null;
        }

        private void OnExternalJoinRequested(string lobbyId)
        {
            JoinExternalAsync(lobbyId).Forget("[LobbyManager] External join failed");
        }

        /// <summary>
        /// Handles a platform join request (e.g. an accepted Steam overlay invite):
        /// leaves the current lobby if any, joins the requested one behind a loading
        /// view, and opens the lobby screen.
        /// </summary>
        private async Task JoinExternalAsync(string lobbyId)
        {
            var provider = _orchestrator.lobbyProvider;
            if (!provider || string.IsNullOrEmpty(lobbyId))
                return;

            if (!provider.capabilities.Has(LobbyCapabilities.JoinLobbyById))
                return;

            if (_orchestrator.activeLobby?.id == lobbyId)
                return;

            // Leave the current lobby through the standard path (pops its view too).
            // var currentLobbyView = _stack.GetFirstView<LobbyView>();
            // if (currentLobbyView)
            //     currentLobbyView.LeaveLobby();

            var loadingView = _stack.Push<LoadingView>();
            loadingView.Setup("Joining lobby ...");

            try
            {
                var response = await provider.JoinLobby(lobbyId);

                if (!this)
                    return;

                if (!response.success)
                {
                    Toaster.PushError("Failed to join lobby", response.error);
                    return;
                }

                // _stack.Push<LobbyView>().Setup(response.lobby, _orchestrator);
            }
            catch (Exception e)
            {
                Toaster.PushError("Failed to join lobby", e);
                Debug.LogException(e);
            }
            finally
            {
                if (loadingView)
                    loadingView.PopMe();
            }
        }

        #region Join with code

        private void OnCodeSubmit(string text)
        {
            CodeSubmit();
        }

        public void CodeSubmit()
        {
            UiTask.Run(CodeSubmitAsync, "Join Failed");
        }

        private async Task CodeSubmitAsync()
        {
            var response = await _orchestrator.lobbyProvider.JoinLobbyByCode(_codeField.text.Trim());

            if (!this)
                return;

            if (!response.success)
            {
                // Toaster.Push("Join Failed", response.error, true);
                Debug.LogError("Join Failed");
                return;
            }

            lobbyView.Setup(response.lobby, _orchestrator);
        }

        #endregion

        #region Matchmaking

        public void MatchMaking()
        {
            Debug.Log("Matchmaking to implemente");
            StartMatchmakingAsync(new MatchmakingRequest
            {
                gameMode = "PurrNet"
            }).Forget("[MatchmakingView] StartMatchmaking failed");
        }

        private async Task StartMatchmakingAsync(MatchmakingRequest request)
        {
            var response = await _matchMakingProvider.StartMatchmaking(request);

            if (!this)
                return;

            if (!response.success)
            {
                Debug.LogError("Matchmaking failed " + response.error);
                // PopMe();
            }
            else
            {
                _currentTicket = response.ticket;
            }
        }

        private void OnMatchFound(MatchmakingTicket ticket, MatchResult result)
        {
            if (result.lobby != null)
            {
                lobbyView.Setup(result.lobby, _orchestrator);
                return;
            }

            StartGameFromMatch(result).Forget("[MatchmakingView] StartGameFromMatch failed");
        }

        private async Task StartGameFromMatch(MatchResult result)
        {
            _orchestrator.activeLobby = null;
            // TODO :
            // var loadingView = parentStack.ReplaceOrPush<LoadingView>(this);

            try
            {
                // loadingView.Setup("Allocating game...");

                var response = await _orchestrator.gameAllocator.AllocateGame(result);

                if (!response.success)
                    throw new Exception(response.error);

                // loadingView.Setup("Loading game...");
                await _orchestrator.gameAllocator.LoadGame(result);

                _orchestrator.gameAllocator.Connect(response.connection, result.isHost);
            }
            catch (Exception e)
            {
                Toaster.PushError("Failed to start game", e);
                if (_orchestrator.gameAllocator)
                    Debug.LogException(e, _orchestrator.gameAllocator);
            }
            finally
            {
                // if (loadingView)
                //     loadingView.PopMe();
            }
        }

        public void Cancel()
        {
            if (_cancelling)
                return;

            if (!_currentTicket.HasValue)
            {
                Debug.LogError("Failed to cancel");
                return;
            }

            _cancelling = true;
            CancelAsync(_currentTicket.Value).Forget("[MatchmakingView] CancelMatchmaking failed");
        }

        private async Task CancelAsync(MatchmakingTicket ticket)
        {
            var response = await _matchMakingProvider.CancelMatchmaking(ticket);

            if (!this)
                return;

            _cancelling = false;
            if (!response.success)
                Debug.LogError("Failed to cancel");
                //  Toaster.Push($"Failed to cancel", response.error, true);
        }

        private void OnStatusChanged(MatchmakingTicket ticket, MatchmakingStatus status)
        {
            // TODO : 
            // _message.text = $"Matchmaking: {status}";

            if (status is MatchmakingStatus.Failed && !_failed)
            {
                _failed = true;
                Debug.LogError("Matchmaking failed");
            }
        }

        private void OnMatchmakingError(MatchmakingTicket ticket, string error)
        {
            if (_failed)
                return;

            _failed = true;
            Debug.LogError("Matchmaking failed : " + error);
        }

        #endregion
    }
}
