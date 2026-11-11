using System.IO;
using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Online play before the game starts: connecting, identity, joining or
    /// resuming a room, lobby ticking, and the menu's server status dot.</summary>
    public sealed partial class LemonadeWarsApp
    {
        private const string PlayerKeyPref = "lw_player_key";
        private const string ResumeCodePref = "lw_code";
        private const string ResumeTokenPref = "lw_token";
        private const float ConnectTimeoutSeconds = 10f;

        // The lobby verb to send once the socket opens.
        private bool _pendingSend;
        private bool _pendingCreate;
        private bool _pendingListOnly;
        private string _pendingName;
        private string _pendingCode;
        private string _pendingToken;
        private float _pendingSince;

        private float _lastGamesRefresh;
        private int _lobbyRevision = -1;

        /// <summary>From StreamingAssets/client-config.json (gitignored; written by tools/sync_unity.sh,
        /// baked into builds, editable post-build). Players cannot change it in-app.</summary>
        private static string LoadConfiguredServerUrl()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "client-config.json");
                if (File.Exists(path))
                {
                    var config = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    string url = (string)config["serverUrl"];
                    if (!string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
            }
            catch (System.Exception)
            {
                // Malformed config: fall through to the dev default.
            }
            return "ws://localhost:5225/ws";
        }

        /// <summary>Durable identity secret; generated once and never shown anywhere.</summary>
        private static string PlayerKey
        {
            get
            {
                string key = PlayerPrefs.GetString(PlayerKeyPref, "");
                if (string.IsNullOrEmpty(key))
                {
                    key = System.Guid.NewGuid().ToString("N") + System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(PlayerKeyPref, key);
                    PlayerPrefs.Save();
                }
                return key;
            }
        }

        private void ConnectRemote(string url, bool create, string name, string code,
            string token, bool listOnly = false)
        {
            _session?.Dispose();
            _remote = RemoteGameSession.Connect(url);
            _session = _remote;
            _session.EventEmitted += OnGameEvent;
            _remote.TurnAlert += OnTurnAlert;
            _pendingSend = true;
            _pendingSince = Time.time;
            _pendingCreate = create;
            _pendingListOnly = listOnly;
            _pendingName = name;
            _pendingCode = code;
            _pendingToken = token;
            PlayerPrefs.SetString("lw_name", name);
            _screen = Screen.Lobby;
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, "Connecting to " + url + "...", false);
        }

        /// <summary>Sends the deferred lobby verb once the socket is open, or gives up.</summary>
        private void TickPendingConnection()
        {
            if (_remote == null || !_pendingSend)
            {
                return;
            }
            if (_remote.Connected)
            {
                _pendingSend = false;
                // Identity first on every connection: seats bind to it, and the
                // welcome reply carries the My Games list.
                _remote.Hello(PlayerKey, _pendingName);
                if (_pendingCreate)
                {
                    _remote.CreateRoom(_pendingName);
                }
                else if (!_pendingListOnly)
                {
                    _remote.JoinRoom(_pendingCode, _pendingName,
                        string.IsNullOrEmpty(_pendingToken) ? null : _pendingToken);
                }
            }
            else if (_remote.ConnectionError.Length > 0)
            {
                _pendingSend = false;
                BackToMenu(_remote.ConnectionError);
            }
            else if (Time.time - _pendingSince > ConnectTimeoutSeconds)
            {
                // A dead host can hang ConnectAsync with no error for minutes.
                _pendingSend = false;
                BackToMenu("Could not reach the server — check the address and try again.");
            }
        }

        /// <summary>Browsing My Games: keep the YOUR TURN badges fresh.</summary>
        private void TickGamesListRefresh()
        {
            if (_pendingListOnly && _screen == Screen.Lobby && _remote != null &&
                _remote.Room.YourSeat < 0 && _remote.Connected &&
                Time.time - _lastGamesRefresh > 20f)
            {
                _lastGamesRefresh = Time.time;
                _remote.ListGames();
            }
        }

        private void OnTurnAlert(string code)
        {
            _alertText.text = $"YOUR TURN — GAME {code}";
            _alertUntil = Time.time + 5f;
            _alertGroup.blocksRaycasts = true;
            if (_pendingListOnly && _remote != null && _remote.Room.YourSeat < 0)
            {
                _remote.ListGames(); // browsing My Games: badge updates immediately
            }
        }

        /// <summary>A My Games row was picked: enter that room on the same connection.</summary>
        private void JoinFromList(string code)
        {
            if (_remote == null)
            {
                return;
            }
            // The stored per-room token only matters for games created before identities existed.
            string token = PlayerPrefs.GetString(ResumeCodePref, "") == code
                ? PlayerPrefs.GetString(ResumeTokenPref, "")
                : "";
            _remote.JoinRoom(code, _lobby.DisplayName,
                string.IsNullOrEmpty(token) ? null : token);
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, $"Joining {code}...", false);
        }

        /// <summary>Remember enough to resume this room after a crash or redeploy.</summary>
        private void SaveResume()
        {
            if (_remote == null || string.IsNullOrEmpty(_remote.Room.Token))
            {
                return;
            }
            PlayerPrefs.SetString(ResumeCodePref, _remote.Room.Code);
            PlayerPrefs.SetString(ResumeTokenPref, _remote.Room.Token);
            PlayerPrefs.Save();
        }

        /// <summary>Lobby refresh and the started transition, driven off the session revision.</summary>
        private void TickLobby()
        {
            if (_remote == null || _screen != Screen.Lobby)
            {
                return;
            }
            if (_remote.Room.Started)
            {
                EnterGame();
                return;
            }
            if (_remote.Revision == _lobbyRevision)
            {
                return;
            }
            _lobbyRevision = _remote.Revision;
            if (_pendingListOnly && _remote.Room.YourSeat < 0)
            {
                // Browsing My Games: keep rendering the list until a room is picked.
                _lobby.ShowGames(_remote.GamesList, JoinFromList);
                return;
            }
            string status = _remote.ConnectionError.Length > 0
                ? _remote.ConnectionError
                : _remote.Log.LastOrDefault(l => l.StartsWith("!"))?.TrimStart('!', ' ') ?? "";
            _lobby.ShowLobby(_remote.Room, status, _remote.MyReady);
            SaveResume();
        }

        /// <summary>Pings the server's /health while the menu is visible: the status dot.</summary>
        private System.Collections.IEnumerator ServerStatusLoop()
        {
            while (true)
            {
                if (_lobby.MenuVisible)
                {
                    string healthUrl = ToHealthUrl(_lobby.ServerUrl);
                    if (healthUrl != null)
                    {
                        using (var request = UnityEngine.Networking.UnityWebRequest.Get(healthUrl))
                        {
                            request.timeout = 3;
                            yield return request.SendWebRequest();
                            bool ok = request.result ==
                                UnityEngine.Networking.UnityWebRequest.Result.Success;
                            _lobby.SetServerStatus(ok, ok ? "" : "server unreachable");
                        }
                    }
                    else
                    {
                        _lobby.SetServerStatus(false, "invalid server url");
                    }
                }
                yield return new WaitForSeconds(3f);
            }
        }

        /// <summary>ws://host:port/ws -> http://host:port/health (and wss -> https).</summary>
        private static string ToHealthUrl(string wsUrl)
        {
            if (!System.Uri.TryCreate(wsUrl, System.UriKind.Absolute, out var uri))
            {
                return null;
            }
            string scheme = uri.Scheme == "wss" ? "https" : "http";
            return $"{scheme}://{uri.Authority}/health";
        }
    }
}
