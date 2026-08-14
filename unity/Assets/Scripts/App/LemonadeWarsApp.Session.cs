using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LemonadeWars.Unity
{
    /// <summary>Session lifecycle: solo game start, server connect/join/resume, lobby
    /// ticking, game speed, and the return trip to the menu.</summary>
    public sealed partial class LemonadeWarsApp
    {
        // Lobby verb deferred until the socket opens.
        private bool _pendingSend;
        private bool _pendingCreate;
        private bool _pendingListOnly;
        private string _pendingName;
        private string _pendingCode;
        private string _pendingToken;
        private float _pendingSince;
        private const float ConnectTimeoutSeconds = 10f;

        private float _lastGamesRefresh;
        private int _lobbyRevision = -1;

        private IReadOnlyList<(string Name, string Level)> _soloBots =
            new[] { ("Benny", "medium"), ("Cleo", "medium"), ("Dex", "medium") };

        // ------------------------------------------------------- game speed

        private const string SpeedPref = "lw_game_speed";
        private static readonly string[] SpeedOrder = { "slow", "medium", "fast" };

        private static float LocalStepSeconds(string speed) =>
            speed == "slow" ? 2.0f : speed == "fast" ? 0.45f : 1.0f;

        /// <summary>Local games follow the saved preference; online rooms follow the server.</summary>
        private string CurrentSpeed() =>
            _remote != null && _screen == Screen.Game
                ? (string.IsNullOrEmpty(_remote.Room.Speed) ? "medium" : _remote.Room.Speed)
                : PlayerPrefs.GetString(SpeedPref, "medium");

        private void CycleSpeed()
        {
            string next = SpeedOrder[
                (System.Array.IndexOf(SpeedOrder, CurrentSpeed()) + 1) % SpeedOrder.Length];
            PlayerPrefs.SetString(SpeedPref, next);
            PlayerPrefs.Save();
            Sfx.Play(Sfx.BotSpeed(next));
            if (_session is LocalGameSession local)
            {
                local.BotStepSeconds = LocalStepSeconds(next);
            }
            else
            {
                _remote?.SetSpeed(next); // the room broadcast confirms + syncs everyone
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

        // ------------------------------------------------------------ flows

        /// <summary>
        /// Default server URL from StreamingAssets/client-config.json (gitignored; written
        /// by tools/sync_unity.sh, baked into builds, editable post-build). The last URL a
        /// player actually connected to takes precedence.
        /// </summary>
        private static string LoadConfiguredServerUrl()
        {
            string remembered = PlayerPrefs.GetString("lw_server", "");
            if (!string.IsNullOrEmpty(remembered))
            {
                return remembered;
            }
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

        private void StartLocalGame(IReadOnlyList<(string Name, string Level)> bots)
        {
            _soloBots = bots.ToList();
            var names = new List<string> { _lobby.DisplayName };
            names.AddRange(_soloBots.Select(b => b.Name));

            _remote?.Dispose();
            _remote = null;
            _session?.Dispose();
            var localSession = new LocalGameSession(_db, names.ToArray(), 0,
                (ulong)System.DateTime.Now.Ticks,
                _soloBots.Select(b => b.Level).ToList());
            localSession.BotStepSeconds = LocalStepSeconds(PlayerPrefs.GetString(SpeedPref, "medium"));
            _session = localSession;
            _session.EventEmitted += OnGameEvent;
            EnterGame();
        }

        /// <summary>Durable identity secret; generated once and never shown anywhere.</summary>
        private static string PlayerKey
        {
            get
            {
                string key = PlayerPrefs.GetString("lw_player_key", "");
                if (string.IsNullOrEmpty(key))
                {
                    key = System.Guid.NewGuid().ToString("N") + System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("lw_player_key", key);
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
            PlayerPrefs.SetString("lw_server", url);
            PlayerPrefs.SetString("lw_name", name);
            _screen = Screen.Lobby;
            _lobbyRevision = -1;
            _lobby.ShowLobby(_remote.Room, "Connecting to " + url + "...", false);
        }

        /// <summary>Deferred lobby verb once the socket is open (or gave up).</summary>
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
                // A dead host can hang ConnectAsync with no error for minutes —
                // give up cleanly instead of trapping the player on CONNECTING.
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

        /// <summary>A My Games row was picked: enter that room on the same connection.</summary>
        private void JoinFromList(string code)
        {
            if (_remote == null)
            {
                return;
            }
            // The stored per-room token still helps pre-identity games; identity
            // covers everything created from now on.
            string token = PlayerPrefs.GetString("lw_code", "") == code
                ? PlayerPrefs.GetString("lw_token", "")
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
            PlayerPrefs.SetString("lw_code", _remote.Room.Code);
            PlayerPrefs.SetString("lw_token", _remote.Room.Token);
            PlayerPrefs.Save();
        }

        /// <summary>Lobby refresh + started transition, driven off the session revision.</summary>
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
                : _remote.Log.LastOrDefault(l => l.StartsWith("!")) ?? "";
            _lobby.ShowLobby(_remote.Room, status, _remote.MyReady);
            SaveResume();
        }

        private void EnterGame()
        {
            _screen = Screen.Game;
            SaveResume();
            _lobby.HideAll();
            _table.SetVisible(true);
            _prompt.Hide();
            _picker.Hide();
            _reaction.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _fx.Clear();
            _table.ViewedBoardPlayer = -1;
            _table.SuppressWhiniestBaby = false; // a dropped mid-flight claim must not hide it forever
            _table.SuppressSpoiledRotten = false;
            // Same law for the shelf: stale holds from a previous game would keep
            // market slots empty forever (ids can collide across games), and a stale
            // snapshot would diff garbage.
            _hiddenMarketIds.Clear();
            _marketSnap = null;
            _supplyTopSnap = null;
            _lastSpenderId = -1;
            _renderedRevision = -1;
            _modalRevision = -1;
            _wasMyTurn = false;
            _actionLog.Clear();
            _drawRunText = null;
            _saleEarnings = null;
            _endShown = false;
        }

        private void BackToMenu(string status)
        {
            _session?.Dispose();
            _session = null;
            _remote = null;
            _screen = Screen.Menu;
            UiKit.Clear(_turnActionRow);
            _table.SetVisible(false);
            _prompt.Hide();
            _picker.Hide();
            _turnBanner.Hide();
            _dice.Clear();
            _fx.Clear();
            _lobby.ShowMenu(status);
        }

        /// <summary>Ping the server's /health while the menu is visible: the status dot.</summary>
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
