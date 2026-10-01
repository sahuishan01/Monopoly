using System.Text.Json;
using BoardEmpire.Core;
using BoardEmpire.Net;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Protocol;
using Godot;

namespace BoardEmpire.UI;

/// <summary>Account, matchmaking, private rooms, friends and the leaderboard.</summary>
public partial class OnlineScreen : Screen
{
    private VBoxContainer _account = null!;
    private VBoxContainer _play = null!;
    private VBoxContainer _social = null!;
    private string _username = "", _password = "", _code = "", _friend = "";
    private string _server = "";
    private bool _busy;

    public override Screen Recreate() => new OnlineScreen();

    private OnlineApi Api => App.Session.Api;

    protected override void Build()
    {
        _server = Settings.ServerUrl;
        _account = Ui.VBox(10);
        _play = Ui.VBox(10);
        _social = Ui.VBox(8);
        var left = Ui.VBox(14, Ui.Panel(_account), Ui.Panel(_play).Expand(true, true)).Expand(true, true);
        var right = Ui.Panel(Ui.Scroll(_social)).Expand(true, true);
        right.SizeFlagsStretchRatio = 0.85f;
        Page("Online", Ui.HBox(16, left, right));
        Api.Configure(Settings.ServerUrl, Settings.AuthToken);
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        DrawAccount(null, "Connecting…");
        var version = await Api.Version();
        if (!IsInsideTree()) return;
        if (!version.Ok)
        {
            DrawAccount(null, version.Error);
            DrawPlay(false);
            return;
        }
        int minimum = version.Body.GetProperty("minimum").GetInt32(), protocol = version.Body.GetProperty("protocol").GetInt32();
        if (ProtocolInfo.Version < minimum)
        {
            DrawAccount(null, "This version is too old for the server. Please update the game.");
            DrawPlay(false);
            return;
        }
        if (ProtocolInfo.Version > protocol)
        {
            DrawAccount(null, "The server has not been updated to this version yet.");
            DrawPlay(false);
            return;
        }

        ApiResult me = Settings.AuthToken.Length > 0 ? await Api.Me() : new ApiResult(false, default, "");
        if (!me.Ok)
        {
            // First visit (or an expired session): play as a guest right away.
            var guest = await Api.Guest(Settings.PlayerName);
            if (!guest.Ok)
            {
                DrawAccount(null, guest.Error);
                DrawPlay(false);
                return;
            }
            StoreSession(guest.Body, true);
            me = await Api.Me();
        }
        if (!IsInsideTree()) return;
        DrawAccount(me.Ok ? me.Body : null, me.Ok ? "" : me.Error);
        DrawPlay(me.Ok);
        if (me.Ok) await DrawSocial(me.Body);
    }

    private void StoreSession(JsonElement body, bool guest)
    {
        Settings.AuthToken = body.GetProperty("token").GetString() ?? "";
        Settings.UserId = body.GetProperty("userId").GetString() ?? "";
        Settings.AccountIsGuest = guest;
        Settings.Save();
        Api.Configure(Settings.ServerUrl, Settings.AuthToken);
    }

    private void DrawAccount(JsonElement? me, string message)
    {
        foreach (var child in _account.GetChildren()) child.QueueFree();
        _account.AddChild(Ui.Caption("Account", Tokens.Accent));
        if (me is { } body)
        {
            var profile = body.GetProperty("profile");
            bool guest = profile.GetProperty("guest").GetBoolean();
            _account.AddChild(Ui.HBox(10,
                Ui.Label(profile.GetProperty("displayName").GetString() ?? "", 20, Tokens.Text, bold: true).Expand(),
                Ui.Chip(guest ? "GUEST" : $"RATING {profile.GetProperty("rating").GetInt32()}", guest ? Tokens.Muted : Tokens.Accent)));
            if (guest)
            {
                _account.AddChild(Ui.Wrapped("Create an account to play ranked matches and keep your statistics across devices.", 13, Tokens.Muted));
                _account.AddChild(Ui.HBox(8, Ui.Input(_username, "Username", t => _username = t, 20).Expand(), PasswordInput()));
                _account.AddChild(Ui.HBox(8,
                    Ui.Button("Sign in", () => _ = SignAsync(false)).Expand(),
                    Ui.Button("Create account", () => _ = SignAsync(true), ButtonKind.Primary).Expand()));
            }
            else
            {
                _account.AddChild(Ui.Button("Sign out", () =>
                {
                    Settings.AuthToken = "";
                    Settings.UserId = "";
                    Settings.AccountIsGuest = true;
                    Settings.Save();
                    _ = RefreshAsync();
                }, ButtonKind.Ghost));
            }
            if (body.TryGetProperty("invites", out var invites))
            {
                foreach (var invite in invites.EnumerateArray())
                {
                    string code = invite.GetProperty("roomCode").GetString() ?? "";
                    _account.AddChild(Ui.Button($"Join {invite.GetProperty("fromName").GetString()}'s room ({code})", () => _ = JoinAsync(code, false), ButtonKind.Primary));
                }
            }
        }
        else
        {
            _account.AddChild(Ui.Wrapped(message, 15, Tokens.Muted));
            _account.AddChild(Row("Server", Ui.Input(_server, "https://…", t => _server = t, 120)));
            _account.AddChild(Ui.Button("Connect", () =>
            {
                Settings.ServerUrl = _server.Trim();
                Settings.Save();
                Api.Configure(Settings.ServerUrl, Settings.AuthToken);
                _ = RefreshAsync();
            }, ButtonKind.Primary));
        }
    }

    private LineEdit PasswordInput()
    {
        var edit = Ui.Input(_password, "Password", t => _password = t, 64);
        edit.Secret = true;
        edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return edit;
    }

    private async Task SignAsync(bool register)
    {
        if (_busy) return;
        _busy = true;
        var result = register ? await Api.Register(_username.Trim(), _password, Settings.PlayerName) : await Api.Login(_username.Trim(), _password);
        _busy = false;
        if (!result.Ok)
        {
            App.Toast(result.Error, error: true);
            return;
        }
        _password = "";
        StoreSession(result.Body, false);
        await RefreshAsync();
    }

    private void DrawPlay(bool online)
    {
        foreach (var child in _play.GetChildren()) child.QueueFree();
        _play.AddChild(Ui.Caption("Play", Tokens.Accent));
        var quick = Ui.Button(Loc.T("Quick Match"), () => _ = MatchAsync(false), ButtonKind.Primary);
        var ranked = Ui.Button(Loc.T("Ranked"), () => _ = MatchAsync(true));
        var room = Ui.Button(Loc.T("Private Room"), () => App.Go(new SetupScreen(SetupMode.OnlinePrivate)));
        ranked.Disabled = Settings.AccountIsGuest;
        if (Settings.AccountIsGuest) ranked.TooltipText = "Ranked play needs an account";
        quick.Disabled |= !online;
        ranked.Disabled |= !online;
        room.Disabled = !online;
        _play.AddChild(quick);
        _play.AddChild(ranked);
        _play.AddChild(room);
        _play.AddChild(Ui.Caption(Loc.T("Join Code")));
        var code = Ui.Input(_code, "ABC123", t => _code = t, 8);
        var join = Ui.Button("Join", () => _ = JoinAsync(_code, false), ButtonKind.Primary);
        var watch = Ui.Button("Watch", () => _ = JoinAsync(_code, true));
        join.Disabled = watch.Disabled = !online;
        _play.AddChild(Ui.HBox(8, code.Expand(), join, watch));
    }

    private async Task MatchAsync(bool ranked)
    {
        if (_busy) return;
        _busy = true;
        var result = await Api.Matchmaking(ranked, ranked ? "classic_plus" : "quick");
        _busy = false;
        if (!result.Ok)
        {
            App.Toast(result.Error, error: true);
            return;
        }
        await JoinAsync(result.Text("code"), false);
    }

    private async Task JoinAsync(string code, bool spectate)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length == 0) return;
        var room = await Api.Room(code);
        if (!room.Ok)
        {
            App.Toast(room.Error.Length > 0 ? room.Error : "Room not found", error: true);
            return;
        }
        if (await App.Session.JoinOnlineAsync(code, spectate) && IsInsideTree())
        {
            // Matchmaking rooms start on their own once everyone is ready.
            if (!spectate) App.Session.Client?.SetReady(true);
            App.Go(new LobbyScreen());
        }
    }

    private async Task DrawSocial(JsonElement me)
    {
        var friends = await Api.Friends();
        var board = await Api.Leaderboard();
        var recent = await Api.RecentPlayers();
        var matches = await Api.RecentMatches();
        if (!IsInsideTree()) return;
        foreach (var child in _social.GetChildren()) child.QueueFree();

        _social.AddChild(Ui.Caption("Friends", Tokens.Accent));
        _social.AddChild(Ui.HBox(8, Ui.Input(_friend, "Add by username", t => _friend = t, 20).Expand(),
            Ui.Button("Add", async () =>
            {
                var added = await Api.AddFriend(_friend.Trim());
                App.Toast(added.Ok ? "Friend added" : added.Error, !added.Ok);
                if (added.Ok) await DrawSocial(me);
            })));
        if (friends.Ok)
        {
            foreach (var f in friends.Body.EnumerateArray())
            {
                bool online = f.GetProperty("online").GetBoolean();
                string room = f.GetProperty("room").GetString() ?? "";
                string id = f.GetProperty("userId").GetString() ?? "";
                var row = Ui.HBox(8, Ui.Label(f.GetProperty("displayName").GetString() ?? "", 16).Expand(),
                    Ui.Chip(online ? "ONLINE" : "OFFLINE", online ? Tokens.Good : Tokens.Muted));
                if (online && room.Length > 0) row.AddChild(Ui.Button("Join", () => _ = JoinAsync(room, false), ButtonKind.Ghost));
                row.AddChild(Ui.Button("✕", async () =>
                {
                    await Api.RemoveFriend(id);
                    await DrawSocial(me);
                }, ButtonKind.Ghost));
                _social.AddChild(row);
            }
            if (friends.Body.GetArrayLength() == 0) _social.AddChild(Ui.Label("No friends yet.", 14, Tokens.Muted));
        }

        if (recent.Ok && recent.Body.GetArrayLength() > 0)
        {
            _social.AddChild(Ui.Caption("Recent players", Tokens.Accent));
            foreach (var p in recent.Body.EnumerateArray().Take(6))
            {
                string id = p.GetProperty("userId").GetString() ?? "";
                _social.AddChild(Ui.HBox(8, Ui.Label(p.GetProperty("displayName").GetString() ?? "", 15).Expand(),
                    Ui.Button("Add friend", async () =>
                    {
                        var added = await Api.AddFriendById(id);
                        App.Toast(added.Ok ? "Friend added" : added.Error, !added.Ok);
                        if (added.Ok) await DrawSocial(me);
                    }, ButtonKind.Ghost)));
            }
        }

        _social.AddChild(Ui.Caption("Leaderboard", Tokens.Accent));
        if (board.Ok)
        {
            foreach (var row in board.Body.EnumerateArray().Take(10))
                _social.AddChild(Ui.HBox(8,
                    Ui.Label($"#{row.GetProperty("rank").GetInt32()}", 14, Tokens.Muted, mono: true).MinSize(40, 0),
                    Ui.Label(row.GetProperty("displayName").GetString() ?? "", 15).Expand(),
                    Ui.Label(row.GetProperty("rating").GetInt32().ToString(), 15, Tokens.Accent, mono: true, bold: true)));
            if (board.Body.GetArrayLength() == 0) _social.AddChild(Ui.Label("No ranked matches have been played yet.", 14, Tokens.Muted));
        }

        if (matches.Ok && matches.Body.GetArrayLength() > 0)
        {
            _social.AddChild(Ui.Caption("Your recent matches", Tokens.Accent));
            foreach (var m in matches.Body.EnumerateArray().Take(6))
            {
                string matchId = m.GetProperty("matchId").GetString() ?? "";
                var mine = m.GetProperty("players").EnumerateArray().FirstOrDefault(p => p.GetProperty("userId").GetString() == Settings.UserId);
                string rank = mine.ValueKind == JsonValueKind.Object ? $"#{mine.GetProperty("rank").GetInt32()}" : "";
                _social.AddChild(Ui.HBox(8,
                    Ui.Label($"{m.GetProperty("preset").GetString()} · {m.GetProperty("turns").GetInt32()} turns", 14, Tokens.Text).Expand(),
                    Ui.Label(rank, 14, Tokens.Accent, mono: true),
                    Ui.Button("Replay", async () =>
                    {
                        string? json = await Api.Replay(matchId);
                        if (json == null)
                        {
                            App.Toast("Replay unavailable", error: true);
                            return;
                        }
                        App.Go(new BoardScreen(ReplayFile.FromJson(json)));
                    }, ButtonKind.Ghost)));
            }
        }
    }
}
