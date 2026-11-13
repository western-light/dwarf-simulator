using System;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using PurrNet.Lobby;
using PurrNet.UI;
using Unity.Cinemachine;

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


        private LobbyProvider _subscribedProvider;


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
            // UnsubscribeExternalJoin();
        }

        public void Initialize()
        {
            InitializeAsync().Forget("[LobbyManager] Initialize failed");
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

        public void JoinWithCode()
        {
            Debug.Log("Join with code to implemente");
        }

        public void MatchMaking()
        {
            Debug.Log("Matchmaking to implemente");
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
                Debug.Log("Lobby Creation Failed");
                return;
            }

            // var lobbyView = parentStack.ReplaceOrPush<LobbyView>(this);
            var lobbyView = gameObject.GetComponent<LobbyView>();
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
    }
}
