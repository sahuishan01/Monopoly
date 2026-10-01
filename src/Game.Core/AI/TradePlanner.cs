using Game.Core.Board;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.AI;

/// <summary>Finds a district-completing trade that both sides should find acceptable.</summary>
public static class TradePlanner
{
    private const int TheirMargin = 40;
    private const int MyMargin = 30;

    public static TradeOffer? FindProposal(GameState s, int me, int reserve, int rivalry, bool smart,
        Func<int, bool>? skipTile = null)
    {
        var p = s.Players[me];
        int budget = Math.Max(0, p.Money - reserve);
        TradeOffer? best = null;
        int bestGain = 0;

        foreach (var d in s.Board.Districts)
        {
            var tiles = s.Board.DistrictTiles(d.Id);
            int mine = 0, owner = -1;
            bool viable = true;
            var wanted = new List<int>();
            foreach (int t in tiles)
            {
                var prop = s.Properties[t]!;
                if (prop.Owner == me)
                {
                    mine++;
                    continue;
                }
                if (prop.Owner < 0 || prop.Level > 0 || (owner >= 0 && owner != prop.Owner))
                {
                    viable = false;
                    break;
                }
                owner = prop.Owner;
                wanted.Add(t);
            }
            if (!viable || mine == 0 || wanted.Count == 0 || wanted.Count > (smart ? 2 : 1)) continue;
            if (s.Rules.TeamsEnabled && s.SameTeam(me, owner)) continue;
            if (Calc.DistrictHasBuildings(s, d.Id)) continue;
            if (skipTile != null && skipTile(wanted[0])) continue;

            foreach (var give in SwapCandidates(s, me, owner, d.Id, smart))
            {
                var offer = new TradeOffer { From = me, To = owner };
                offer.Give.Tiles.AddRange(give);
                offer.Receive.Tiles.AddRange(wanted);
                if (TradeRules.Validate(s, me, owner, offer.Give, offer.Receive) != null) continue;
                var (mineRaw, theirsRaw) = Valuation.TradeRawDeltas(s, offer);

                // Cash moves value one-for-one, so the balancing payment is solved directly:
                // positive = we pay them, negative = they pay us.
                int cash = SolveCash(mineRaw, theirsRaw, rivalry, budget, s.Players[owner].Money);
                if (cash == int.MinValue) continue;
                int gain = Adjusted(mineRaw - cash, theirsRaw + cash, rivalry);
                if (gain <= bestGain) continue;
                if (cash >= 0) offer.Give.Money = cash;
                else offer.Receive.Money = -cash;
                best = offer;
                bestGain = gain;
            }

            if (best == null && smart && s.Rules.ContractsEnabled && budget >= 100)
            {
                // Short on cash: pay part now and the rest in instalments.
                var offer = new TradeOffer { From = me, To = owner };
                offer.Give.Money = budget / 2;
                offer.Give.Terms.Add(new ContractTerm { Kind = ContractKind.Installment, Amount = Math.Max(20, budget / 4), Count = 4 });
                offer.Receive.Tiles.AddRange(wanted);
                if (TradeRules.Validate(s, me, owner, offer.Give, offer.Receive) == null)
                {
                    var (mineRaw, theirsRaw) = Valuation.TradeRawDeltas(s, offer);
                    int gain = Adjusted(mineRaw, theirsRaw, rivalry);
                    if (Adjusted(theirsRaw, mineRaw, rivalry) >= TheirMargin && gain >= MyMargin && gain > bestGain)
                    {
                        best = offer;
                        bestGain = gain;
                    }
                }
            }
        }
        return best;
    }

    private static int Adjusted(int own, int other, int rivalry) => own - other * rivalry / 100;

    /// <summary>
    /// Smallest payment (in steps of 10) that satisfies the other side while keeping our own
    /// margin, or int.MinValue when no payment works.
    /// </summary>
    private static int SolveCash(int mineRaw, int theirsRaw, int rivalry, int myBudget, int theirMoney)
    {
        for (int cash = -Math.Min(theirMoney, 600) / 10 * 10; cash <= myBudget; cash += 10)
        {
            if (Adjusted(theirsRaw + cash, mineRaw - cash, rivalry) < TheirMargin) continue;
            return Adjusted(mineRaw - cash, theirsRaw + cash, rivalry) >= MyMargin ? cash : int.MinValue;
        }
        return int.MinValue;
    }

    private static IEnumerable<List<int>> SwapCandidates(GameState s, int me, int other, string targetDistrict, bool smart)
    {
        yield return new List<int>();
        int yielded = 0;
        foreach (var prop in s.OwnedBy(me).ToList())
        {
            var def = s.Board.Tiles[prop.Tile];
            if (def.District == targetDistrict) continue;
            if (def.Type == TileType.Street)
            {
                if (Calc.DistrictHasBuildings(s, def.District)) continue;
                if (Calc.OwnsDistrict(s, me, def.District)) continue;
                bool theyHaveSome = s.Board.DistrictTiles(def.District).Any(t => s.Properties[t]!.Owner == other);
                if (!theyHaveSome) continue;
            }
            else if (!smart)
            {
                continue;
            }
            yield return new List<int> { prop.Tile };
            if (++yielded >= 4) yield break;
        }
    }
}
