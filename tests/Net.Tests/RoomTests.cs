using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.Serialization;
using Game.Core.State;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;

namespace Net.Tests;

public class LobbyTests
{
    [Fact]
    public async Task Players_join_get_seats_and_unique_names()
    {
        await using var h = new Harness();
        var a = h.Join("Ishan");
        var b = h.Join("Ishan");
        Assert.Equal(ClientStatus.Lobby, a.Status);
        Assert.True(a.IsHost);
        Assert.False(b.IsHost);
        Assert.Equal(new[] { 0 }, a.Seats);
        Assert.Equal(new[] { 1 }, b.Seats);
        Assert.Equal(new[] { "Ishan", "Ishan 2" }, a.Lobby!.Seats.Select(s => s.Name));
        Assert.NotEqual(a.Lobby.Seats[0].Token, a.Lobby.Seats[1].Token);
    }

    [Fact]
    public async Task Match_needs_two_ready_players_and_only_the_host_starts_it()
    {
        await using var h = new Harness();
        var a = h.Join("A");
        string? failure = null;
        a.CommandFailed += (_, reason) => failure = reason;
        a.StartMatch();
        h.Pump(0.2);
        Assert.Equal("At least two players are needed", failure);

        var b = h.Join("B");
        a.StartMatch();
        h.Pump(0.2);
        Assert.Equal("Not everyone is ready", failure);

        b.StartMatch();
        h.Pump(0.2);
        Assert.Null(h.Host.Room.Engine);

        b.SetReady(true);
        h.Pump(0.2);
        a.StartMatch();
        h.Pump(0.3);
        Assert.NotNull(h.Host.Room.Engine);
        Assert.Equal(ClientStatus.InMatch, a.Status);
        Assert.Equal(ClientStatus.InMatch, b.Status);
        Assert.True(a.Lobby!.InMatch);
    }

    [Fact]
    public async Task Host_controls_settings_bots_and_kicks()
    {
        await using var h = new Harness(o => o.MaxSeats = 4);
        var a = h.Join("A");
        var b = h.Join("B");
        b.UpdateSettings(RulePresets.Get("tycoon"), "pocket_city");
        b.AddBot(BotLevel.Hard);
        h.Pump(0.2);
        Assert.Equal("classic", a.Lobby!.Rules.PresetId);
        Assert.Equal(2, a.Lobby.Seats.Count);

        a.UpdateSettings(RulePresets.Get("tycoon"), "pocket_city", "Ishan's game");
        a.AddBot(BotLevel.Hard);
        a.AddBot(BotLevel.Easy);
        a.AddBot(BotLevel.Easy);
        h.Pump(0.2);
        Assert.Equal("tycoon", b.Lobby!.Rules.PresetId);
        Assert.Equal("pocket_city", b.Lobby.BoardId);
        Assert.Equal("Ishan's game", b.Lobby.RoomName);
        Assert.Equal(4, b.Lobby.Seats.Count);
        Assert.True(b.Lobby.Seats[2].IsBot);

        Reject? rejected = null;
        b.Rejected += r => rejected = r;
        a.Kick(1);
        h.Pump(0.2);
        Assert.Equal(RejectCode.Kicked, rejected!.Code);
        Assert.Equal(3, a.Lobby!.Seats.Count);
        Assert.Equal(new[] { 0, 1, 2 }, a.Lobby.Seats.Select(s => s.Seat));
    }

    [Fact]
    public async Task Full_rooms_and_old_clients_are_rejected()
    {
        await using var h = new Harness(o => o.MaxSeats = 2);
        h.Join("A");
        h.Join("B");
        var c = new RoomClient(h.Hub.CreateClient(), "C");
        Reject? rejected = null;
        c.Rejected += r => rejected = r;
        await c.ConnectAsync();
        h.Clients.Add(c);
        h.Pump(0.2);
        Assert.Equal(RejectCode.RoomFull, rejected!.Code);

        var transport = h.Hub.CreateClient();
        Reject? old = null;
        transport.PacketReceived += p =>
        {
            if (PacketCodec.Decode(p, out _) is Reject r) old = r;
        };
        await transport.ConnectAsync();
        await transport.SendAsync(PacketCodec.Encode(new Hello(0, "Old"), "", 1));
        h.Pump(0.2);
        Assert.Equal(RejectCode.VersionTooOld, old!.Code);
        Assert.Equal(ProtocolInfo.MinimumSupported, old.MinimumVersion);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task Leaving_the_lobby_frees_the_seat_and_passes_host()
    {
        await using var h = new Harness();
        var a = h.Join("A");
        var b = h.Join("B");
        await a.DisconnectAsync();
        h.Pump(0.2);
        Assert.Single(b.Lobby!.Seats);
        Assert.True(b.IsHost);
        Assert.Equal(new[] { 0 }, b.Seats);
    }

    [Fact]
    public async Task One_device_can_hold_several_seats()
    {
        await using var h = new Harness();
        var a = h.Join("A");
        a.AddLocalPlayer("B");
        a.AddLocalPlayer("C");
        h.Pump(0.2);
        Assert.Equal(new[] { 0, 1, 2 }, a.Seats);
        a.Kick(2);
        h.Pump(0.2);
        Assert.Equal(new[] { 0, 1 }, a.Seats);
        h.StartMatch();
        Assert.True(h.PlayToEnd());
        Assert.Equal(h.HostHash, StateHasher.Hash(a.State!));
    }

    [Fact]
    public async Task Chat_is_limited_to_presets()
    {
        await using var h = new Harness();
        var a = h.Join("A");
        var b = h.Join("B");
        var got = new List<(int, int)>();
        b.ChatReceived += (seat, preset) => got.Add((seat, preset));
        a.Chat(3);
        a.Chat(9999);
        h.Pump(0.2);
        Assert.Equal(new[] { (0, 3) }, got);
    }
}

public class MatchTests
{
    [Fact]
    public async Task Humans_and_bots_finish_a_match_with_identical_replicas()
    {
        await using var h = new Harness(o => o.Rules = RulePresets.Get("classic_plus"));
        var a = h.Join("A");
        var b = h.Join("B");
        var spectator = h.Join("Viewer", PeerRole.Spectator);
        a.AddBot(BotLevel.Medium);
        a.AddBot(BotLevel.Hard);
        h.Options.Rules.MaximumRounds = 40;
        a.UpdateSettings(h.Options.Rules, null);
        h.StartMatch();
        Assert.True(h.PlayToEnd());

        string hash = h.HostHash;
        foreach (var c in h.Clients)
        {
            Assert.Equal(hash, StateHasher.Hash(c.State!));
            Assert.Equal(h.Host.Room.Engine!.State.Version, c.State!.Version);
        }
        Assert.Empty(spectator.Seats);
        Assert.All(h.Pilots, p => Assert.True(p.Sent > 0));
        Assert.Equal(h.Host.Room.EventLog.Count, a.EventLog.Count + (a.InitialState!.Version));
    }

    [Fact]
    public async Task Secrets_never_leave_for_other_players()
    {
        await using var h = new Harness(o => o.Rules = RulePresets.Get("classic_plus"));
        var a = h.Join("A");
        var b = h.Join("B");
        var spectator = h.Join("S", PeerRole.Spectator);
        h.StartMatch();

        var real = h.Host.Room.Engine!.State;
        Assert.All(real.Players, p => Assert.All(p.Objectives, o => Assert.True(o.Id >= 0)));
        Assert.Equal(real.Players[0].Objectives.Select(o => o.Id), a.State!.Players[0].Objectives.Select(o => o.Id));
        Assert.All(a.State.Players[1].Objectives, o => Assert.Equal(-1, o.Id));
        Assert.All(b.State!.Players[0].Objectives, o => Assert.Equal(-1, o.Id));
        Assert.All(spectator.State!.Players.SelectMany(p => p.Objectives), o => Assert.Equal(-1, o.Id));
        Assert.Equal(0UL, a.State.Rng.S);

        // Reach a sealed auction and check what each replica knows about the bids.
        foreach (var p in h.Pilots) p.Enabled = false;
        var engine = h.Host.Room.Engine;
        bool auction = h.PumpUntil(() =>
        {
            var s = a.State!;
            if (s.Phase == TurnPhase.Auction) return true;
            var client = s.CurrentPlayer == 0 ? a : b;
            if (client.State!.Version != engine.State.Version) return false;
            GameCommand c = s.Phase switch
            {
                TurnPhase.PreRoll => s.Players[s.CurrentPlayer].InJail && s.Players[s.CurrentPlayer].Money >= 50
                    ? new PayJailFineCommand(s.CurrentPlayer)
                    : new RollDiceCommand(s.CurrentPlayer),
                TurnPhase.BuyDecision => new DeclinePropertyCommand(s.CurrentPlayer),
                _ => new EndTurnCommand(s.CurrentPlayer),
            };
            client.SendCommand(c);
            return false;
        }, 60, 0.1);
        Assert.True(auction);
        a.SendCommand(new SubmitSealedBidCommand(0, 123));
        h.Pump(0.3);
        Assert.Equal(123, a.State!.Auction!.SealedBids.Single().Amount);
        Assert.Equal(-1, b.State!.Auction!.SealedBids.Single().Amount);
        Assert.Equal(-1, spectator.State!.Auction!.SealedBids.Single().Amount);
        Assert.Equal(h.HostHash, StateHasher.Hash(b.State));
    }

    [Fact]
    public async Task Commands_for_someone_elses_seat_are_refused()
    {
        await using var h = new Harness();
        var a = h.Join("A", autopilot: false);
        var b = h.Join("B", autopilot: false);
        var spectator = h.Join("S", PeerRole.Spectator);
        h.StartMatch();
        var failures = new List<string>();
        b.CommandFailed += (_, r) => failures.Add(r);
        spectator.CommandFailed += (_, r) => failures.Add(r);
        b.SendCommand(new RollDiceCommand(0));
        spectator.SendCommand(new RollDiceCommand(0));
        h.Pump(0.3);
        Assert.Equal(new[] { "That seat is not yours", "That seat is not yours" }, failures);
        Assert.Equal(0, h.Host.Room.Engine!.State.Players[0].Stats.Rolls);
    }

    [Fact]
    public async Task Duplicate_and_stale_commands_do_not_double_execute()
    {
        await using var h = new Harness();
        var a = h.Join("A", autopilot: false);
        h.Join("B", autopilot: false);
        h.StartMatch();
        var command = a.SendCommand(new RollDiceCommand(0));
        a.Send(new SubmitCommand(command));
        a.Send(new SubmitCommand(command));
        h.Pump(0.3);
        Assert.Equal(1, h.Host.Room.Engine!.State.Players[0].Stats.Rolls);

        string? failure = null;
        a.CommandFailed += (_, r) => failure = r;
        a.Send(new SubmitCommand(new RollDiceCommand(0) { ExpectedVersion = 1 }));
        h.Pump(0.3);
        Assert.Equal("Stale command", failure);
    }

    [Fact]
    public async Task Turn_timer_moves_an_idle_player_along()
    {
        await using var h = new Harness(o => o.Rules = new GameRules { TurnTimeSeconds = 5, MaximumRounds = 3 });
        var a = h.Join("A", autopilot: false);
        h.Join("B", autopilot: false);
        h.StartMatch();
        h.Pump(2.5);
        Assert.Equal(0, a.TimerSeat);
        Assert.InRange(a.TimerSeconds, 1, 5);
        Assert.True(h.PlayToEnd(400));
        Assert.Equal(GameEndReason.RoundLimit, h.Host.Room.Engine!.State.EndReason);
    }

    [Fact]
    public async Task Rematch_returns_everyone_to_the_lobby()
    {
        await using var h = new Harness(o => o.Rules = new GameRules { MaximumRounds = 2 });
        var a = h.Join("A");
        var b = h.Join("B");
        h.StartMatch();
        Assert.True(h.PlayToEnd());
        string first = a.State!.MatchId;
        bool notified = false;
        b.ReturnedToLobby += () => notified = true;
        a.Send(new RequestRematch());
        h.Pump(3);
        Assert.True(notified);
        Assert.Null(b.State);
        Assert.Equal(ClientStatus.Lobby, b.Status);
        Assert.False(b.Lobby!.Seats[1].Ready);
        h.StartMatch();
        Assert.NotEqual(first, a.State!.MatchId);
        Assert.True(h.PlayToEnd());
    }
}

public class ResilienceTests
{
    [Fact]
    public async Task Reconnecting_with_a_replica_only_replays_missed_events()
    {
        await using var h = new Harness(o => o.Rules = new GameRules { MaximumRounds = 30 });
        var a = h.Join("A");
        var b = h.Join("B");
        a.AddBot(BotLevel.Medium);
        h.StartMatch();
        h.PumpUntil(() => h.Host.Room.Engine!.State.Round >= 3);

        var statuses = new List<PeerStatus>();
        a.PeerStatusChanged += statuses.Add;
        int versionAtDrop = b.State!.Version;
        await b.DisconnectAsync();
        h.Pump(1);
        Assert.Contains(statuses, s => s.Seat == 1 && !s.Connected && s.ReconnectSeconds == 60);
        Assert.False(a.Lobby!.Seats[1].Connected);

        // Same client object reconnects: it still has its replica and resume token.
        int snapshots = 0;
        b.SnapshotLoaded += _ => snapshots++;
        var transport = h.Hub.CreateClient();
        var back = new RoomClient(transport, "B") { ResumeToken = b.ResumeToken };
        typeof(RoomClient).GetProperty(nameof(RoomClient.State))!.SetValue(back, b.State);
        typeof(RoomClient).GetProperty(nameof(RoomClient.Board))!.SetValue(back, b.Board);
        back.SnapshotLoaded += _ => snapshots++;
        await back.ConnectAsync();
        h.Clients[1] = back;
        h.Pilots[1] = new Autopilot(back, 5);
        h.Pump(1);
        Assert.Equal(0, snapshots);
        Assert.Equal(new[] { 1 }, back.Seats);
        Assert.True(back.State!.Version >= versionAtDrop);
        Assert.Contains(statuses, s => s.Seat == 1 && s.Connected);
        Assert.True(h.PlayToEnd());
        Assert.Equal(h.HostHash, StateHasher.Hash(back.State));
    }

    [Fact]
    public async Task Reconnecting_without_a_replica_gets_a_snapshot()
    {
        await using var h = new Harness(o => o.Rules = RulePresets.Get("classic_plus"));
        var a = h.Join("A");
        var b = h.Join("B");
        h.StartMatch();
        h.PumpUntil(() => h.Host.Room.Engine!.State.TurnNumber >= 6);
        string token = b.ResumeToken;
        var mine = b.State!.Players[1].Objectives.Select(o => o.Id).ToArray();
        await b.DisconnectAsync();
        h.Pump(0.5);

        var back = new RoomClient(h.Hub.CreateClient(), "B") { ResumeToken = token };
        await back.ConnectAsync();
        h.Clients[1] = back;
        h.Pilots[1] = new Autopilot(back, 5);
        h.Pump(0.5);
        Assert.Equal(ClientStatus.InMatch, back.Status);
        Assert.Equal(h.HostHash, StateHasher.Hash(back.State!));
        Assert.Equal(mine.Length, back.State!.Players[1].Objectives.Count);
        Assert.All(back.State.Players[1].Objectives, o => Assert.True(o.Id >= 0));
        Assert.All(back.State.Players[0].Objectives.Where(o => !o.Completed), o => Assert.Equal(-1, o.Id));
    }

    [Fact]
    public async Task Strangers_cannot_join_a_running_match()
    {
        await using var h = new Harness();
        h.Join("A");
        h.Join("B");
        h.StartMatch();
        var c = new RoomClient(h.Hub.CreateClient(), "C") { ResumeToken = "seat:1" };
        Reject? rejected = null;
        c.Rejected += r => rejected = r;
        await c.ConnectAsync();
        h.Clients.Add(c);
        h.Pump(0.3);
        Assert.Equal(RejectCode.MatchInProgress, rejected!.Code);
    }

    [Fact]
    public async Task A_bot_takes_over_after_the_reconnect_window_and_hands_back_control()
    {
        await using var h = new Harness(o =>
        {
            o.ReconnectSeconds = 5;
            o.Rules = new GameRules { MaximumRounds = 200 };
        });
        var a = h.Join("A");
        var b = h.Join("B");
        h.StartMatch();
        h.PumpUntil(() => h.Host.Room.Engine!.State.TurnNumber >= 4);
        string token = b.ResumeToken;
        await b.DisconnectAsync();
        h.Pilots[1].Enabled = false;
        h.Pump(8);
        var state = h.Host.Room.Engine!.State;
        Assert.True(state.Players[1].IsBot);
        Assert.True(a.Lobby!.Seats[1].IsBot);
        int turn = state.TurnNumber;
        h.Pump(5);
        Assert.True(h.Host.Room.Engine.State.TurnNumber > turn || h.Host.Room.Engine.State.IsOver);
        if (h.Host.Room.Engine.State.IsOver) return;

        var back = new RoomClient(h.Hub.CreateClient(), "B") { ResumeToken = token };
        await back.ConnectAsync();
        h.Clients[1] = back;
        h.Pilots[1] = new Autopilot(back, 5);
        h.Pump(0.5);
        Assert.False(h.Host.Room.Engine.State.Players[1].IsBot);
        Assert.False(back.State!.Players[1].IsBot);
        Assert.Equal(new[] { 1 }, back.Seats);
        Assert.Contains(h.Host.Room.EventLog.OfType<PlayerControlChanged>(), e => e.Player == 1 && !e.IsBot);
    }

    [Fact]
    public async Task Without_bot_replacement_an_absent_player_forfeits()
    {
        await using var h = new Harness(o =>
        {
            o.ReconnectSeconds = 2;
            o.ReplaceDisconnectedWithBot = false;
        });
        var a = h.Join("A");
        var b = h.Join("B");
        h.StartMatch();
        h.Pump(0.5);
        await b.DisconnectAsync();
        Assert.True(h.PlayToEnd(120));
        Assert.Equal(new[] { 0 }, a.State!.Winners);
    }

    [Theory]
    [InlineData(0.2, 0.0, 0.0, 0.0)]
    [InlineData(0.5, 0.3, 0.0, 0.0)]
    [InlineData(2.0, 0.5, 0.0, 0.0)]
    [InlineData(0.1, 0.4, 0.15, 0.15)]
    public async Task Bad_networks_do_not_break_the_match(double latency, double jitter, double loss, double duplicates)
    {
        await using var h = new Harness(o => o.Rules = new GameRules { MaximumRounds = 12 });
        void Bad(ChaosTransport c)
        {
            c.LatencySeconds = latency;
            c.JitterSeconds = jitter;
            c.LossRate = loss;
            c.DuplicateRate = duplicates;
        }

        var a = h.Join("A");
        var b = h.Join("B", chaos: Bad);
        var c = h.Join("C", chaos: Bad);
        h.Pump(8);
        a.AddBot(BotLevel.Medium);
        Assert.True(h.PumpUntil(() => a.Lobby!.Seats.Count == 4, 60));
        Assert.True(h.PumpUntil(() =>
        {
            if (h.Host.Room.Engine != null) return true;
            b.SetReady(true);
            c.SetReady(true);
            a.StartMatch();
            return false;
        }, 120, 1.0));
        Assert.True(h.PlayToEnd(20_000));
        h.Pump(30);
        string hash = h.HostHash;
        foreach (var client in h.Clients)
        {
            Assert.Equal(h.Host.Room.Engine!.State.Version, client.State!.Version);
            Assert.Equal(hash, StateHasher.Hash(client.State));
        }
        Assert.Equal(0, h.Host.Room.Engine!.State.Players.Count(p => p.IsBot && p.Id < 3));
    }

    [Fact]
    public async Task A_corrupted_replica_is_detected_and_resynced()
    {
        await using var h = new Harness();
        var a = h.Join("A", autopilot: false);
        var b = h.Join("B", autopilot: false);
        h.StartMatch();
        b.State!.Players[0].Money += 500;
        h.Pump(5);
        Assert.True(b.ResyncCount >= 1);
        Assert.Equal(h.HostHash, StateHasher.Hash(b.State!));
    }

    [Fact]
    public async Task Garbage_packets_are_ignored()
    {
        await using var h = new Harness();
        var transport = h.Hub.CreateClient();
        await transport.ConnectAsync();
        await transport.SendAsync(new byte[] { 0, 1, 2, 3 });
        await transport.SendAsync(Array.Empty<byte>());
        await transport.SendAsync(new byte[] { 9, 9 });
        h.Pump(0.2);
        var a = h.Join("A");
        Assert.Equal(ClientStatus.Lobby, a.Status);
    }

    [Fact]
    public async Task A_saved_match_survives_an_authority_restart()
    {
        await using var h = new Harness(o => o.Rules = RulePresets.Get("tycoon"));
        var a = h.Join("A");
        var b = h.Join("B");
        a.AddBot(BotLevel.Medium);
        h.StartMatch();
        h.PumpUntil(() => h.Host.Room.Engine!.State.Round >= 4);

        string json = h.Host.Room.Save()!.ToJson();
        string hashBefore = h.HostHash;
        int version = h.Host.Room.Engine!.State.Version;
        foreach (var p in h.Pilots) p.Enabled = false;
        h.ReplaceHost(room => room.Restore(RoomSave.FromJson(json), allowSeatClaims: true));
        Assert.Equal(hashBefore, h.HostHash);
        Assert.Equal(version, h.Host.Room.Engine!.State.Version);

        var a2 = new RoomClient(h.Hub.CreateClient(), "A") { ResumeToken = "seat:0" };
        var b2 = new RoomClient(h.Hub.CreateClient(), "B") { ResumeToken = "seat:1" };
        await a2.ConnectAsync();
        await b2.ConnectAsync();
        h.Clients.Clear();
        h.Clients.AddRange(new[] { a2, b2 });
        h.Pilots.Clear();
        h.Pilots.Add(new Autopilot(a2, 1));
        h.Pilots.Add(new Autopilot(b2, 2));
        h.Pump(0.5);
        Assert.Equal(new[] { 0 }, a2.Seats);
        Assert.Equal(new[] { 1 }, b2.Seats);
        Assert.True(h.PlayToEnd());
        Assert.Equal(h.HostHash, StateHasher.Hash(a2.State!));
        Assert.Equal(h.HostHash, StateHasher.Hash(b2.State!));
    }

    [Fact]
    public async Task Clients_elect_a_new_host_and_continue_when_the_host_disappears()
    {
        await using var h = new Harness(o => o.Rules = RulePresets.Get("classic_plus"));
        var a = h.Join("A");
        var b = h.Join("B");
        var c = h.Join("C");
        h.StartMatch();
        h.PumpUntil(() => h.Host.Room.Engine!.State.Round >= 3);
        foreach (var p in h.Pilots) p.Enabled = false;
        h.Pump(1);

        // The host device (seat 0) vanishes. Both survivors compute the same successor.
        Assert.Equal(1, HostMigration.ElectSeat(b.Lobby!));
        Assert.True(HostMigration.ShouldBecomeHost(b.Lobby!, b.Seats));
        Assert.False(HostMigration.ShouldBecomeHost(c.Lobby!, c.Seats));
        var bObjectives = b.State!.Players[1].Objectives.Select(o => o.Id).ToArray();
        var cObjectives = c.State!.Players[2].Objectives.Select(o => o.Id).ToArray();
        int version = b.State.Version;

        var save = RoomHost.SaveFromReplica(b.State, b.Board!, HostMigration.LobbyForNewHost(b.Lobby!), b.EventLog, 12345);
        h.Options.ReconnectSeconds = 3;
        h.ReplaceHost(room => room.Restore(save, allowSeatClaims: true));

        var b2 = new RoomClient(h.Hub.CreateClient(), "B") { ResumeToken = HostMigration.SeatClaim(b.Seats) };
        var c2 = new RoomClient(h.Hub.CreateClient(), "C") { ResumeToken = HostMigration.SeatClaim(c.Seats) };
        await b2.ConnectAsync();
        await c2.ConnectAsync();
        h.Clients.Clear();
        h.Clients.AddRange(new[] { b2, c2 });
        h.Pump(0.5);
        b2.Send(new ReportPrivateState(1, bObjectives));
        c2.Send(new ReportPrivateState(2, cObjectives));
        h.Pilots.Clear();
        h.Pilots.Add(new Autopilot(b2, 1));
        h.Pilots.Add(new Autopilot(c2, 2));
        h.Pump(0.5);

        var state = h.Host.Room.Engine!.State;
        Assert.True(state.Version >= version);
        Assert.True(b2.IsHost);
        Assert.Equal(new[] { 1 }, b2.Seats);
        Assert.Equal(new[] { 2 }, c2.Seats);
        Assert.Equal(bObjectives.Where(i => i >= 0), state.Players[1].Objectives.Where(o => !o.Completed).Select(o => o.Id));

        // The lost host's seat is handed to a bot once the reconnect window closes.
        Assert.True(h.PumpUntil(() => h.Host.Room.Engine.State.Players[0].IsBot || h.Host.Room.Engine.State.IsOver, 30));
        Assert.True(h.PlayToEnd());
        Assert.Equal(h.HostHash, StateHasher.Hash(b2.State!));
        Assert.Equal(h.HostHash, StateHasher.Hash(c2.State!));
    }
}

public class TransportTests
{
    [Fact]
    public void Codec_roundtrips_and_compresses_large_packets()
    {
        var small = PacketCodec.Encode(new SetReady(true), "ROOM", 7);
        Assert.Equal(0, small[0]);
        var message = PacketCodec.Decode(small, out var envelope);
        Assert.True(Assert.IsType<SetReady>(message).Ready);
        Assert.Equal("ROOM", envelope.MatchId);
        Assert.Equal(7, envelope.Sequence);
        Assert.Equal(ProtocolInfo.Version, envelope.ProtocolVersion);
        Assert.Equal(nameof(SetReady), envelope.MessageType);

        var lobby = new LobbyInfo { RoomCode = "ROOM" };
        for (int i = 0; i < 8; i++) lobby.Seats.Add(new SeatInfo { Seat = i, Name = "Player " + i });
        var big = PacketCodec.Encode(new LobbyState(lobby), "ROOM", 1);
        Assert.Equal(1, big[0]);
        var back = Assert.IsType<LobbyState>(PacketCodec.Decode(big, out _));
        Assert.Equal(8, back.Lobby.Seats.Count);

        var command = PacketCodec.Encode(new SubmitCommand(new PlaceBidCommand(2, 150)), "ROOM", 1);
        var submit = Assert.IsType<SubmitCommand>(PacketCodec.Decode(command, out _));
        Assert.Equal(150, Assert.IsType<PlaceBidCommand>(submit.Command).Amount);
    }

    [Fact]
    public void Codec_rejects_malformed_input()
    {
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 0 }, out _));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 7, 1, 2 }, out _));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 0, (byte)'{', (byte)'x' }, out _));
        byte[] unknown = System.Text.Encoding.UTF8.GetBytes("\0{\"ProtocolVersion\":1,\"MatchId\":\"\",\"MessageId\":\"a\",\"Sequence\":1,\"MessageType\":\"Nope\",\"Payload\":{}}");
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(unknown, out _));
    }

    [Fact]
    public void Version_negotiation_covers_both_directions()
    {
        Assert.Null(VersionCheck.Validate(ProtocolInfo.Version));
        Assert.Equal(RejectCode.VersionTooOld, VersionCheck.Validate(ProtocolInfo.MinimumSupported - 1)!.Code);
        Assert.Equal(RejectCode.VersionTooNew, VersionCheck.Validate(ProtocolInfo.Version + 1)!.Code);
    }

    [Fact]
    public async Task Lan_transport_carries_a_whole_match_over_real_sockets()
    {
        var options = new RoomHostOptions
        {
            RoomCode = "LAN1", RoomName = "Sockets", BotDelaySeconds = 0, FixedSeed = 5, RapidAuctionSeconds = 0,
            Rules = new GameRules { MaximumRounds = 10 },
        };
        HostSession? session = null;
        var lan = new LanHostTransport(0, () => new LanAdvertisement
        {
            RoomCode = "LAN1", RoomName = "Sockets", HostName = "tester", ProtocolVersion = ProtocolInfo.Version,
            Players = session?.Room.Lobby.Seats.Count ?? 0, MaxPlayers = 6,
        });
        session = new HostSession(options, lan);
        await session.StartAsync();
        Assert.True(lan.Port > 0);

        using var discovery = new LanDiscovery();
        discovery.Start();

        var a = new RoomClient(new LanClientTransport("127.0.0.1", lan.Port), "A");
        var b = new RoomClient(new LanClientTransport("127.0.0.1", lan.Port), "B");
        await a.ConnectAsync();
        await b.ConnectAsync();
        var pilots = new[] { new Autopilot(a, 1), new Autopilot(b, 2) };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        async Task<bool> Until(Func<bool> done, double seconds)
        {
            double end = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < end)
            {
                double now = clock.Elapsed.TotalSeconds;
                session.Pump(now);
                a.Poll(now);
                b.Poll(now);
                foreach (var p in pilots) p.Step(now);
                if (done()) return true;
                await Task.Delay(2);
            }
            return done();
        }

        Assert.True(await Until(() => a.Lobby?.Seats.Count == 2 && b.Lobby?.Seats.Count == 2, 10));
        a.AddBot(BotLevel.Medium);
        b.SetReady(true);
        Assert.True(await Until(() => a.Lobby!.Seats.Count == 3 && a.Lobby.Seats[1].Ready, 10));
        a.StartMatch();
        Assert.True(await Until(() => session.Room.Engine?.State.IsOver == true, 120));
        Assert.True(await Until(() => a.State!.Version == session.Room.Engine!.State.Version && b.State!.Version == a.State.Version, 10));
        string hash = StateHasher.Hash(session.Room.Engine!.State);
        Assert.Equal(hash, StateHasher.Hash(a.State!));
        Assert.Equal(hash, StateHasher.Hash(b.State!));

        // Discovery is best effort (UDP broadcast may be blocked in sandboxes) but must not throw.
        var found = discovery.Rooms.FirstOrDefault(r => r.RoomCode == "LAN1" && r.Port == lan.Port);
        if (found != null) Assert.Equal("Sockets", found.RoomName);

        bool dropped = false;
        a.Disconnected += _ => dropped = true;
        await session.DisposeAsync();
        Assert.True(await Until(() => dropped, 5));
    }
}
