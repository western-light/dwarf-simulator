using System.Threading.Tasks;
using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices;
using PurrNet.Lobby;
using PurrNet.UI;
using UnityEngine;

namespace DwarfSimulator.Lobby.EOS
{
    /// <summary>
    /// Logs the player into EOS Connect, which yields the ProductUserId used as the player id
    /// everywhere: lobby membership, and the address the EOS transport connects to.
    /// </summary>
    [ProviderDependency("com.playeveryware.eos", "EOS Plugin for Unity")]
    [CreateAssetMenu(menuName = "PurrLobby/EOS/Session Provider", fileName = "EOS Session Provider", order = -204)]
    public class EOSSessionProvider : SessionProvider
    {
        public enum LoginMethod
        {
            /// <summary>Anonymous per-device account. One account per machine, so two local instances collide.</summary>
            DeviceId,

            /// <summary>Epic account served by the EOS Developer Authentication Tool, for local multi-instance testing.</summary>
            DevAuthTool,
        }

        [SerializeField] private LoginMethod _loginMethod = LoginMethod.DeviceId;

        [Tooltip("Display name for DeviceId login. Empty uses the machine name.")]
        [SerializeField] private string _displayName;

        [Header("Dev Auth Tool")]
        [SerializeField] private string _devAuthHost = "localhost:6547";

        [Tooltip("Credential name configured in the Dev Auth Tool. Override per instance with -devauth=<name> on the command line.")]
        [SerializeField] private string _devAuthCredential = "Player1";

        private const int MaxDisplayNameLength = 32;
        private const string DevAuthArg = "-devauth=";

        [System.NonSerialized] private string _playerName;

        public override bool isLoggedIn => EOSManager.Instance.HasLoggedInWithConnect();

        public override string playerId => isLoggedIn ? EOSManager.Instance.GetProductUserId().ToString() : null;

        public override string playerName => _playerName;

        public override async Task Login(ViewStack stack)
        {
            if (isLoggedIn)
                return;

            if (EOSManager.Instance.GetEOSPlatformInterface() == null)
            {
                Debug.LogError($"[{name}] The EOS platform is not initialized. Add an EOSManager to the menu scene and fill in the EOS configuration.");
                return;
            }

            string displayName;
            Result result;

            if (_loginMethod == LoginMethod.DevAuthTool)
            {
                displayName = ResolveDevAuthCredential();
                result = await LoginWithDevAuth(displayName);
            }
            else
            {
                displayName = ResolveDisplayName();
                result = await LoginWithDeviceId(displayName);
            }

            if (result != Result.Success)
            {
                Debug.LogError($"[{name}] EOS login ({_loginMethod}) failed: {result}");
                return;
            }

            _playerName = displayName;
        }

        public override Task Logout()
        {
            _playerName = null;
            return Task.CompletedTask;
        }

        private static Task<Result> LoginWithDeviceId(string displayName)
        {
            var tcs = new TaskCompletionSource<Result>();
            var connect = EOSManager.Instance.GetEOSConnectInterface();
            var options = new Epic.OnlineServices.Connect.CreateDeviceIdOptions
            {
                DeviceModel = Truncate(SystemInfo.deviceModel, 64),
            };

            connect.CreateDeviceId(ref options, null, (ref Epic.OnlineServices.Connect.CreateDeviceIdCallbackInfo created) =>
            {
                // DuplicateNotAllowed means this device already has an id: the normal case after the first run.
                if (created.ResultCode != Result.Success && created.ResultCode != Result.DuplicateNotAllowed)
                {
                    tcs.TrySetResult(created.ResultCode);
                    return;
                }

                EOSManager.Instance.StartConnectLoginWithDeviceToken(displayName,
                    info => tcs.TrySetResult(info.ResultCode));
            });

            return tcs.Task;
        }

        private Task<Result> LoginWithDevAuth(string credential)
        {
            var tcs = new TaskCompletionSource<Result>();

            EOSManager.Instance.StartLoginWithLoginTypeAndToken(
                Epic.OnlineServices.Auth.LoginCredentialType.Developer, _devAuthHost, credential, auth =>
                {
                    if (auth.ResultCode != Result.Success)
                    {
                        tcs.TrySetResult(auth.ResultCode);
                        return;
                    }

                    EOSManager.Instance.StartConnectLoginWithEpicAccount(auth.LocalUserId, connect =>
                    {
                        // First Connect login for this Epic account: its ProductUserId must be created.
                        if (connect.ResultCode == Result.InvalidUser)
                        {
                            EOSManager.Instance.CreateConnectUserWithContinuanceToken(connect.ContinuanceToken,
                                created => tcs.TrySetResult(created.ResultCode));
                            return;
                        }

                        tcs.TrySetResult(connect.ResultCode);
                    });
                });

            return tcs.Task;
        }

        private string ResolveDisplayName()
        {
            var displayName = string.IsNullOrWhiteSpace(_displayName) ? SystemInfo.deviceName : _displayName;
            return Truncate(displayName, MaxDisplayNameLength);
        }

        private string ResolveDevAuthCredential()
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(DevAuthArg) && arg.Length > DevAuthArg.Length)
                    return arg.Substring(DevAuthArg.Length);
            }

            return _devAuthCredential;
        }

        private static string Truncate(string value, int maxLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
