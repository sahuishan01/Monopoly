using Game.Protocol;

namespace Game.Net;

/// <summary>
/// Deterministic host election for local (LAN / Nearby) matches. Every surviving client runs the
/// same computation on the last lobby it saw, so they agree on the new authority without talking.
/// </summary>
public static class HostMigration
{
    /// <summary>The seat whose device takes over: the lowest connected human seat not owned by the lost host.</summary>
    public static int ElectSeat(LobbyInfo lobby)
    {
        foreach (var seat in lobby.Seats.OrderBy(s => s.Seat))
        {
            if (seat.IsBot || !seat.Connected) continue;
            if (seat.PeerId == lobby.HostPeerId) continue;
            return seat.Seat;
        }
        return -1;
    }

    public static bool ShouldBecomeHost(LobbyInfo lobby, IReadOnlyCollection<int> mySeats)
    {
        int elected = ElectSeat(lobby);
        return elected >= 0 && mySeats.Contains(elected);
    }

    /// <summary>Lobby as the new host should see it: every remote human is waiting to reconnect.</summary>
    public static LobbyInfo LobbyForNewHost(LobbyInfo lobby)
    {
        var copy = new LobbyInfo
        {
            RoomCode = lobby.RoomCode, RoomName = lobby.RoomName, Revision = lobby.Revision, HostPeerId = "",
            BoardId = lobby.BoardId, Rules = lobby.Rules, InMatch = true, Ranked = lobby.Ranked,
        };
        foreach (var seat in lobby.Seats)
        {
            var s = seat.Clone();
            if (!s.IsBot)
            {
                s.Connected = false;
                s.PeerId = "";
            }
            copy.Seats.Add(s);
        }
        return copy;
    }

    public static string SeatClaim(IEnumerable<int> seats) => "seat:" + string.Join(',', seats);
}
