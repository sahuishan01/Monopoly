using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Core.AI;

/// <summary>
/// A bot is just another source of commands: it sees a game state and answers with the same
/// commands a human would send. Difficulty changes decision quality, never the rules.
/// </summary>
public sealed class BotBrain
{
    private readonly IRandomSource _rng;
    private readonly Dictionary<int, int> _askedOnTurn = new();

    public BotLevel Level { get; }

    /// <summary>Monte Carlo budget for <see cref="BotLevel.Expert"/>; 0 disables rollouts.</summary>
    public int Rollouts { get; set; } = 6;

    public BotBrain(BotLevel level, IRandomSource rng)
    {
        Level = level;
        _rng = rng;
    }

    private bool Smart => Level >= BotLevel.Hard;

    private int Rivalry(GameState s) => s.ActivePlayers.Count() <= 2 ? 100 : Level switch
    {
        BotLevel.Easy => 0,
        BotLevel.Medium => 40,
        _ => 60,
    };

    private bool Chance(int percent) => _rng.Next(100) < percent;

    public GameCommand? Decide(GameState s, int me)
    {
        if (s.IsOver || s.Players[me].Bankrupt) return null;

        var incoming = s.Trades.FirstOrDefault(t => t.To == me);
        if (incoming != null) return AnswerTrade(s, me, incoming);

        if (s.Rules.TeamsEnabled)
        {
            var rescue = RescueTeammate(s, me);
            if (rescue != null) return rescue;
        }

        switch (s.Phase)
        {
            case TurnPhase.Auction:
                return s.Auction != null ? DecideAuction(s, me, s.Auction) : null;
            case TurnPhase.DebtResolution:
                return s.Debts.Count > 0 && s.Debts[0].Debtor == me ? ResolveDebt(s, me) : null;
        }

        if (s.CurrentPlayer != me) return null;
        // An offer of ours is waiting for an answer; do not move on until it is resolved.
        if (s.Trades.Any(t => t.From == me)) return null;

        switch (s.Phase)
        {
            case TurnPhase.BuyDecision:
                return DecideBuy(s, me);
            case TurnPhase.PreRoll:
                return DecideJail(s, me) ?? Manage(s, me) ?? new RollDiceCommand(me);
            case TurnPhase.PostRoll:
                return Manage(s, me) ?? new EndTurnCommand(me);
            default:
                return null;
        }
    }

    /// <summary>Safe default for a human who ran out of time: never spends, never trades.</summary>
    public static GameCommand? TimeoutAction(GameState s, int player)
    {
        if (s.IsOver || s.Players[player].Bankrupt) return null;
        var incoming = s.Trades.FirstOrDefault(t => t.To == player);
        if (incoming != null) return new RejectTradeCommand(player, incoming.Id);
        var outgoing = s.Trades.FirstOrDefault(t => t.From == player);
        if (outgoing != null) return new CancelTradeCommand(player, outgoing.Id);

        if (s.Phase == TurnPhase.Auction && s.Auction != null)
        {
            if (!s.Auction.Participants.Contains(player)) return null;
            if (s.Auction.Mode == AuctionMode.Rapid)
                return s.Auction.HasSubmitted(player) ? null : new SubmitSealedBidCommand(player, 0);
            return s.Auction.Passed.Contains(player) || s.Auction.HighBidder == player
                ? null
                : new PassAuctionCommand(player);
        }
        if (s.Phase == TurnPhase.DebtResolution)
            return s.Debts.Count > 0 && s.Debts[0].Debtor == player
                ? new BotBrain(BotLevel.Medium, new Pcg32(1)).ResolveDebt(s, player)
                : null;
        if (s.CurrentPlayer != player) return null;
        return s.Phase switch
        {
            TurnPhase.PreRoll => new RollDiceCommand(player),
            TurnPhase.BuyDecision => new DeclinePropertyCommand(player),
            TurnPhase.PostRoll => new EndTurnCommand(player),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ reserves

    private int Reserve(GameState s, int me)
    {
        switch (Level)
        {
            case BotLevel.Easy:
                return 0;
            case BotLevel.Medium:
                return s.Round > 8 ? 200 : 100;
            default:
                int exposure = Valuation.MaxRentExposure(s, me);
                return Math.Clamp(exposure * 6 / 10, 100, 600);
        }
    }

    // ------------------------------------------------------------------ buying

    private GameCommand DecideBuy(GameState s, int me)
    {
        var p = s.Players[me];
        int tile = p.Position;
        int price = Calc.PurchasePrice(s, tile);
        int value = Valuation.TileValue(s, tile, me);

        if (p.Money < price)
        {
            // Strong bots mortgage loose properties to grab a district-completing tile.
            if (Smart && value >= price * 16 / 10)
            {
                var raise = MortgageCandidate(s, me, onlyLoose: true);
                if (raise != null && Calc.LiquidationValue(s, me) >= price) return raise;
            }
            return new DeclinePropertyCommand(me);
        }

        if (Level == BotLevel.Easy)
            return Chance(75) ? new BuyPropertyCommand(me) : new DeclinePropertyCommand(me);

        int left = p.Money - price;
        bool wants = left >= Reserve(s, me) || value >= price * 15 / 10 || (s.Round <= 6 && left >= 50);
        if (Level == BotLevel.Expert && Rollouts > 0 && !wants && left >= 0)
            wants = MonteCarlo.Prefers(s, me, new BuyPropertyCommand(me), new DeclinePropertyCommand(me), Rollouts, 6, _rng);
        return wants ? new BuyPropertyCommand(me) : new DeclinePropertyCommand(me);
    }

    // ------------------------------------------------------------------ auctions

    private int AuctionCap(GameState s, int me, int tile)
    {
        var p = s.Players[me];
        int value = Valuation.TileValue(s, tile, me);
        int factor = Level switch
        {
            BotLevel.Easy => 55,
            BotLevel.Medium => 85,
            BotLevel.Hard => 100,
            _ => 105,
        };
        int cap = value * factor / 100;
        int spendable = p.Money - Reserve(s, me) / 2;
        return Math.Max(0, Math.Min(cap, spendable));
    }

    private GameCommand? DecideAuction(GameState s, int me, AuctionState a)
    {
        if (!a.Participants.Contains(me)) return null;
        var p = s.Players[me];
        int cap = AuctionCap(s, me, a.Tile);

        if (s.Rules.AbilitiesEnabled && p.Ability == Ability.Investor && !p.AbilityUsed &&
            Valuation.TileValue(s, a.Tile, me) >= s.Board.Tiles[a.Tile].Price * 14 / 10)
            return new UseAbilityCommand(me);

        if (a.Mode == AuctionMode.Rapid)
        {
            if (a.HasSubmitted(me)) return null;
            int bid = cap * (70 + _rng.Next(26)) / 100 / 5 * 5;
            if (bid < s.Rules.AuctionMinIncrement) bid = 0;
            return new SubmitSealedBidCommand(me, Math.Min(bid, p.Money));
        }

        if (a.Passed.Contains(me) || a.HighBidder == me) return null;
        int inc = s.Rules.AuctionMinIncrement;
        int min = a.HighBidder < 0 ? inc : a.HighBid + inc;
        if (min > cap || min > p.Money) return new PassAuctionCommand(me);
        int jump = a.HighBidder < 0 ? cap * 4 / 10 : a.HighBid + Math.Max(inc, (cap - a.HighBid) / 3);
        int amount = Math.Clamp(jump / inc * inc, min, Math.Min(cap, p.Money));
        return new PlaceBidCommand(me, amount);
    }

    // ------------------------------------------------------------------ debts

    private GameCommand? MortgageCandidate(GameState s, int me, bool onlyLoose)
    {
        int best = -1, bestPrice = int.MaxValue;
        foreach (var prop in s.OwnedBy(me))
        {
            if (Calc.MortgageError(s, me, prop.Tile) != null) continue;
            var def = s.Board.Tiles[prop.Tile];
            if (onlyLoose && def.Type == TileType.Street && Calc.OwnsDistrict(s, me, def.District)) continue;
            if (def.Price < bestPrice)
            {
                best = prop.Tile;
                bestPrice = def.Price;
            }
        }
        return best >= 0 ? new MortgageCommand(me, best) : null;
    }

    internal GameCommand ResolveDebt(GameState s, int me)
    {
        var debt = s.Debts[0];
        if (Calc.LiquidationValue(s, me) < debt.Amount) return new DeclareBankruptcyCommand(me);

        var loose = MortgageCandidate(s, me, onlyLoose: true);
        if (loose != null) return loose;

        int sell = -1, sellCost = int.MaxValue;
        foreach (var prop in s.OwnedBy(me))
        {
            if (prop.Level == 0 || Calc.SellError(s, me, prop.Tile) != null) continue;
            int cost = s.Board.Tiles[prop.Tile].HouseCost;
            if (cost < sellCost)
            {
                sell = prop.Tile;
                sellCost = cost;
            }
        }
        if (sell >= 0) return new SellHouseCommand(me, sell);

        return MortgageCandidate(s, me, onlyLoose: false) ?? new DeclareBankruptcyCommand(me);
    }

    private GameCommand? RescueTeammate(GameState s, int me)
    {
        if (s.Phase != TurnPhase.DebtResolution || s.Debts.Count == 0) return null;
        var debt = s.Debts[0];
        if (debt.Debtor == me || !s.SameTeam(me, debt.Debtor)) return null;
        int need = debt.Amount - s.Players[debt.Debtor].Money;
        int spare = s.Players[me].Money - Reserve(s, me);
        return need > 0 && spare >= need ? new TransferMoneyCommand(me, debt.Debtor, need) : null;
    }

    // ------------------------------------------------------------------ jail

    private GameCommand? DecideJail(GameState s, int me)
    {
        var p = s.Players[me];
        if (!p.InJail) return null;
        int developed = 0;
        foreach (var prop in s.Properties)
            if (prop != null && prop.Owner >= 0 && prop.Owner != me && prop.Level >= 3) developed++;
        bool stay = Level >= BotLevel.Medium && developed >= 3;
        if (stay) return new RollDiceCommand(me);
        if (p.JailCards > 0) return new UseJailCardCommand(me);
        if (p.Money >= s.Board.JailFine + Reserve(s, me)) return new PayJailFineCommand(me);
        return new RollDiceCommand(me);
    }

    // ------------------------------------------------------------------ asset management

    private GameCommand? Manage(GameState s, int me)
    {
        var p = s.Players[me];
        int reserve = Reserve(s, me);

        if (s.Rules.AbilitiesEnabled && p.Ability == Ability.Negotiator && !p.AbilityUsed &&
            s.Phase == TurnPhase.PreRoll && Valuation.MaxRentExposure(s, me) >= Math.Max(300, p.Money / 2))
            return new UseAbilityCommand(me);

        if (s.Rules.ContractsEnabled)
        {
            foreach (var c in s.Contracts)
            {
                if (c.Kind != ContractKind.BuyOption || c.Beneficiary != me) continue;
                if (p.Money - c.Amount < reserve) continue;
                if (s.Properties[c.Tile]!.Owner != c.Grantor) continue;
                if (s.Board.Tiles[c.Tile].Type == TileType.Street &&
                    Calc.DistrictHasBuildings(s, s.Board.Tiles[c.Tile].District)) continue;
                if (Valuation.TileValue(s, c.Tile, me) >= c.Amount * 12 / 10) return new ExerciseOptionCommand(me, c.Id);
            }
        }

        if (Level == BotLevel.Easy && !Chance(50)) return null;

        var build = BestBuild(s, me, reserve);
        if (build != null) return build;

        if (Level >= BotLevel.Medium)
        {
            int best = -1, bestScore = 0;
            foreach (var prop in s.OwnedBy(me))
            {
                if (!prop.Mortgaged) continue;
                int cost = Calc.UnmortgageCost(s, prop.Tile, me);
                if (p.Money - cost < reserve + 100) continue;
                var def = s.Board.Tiles[prop.Tile];
                int score = def.Price + (def.Type == TileType.Street && Calc.OwnsDistrict(s, me, def.District) ? 1000 : 0);
                if (score > bestScore)
                {
                    best = prop.Tile;
                    bestScore = score;
                }
            }
            if (best >= 0) return new UnmortgageCommand(me, best);
        }

        if (s.Rules.PublicProjectsEnabled && s.Project != null && Level >= BotLevel.Medium)
        {
            int onSide = s.OwnedBy(me).Count(x => s.Board.SideOf(x.Tile) == s.Project.Side);
            int missing = s.Project.Cost - s.Project.Funded;
            int spare = p.Money - reserve - 200;
            bool alreadyGave = s.Project.Contributions.Any(c => c.Player == me && c.Amount >= s.Project.Cost / 3);
            int amount = Math.Min(missing, Math.Min(spare, 100 * onSide));
            if (onSide >= 2 && amount >= 50 && !alreadyGave) return new ContributeToProjectCommand(me, amount);
        }

        if (s.Rules.TradingEnabled && Level >= BotLevel.Medium && s.Phase == TurnPhase.PostRoll &&
            p.TradeProposalsThisTurn == 0 && Chance(Level == BotLevel.Medium ? 35 : 70))
        {
            // Do not pester the same owner about the same tile turn after turn.
            var offer = TradePlanner.FindProposal(s, me, reserve, Rivalry(s), Smart,
                tile => _askedOnTurn.TryGetValue(tile, out int turn) && s.TurnNumber - turn < 3 * s.Players.Count);
            if (offer != null)
            {
                _askedOnTurn[offer.Receive.Tiles[0]] = s.TurnNumber;
                return new CreateTradeCommand(me, offer.To, offer.Give, offer.Receive);
            }
        }
        return null;
    }

    private GameCommand? BestBuild(GameState s, int me, int reserve)
    {
        var p = s.Players[me];
        int bestTile = -1;
        long bestScore = 0;
        var bestType = DevelopmentType.Residential;
        foreach (var prop in s.OwnedBy(me))
        {
            if (Calc.BuildError(s, me, prop.Tile) != null) continue;
            var def = s.Board.Tiles[prop.Tile];
            var type = prop.Level > 0 ? prop.DevType : PickDevType(s, me);
            int cost = Calc.BuildCost(s, prop.Tile, me, type);
            if (p.Money - cost < reserve) continue;
            // Rent gained per unit of cash; the third building is the sweet spot.
            int gain = def.Rents[prop.Level + 1] - def.Rents[prop.Level];
            long score = gain * 1000L / cost;
            if (score > bestScore)
            {
                bestScore = score;
                bestTile = prop.Tile;
                bestType = type;
            }
        }
        return bestTile >= 0 ? new BuildHouseCommand(me, bestTile, bestType) : null;
    }

    private DevelopmentType PickDevType(GameState s, int me)
    {
        if (!s.Rules.AdvancedDevelopment) return DevelopmentType.Residential;
        if (Level == BotLevel.Easy) return (DevelopmentType)_rng.Next(4);
        int money = s.Players[me].Money;
        if (money > 1200) return Smart && !s.Rules.MarketEventsEnabled ? DevelopmentType.Luxury : DevelopmentType.Commercial;
        if (money < 400) return DevelopmentType.Industrial;
        return DevelopmentType.Residential;
    }

    // ------------------------------------------------------------------ trades

    private GameCommand AnswerTrade(GameState s, int me, TradeOffer offer)
    {
        if (TradeRules.Validate(s, offer.From, offer.To, offer.Give, offer.Receive) != null)
            return new RejectTradeCommand(me, offer.Id);
        int delta = Valuation.TradeDelta(s, offer, me, Rivalry(s));
        int threshold = Level switch
        {
            BotLevel.Easy => 150,
            BotLevel.Medium => 20,
            _ => 40,
        };
        // Never trade away the cash needed to stay solvent.
        int moneyAfter = s.Players[me].Money - offer.Receive.Money + offer.Give.Money;
        if (Level != BotLevel.Easy && moneyAfter < Math.Min(Reserve(s, me), s.Players[me].Money))
            return new RejectTradeCommand(me, offer.Id);

        bool accept = delta >= threshold;
        if (Level == BotLevel.Expert && Rollouts > 0 && Math.Abs(delta - threshold) < 120)
            accept = MonteCarlo.Prefers(s, me, new AcceptTradeCommand(me, offer.Id), new RejectTradeCommand(me, offer.Id), Rollouts, 8, _rng);
        return accept ? new AcceptTradeCommand(me, offer.Id) : new RejectTradeCommand(me, offer.Id);
    }
}
