using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rng;
using Game.Core.Rules;
using Game.Core.Rules.Modules;
using Game.Core.State;

namespace Game.Core.Engine;

/// <summary>
/// The single authoritative implementation of the rules. It has no knowledge of rendering,
/// networking or wall-clock time: commands go in, events come out, and the events are the only
/// thing that mutates <see cref="State"/>.
/// </summary>
public sealed partial class GameEngine
{
    private readonly List<IRuleModule> _modules;
    private List<GameEvent> _out = new();
    private bool _pendingBuy;

    public GameState State { get; }
    public BoardDefinition Board => State.Board;
    public GameRules Rules => State.Rules;
    public IRandomSource Random { get; set; }
    public IReadOnlyList<IRuleModule> Modules => _modules;

    private GameEngine(GameState state)
    {
        State = state;
        Random = new Pcg32(state.Rng);
        _modules = RuleModules.For(state.Rules);
    }

    /// <summary>Builds version 0 of a match. Call <see cref="Start"/> to begin play.</summary>
    public static GameEngine Create(MatchConfig config, BoardDefinition? board = null)
    {
        board ??= BoardLibrary.Get(config.BoardId);
        if (config.Players.Count < 2 || config.Players.Count > 8)
            throw new ArgumentException("A match needs 2 to 8 players");

        var rules = config.Rules;
        var s = new GameState
        {
            MatchId = config.MatchId,
            BoardId = board.BoardId,
            Rules = rules,
            Board = board,
            Rng = Pcg32.Seed(config.Seed),
            Phase = TurnPhase.PreRoll,
            Bank = new BankState { HousesLeft = rules.HouseSupply, HotelsLeft = rules.HotelSupply },
        };
        for (int i = 0; i < config.Players.Count; i++)
        {
            var setup = config.Players[i];
            s.Players.Add(new PlayerState
            {
                Id = i,
                Name = setup.Name,
                Token = setup.Token,
                IsBot = setup.IsBot,
                Money = rules.StartingMoney,
                Team = rules.TeamsEnabled ? (setup.Team >= 0 ? setup.Team : i % 2) : -1,
                Ability = rules.AbilitiesEnabled ? setup.Ability : Ability.None,
            });
        }
        for (int i = 0; i < board.Count; i++)
            s.Properties.Add(board.Tiles[i].IsOwnable ? new PropertyState { Tile = i } : null);
        s.FortuneDeck = Enumerable.Range(0, board.FortuneCards.Count).ToList();
        s.CivicDeck = Enumerable.Range(0, board.CivicCards.Count).ToList();
        return new GameEngine(s);
    }

    /// <summary>Rebuilds an engine around a full (authority) snapshot.</summary>
    public static GameEngine Resume(GameState fullState, BoardDefinition? board = null)
    {
        fullState.Board = board ?? BoardLibrary.Get(fullState.BoardId);
        return new GameEngine(fullState);
    }

    public IReadOnlyList<GameEvent> Start()
    {
        if (State.Version != 0) throw new InvalidOperationException("Match already started");
        _out = new List<GameEvent>();
        Emit(new MatchStarted());
        if (Rules.StartingProperties > 0) DealStartingProperties();
        foreach (var m in _modules) m.OnMatchStarted(this);
        Emit(new TurnStarted(0, 1));
        return _out;
    }

    public CommandResult Execute(GameCommand command)
    {
        if (State.IsOver) return CommandResult.Fail("The match is over");
        if (State.Version == 0) return CommandResult.Fail("The match has not started");
        if (command.ExpectedVersion >= 0 && command.ExpectedVersion != State.Version)
            return CommandResult.Fail("Stale command");
        if (command.PlayerId < 0 || command.PlayerId >= State.Players.Count)
            return CommandResult.Fail("Unknown player");
        if (State.Players[command.PlayerId].Bankrupt)
            return CommandResult.Fail("Bankrupt players cannot act");

        _out = new List<GameEvent>();
        _pendingBuy = false;
        string? error = Dispatch(command);
        if (error != null)
        {
            if (_out.Count > 0) throw new InvalidOperationException($"Command failed after mutating state: {error}");
            return CommandResult.Fail(error);
        }
        if (!State.IsOver)
        {
            foreach (var m in _modules) m.AfterCommand(this);
            CheckGameEnd();
        }
        return CommandResult.Success(_out);
    }

    /// <summary>Authority-side seat change (bot takeover or a returning human); not a player command.</summary>
    public IReadOnlyList<GameEvent> SetBotControl(int player, bool isBot)
    {
        _out = new List<GameEvent>();
        if (State.Players[player].IsBot != isBot) Emit(new PlayerControlChanged(player, isBot));
        return _out;
    }

    public void Emit(GameEvent e)
    {
        e.Version = State.Version + 1;
        e.Apply(State);
        State.Version = e.Version;
        _out.Add(e);
    }

    private string? Dispatch(GameCommand command) => command switch
    {
        RollDiceCommand c => Roll(c),
        EndTurnCommand c => EndTurn(c),
        BuyPropertyCommand c => Buy(c),
        DeclinePropertyCommand c => Decline(c),
        PlaceBidCommand c => PlaceBid(c),
        PassAuctionCommand c => PassAuction(c),
        SubmitSealedBidCommand c => SubmitSealedBid(c),
        BuildHouseCommand c => Build(c),
        SellHouseCommand c => Sell(c),
        MortgageCommand c => Mortgage(c),
        UnmortgageCommand c => Unmortgage(c),
        PayJailFineCommand c => PayJailFine(c),
        UseJailCardCommand c => UseJailCard(c),
        CreateTradeCommand c => CreateTrade(c),
        ModifyTradeCommand c => ModifyTrade(c),
        CounterTradeCommand c => CounterTrade(c),
        AcceptTradeCommand c => AcceptTrade(c),
        RejectTradeCommand c => RejectTrade(c),
        CancelTradeCommand c => CancelTrade(c),
        ExerciseOptionCommand c => ExerciseOption(c),
        UseAbilityCommand c => UseAbility(c),
        TransferMoneyCommand c => TransferMoney(c),
        ContributeToProjectCommand c => Contribute(c),
        DeclareBankruptcyCommand c => DeclareBankruptcy(c),
        _ => "Unsupported command",
    };

    // ------------------------------------------------------------------ phase helpers

    private PlayerState Current => State.Players[State.CurrentPlayer];

    private bool IsCurrent(int player) => State.CurrentPlayer == player;

    private bool InPhase(params TurnPhase[] phases) => Array.IndexOf(phases, State.Phase) >= 0;

    private void SetPhase(TurnPhase phase)
    {
        if (State.Phase != phase) Emit(new PhaseChanged(phase));
    }

    /// <summary>Moves to the next stable phase once the current command has nothing left to resolve.</summary>
    private void SettlePhase()
    {
        if (State.IsOver) return;
        if (State.Debts.Count > 0)
        {
            SetPhase(TurnPhase.DebtResolution);
            return;
        }
        if (State.Auction != null)
        {
            SetPhase(TurnPhase.Auction);
            return;
        }
        if (_pendingBuy)
        {
            SetPhase(TurnPhase.BuyDecision);
            return;
        }
        var cur = Current;
        if (cur.Bankrupt)
        {
            AdvanceTurn();
            return;
        }
        SetPhase(State.DoublesCount > 0 && !cur.InJail ? TurnPhase.PreRoll : TurnPhase.PostRoll);
    }

    /// <summary>Current player may build, unmortgage, contribute or exercise options.</summary>
    private bool CanInvest(int player) => IsCurrent(player) && InPhase(TurnPhase.PreRoll, TurnPhase.PostRoll);

    /// <summary>Player may raise cash by selling buildings or mortgaging.</summary>
    private bool CanLiquidate(int player) =>
        (IsCurrent(player) && InPhase(TurnPhase.PreRoll, TurnPhase.PostRoll, TurnPhase.BuyDecision)) ||
        (State.Phase == TurnPhase.DebtResolution && State.Debts.Any(d => d.Debtor == player));

    // ------------------------------------------------------------------ turn flow

    private string? Roll(RollDiceCommand c)
    {
        if (!IsCurrent(c.PlayerId)) return "Not your turn";
        if (State.Phase != TurnPhase.PreRoll) return "You cannot roll now";
        var p = Current;
        int d1 = Random.Next(6) + 1;
        int d2 = Random.Next(6) + 1;
        bool dbl = d1 == d2;

        if (p.InJail)
        {
            Emit(new DiceRolled(p.Id, d1, d2, 0));
            if (dbl)
            {
                Emit(new PlayerReleasedFromJail(p.Id, JailExit.Doubles));
                MoveBy(p, d1 + d2);
            }
            else
            {
                int attempts = p.JailTurns + 1;
                if (attempts >= Rules.MaxJailTurns)
                {
                    int fine = Board.JailFine;
                    Emit(new PlayerReleasedFromJail(p.Id, JailExit.Fine));
                    if (p.Money >= fine)
                    {
                        Emit(new MoneyTransferred(p.Id, -1, fine, MoneyReason.JailFine));
                        MoveBy(p, d1 + d2);
                    }
                    else
                    {
                        Charge(p.Id, -1, fine, DebtReason.JailFine);
                    }
                }
                else
                {
                    Emit(new JailTurnFailed(p.Id, attempts));
                }
            }
        }
        else
        {
            int count = dbl ? State.DoublesCount + 1 : 0;
            Emit(new DiceRolled(p.Id, d1, d2, count));
            if (count >= 3) SendToJail(p);
            else MoveBy(p, d1 + d2);
        }
        SettlePhase();
        return null;
    }

    private string? EndTurn(EndTurnCommand c)
    {
        if (!IsCurrent(c.PlayerId)) return "Not your turn";
        if (State.Phase != TurnPhase.PostRoll) return "You cannot end your turn yet";
        Emit(new TurnEnded(c.PlayerId));
        AdvanceTurn();
        return null;
    }

    private void AdvanceTurn()
    {
        if (State.IsOver || CheckGameEnd()) return;
        int n = State.Players.Count;
        int cur = State.CurrentPlayer;
        int next = cur;
        do next = (next + 1) % n;
        while (State.Players[next].Bankrupt);

        bool newRound = next <= cur;
        if (newRound)
        {
            int round = State.Round + 1;
            if (Rules.MaximumRounds > 0 && round > Rules.MaximumRounds)
            {
                EndGame(GameEndReason.RoundLimit);
                return;
            }
            Emit(new RoundStarted(round));
        }
        Emit(new TurnStarted(next, State.TurnNumber + 1));
        if (newRound)
        {
            foreach (var m in _modules)
            {
                m.OnRoundStarted(this);
                if (State.IsOver) return;
            }
            if (State.Debts.Count > 0) SetPhase(TurnPhase.DebtResolution);
        }
    }

    // ------------------------------------------------------------------ movement

    private void MoveBy(PlayerState p, int steps)
    {
        int n = Board.Count;
        int from = p.Position;
        int to = (from + steps) % n;
        bool passed = from + steps >= n;
        Emit(new PlayerMoved(p.Id, from, to, MoveKind.Walk, passed));
        if (passed) PaySalary(p, to);
        Land(p, false);
    }

    private void MoveForwardTo(PlayerState p, int to, bool cardBonus)
    {
        int from = p.Position;
        bool passed = to < from;
        Emit(new PlayerMoved(p.Id, from, to, MoveKind.Jump, passed));
        if (passed) PaySalary(p, to);
        Land(p, cardBonus);
    }

    private void MoveBack(PlayerState p, int steps)
    {
        int n = Board.Count;
        int from = p.Position;
        int to = ((from - steps) % n + n) % n;
        Emit(new PlayerMoved(p.Id, from, to, MoveKind.Backward, false));
        Land(p, false);
    }

    private void PaySalary(PlayerState p, int landedOn)
    {
        int amount = Rules.Salary * (landedOn == 0 && Rules.DoubleSalaryOnStart ? 2 : 1);
        if (amount > 0) Emit(new SalaryPaid(p.Id, amount));
    }

    private void SendToJail(PlayerState p)
    {
        int jail = Board.JailIndex;
        Emit(new PlayerSentToJail(p.Id, jail));
    }

    private void Land(PlayerState p, bool cardBonus)
    {
        int tile = p.Position;
        var def = Board.Tiles[tile];
        Emit(new TileLanded(p.Id, tile));
        switch (def.Type)
        {
            case TileType.Street:
            case TileType.Transit:
            case TileType.Utility:
                var prop = State.Properties[tile]!;
                if (prop.Owner < 0)
                {
                    Emit(new PurchaseOffered(p.Id, tile, Calc.PurchasePrice(State, tile)));
                    _pendingBuy = true;
                }
                else if (prop.Owner != p.Id)
                {
                    CollectRent(p, tile, cardBonus);
                }
                break;
            case TileType.Tax:
                Charge(p.Id, -1, Calc.TaxAmount(State, tile), DebtReason.Tax, tile);
                break;
            case TileType.Fortune:
            case TileType.Civic:
                DrawCard(p, def.Type);
                break;
            case TileType.GoToJail:
                SendToJail(p);
                break;
            case TileType.Plaza:
                if (Rules.FreeParkingJackpot && State.Bank.Jackpot > 0)
                    Emit(new MoneyTransferred(-1, p.Id, State.Bank.Jackpot, MoneyReason.Jackpot));
                break;
        }
    }

    private void DrawCard(PlayerState p, TileType deckType)
    {
        var deck = deckType == TileType.Fortune ? State.FortuneDeck : State.CivicDeck;
        var defs = deckType == TileType.Fortune ? Board.FortuneCards : Board.CivicCards;
        int index = deck[Random.Next(deck.Count)];
        var card = defs[index];
        Emit(new CardDrawn(p.Id, deckType, index));
        switch (card.Effect)
        {
            case CardEffect.Collect:
                Emit(new MoneyTransferred(-1, p.Id, card.Amount, MoneyReason.Card));
                break;
            case CardEffect.Pay:
                Charge(p.Id, -1, card.Amount, DebtReason.Card);
                break;
            case CardEffect.MoveTo:
                int target = Board.IndexOfTile(card.Target!);
                if (target != p.Position) MoveForwardTo(p, target, false);
                break;
            case CardEffect.MoveRelative:
                if (card.Amount < 0) MoveBack(p, -card.Amount);
                else if (card.Amount > 0) MoveBy(p, card.Amount);
                break;
            case CardEffect.MoveToNearestTransit:
                MoveToNearest(p, TileType.Transit);
                break;
            case CardEffect.MoveToNearestUtility:
                MoveToNearest(p, TileType.Utility);
                break;
            case CardEffect.GoToJail:
                SendToJail(p);
                break;
            case CardEffect.JailCard:
                Emit(new JailCardReceived(p.Id));
                break;
            case CardEffect.PayPerBuilding:
                int total = 0;
                foreach (var prop in State.OwnedBy(p.Id))
                    total += prop.Level == 5 ? card.Amount2 : prop.Level * card.Amount;
                Charge(p.Id, -1, total, DebtReason.Card);
                break;
            case CardEffect.CollectFromEach:
                foreach (var other in State.Players)
                    if (!other.Bankrupt && other.Id != p.Id) Charge(other.Id, p.Id, card.Amount, DebtReason.Card);
                break;
            case CardEffect.PayEach:
                foreach (var other in State.Players)
                    if (!other.Bankrupt && other.Id != p.Id) Charge(p.Id, other.Id, card.Amount, DebtReason.Card);
                break;
        }
    }

    private void MoveToNearest(PlayerState p, TileType type)
    {
        int n = Board.Count;
        for (int step = 1; step < n; step++)
        {
            int t = (p.Position + step) % n;
            if (Board.Tiles[t].Type != type) continue;
            MoveForwardTo(p, t, true);
            return;
        }
    }

    // ------------------------------------------------------------------ money

    private void CollectRent(PlayerState p, int tile, bool cardBonus)
    {
        var prop = State.Properties[tile]!;
        int owner = prop.Owner;
        if (prop.Mortgaged) return;
        if (!Rules.CollectRentInJail && State.Players[owner].InJail) return;
        if (Rules.TeamsEnabled && Rules.TeamRentExempt && State.SameTeam(p.Id, owner)) return;

        int rent = Calc.Rent(State, tile, State.Die1 + State.Die2, cardBonus);
        if (rent <= 0) return;

        var immunity = State.Contracts.FirstOrDefault(c =>
            c.Kind == ContractKind.RentImmunity && c.Beneficiary == p.Id && c.Grantor == owner &&
            (c.Tile == tile || c.Tile < 0));
        if (immunity != null)
        {
            Emit(new RentWaived(p.Id, tile, immunity.Id, false));
            return;
        }
        if (Rules.AbilitiesEnabled && p.Ability == Ability.Negotiator && p.AbilityArmed)
        {
            Emit(new RentWaived(p.Id, tile, 0, true));
            return;
        }

        var payees = new List<(int Player, int Amount)>();
        if (prop.Shares.Count == 0)
        {
            payees.Add((owner, rent));
        }
        else
        {
            foreach (var sh in prop.Shares)
                if (sh.Player != p.Id && sh.Percent > 0) payees.Add((sh.Player, rent * sh.Percent / 100));
        }
        foreach (var c in State.Contracts)
        {
            if (c.Kind != ContractKind.RevenueShare || c.Tile != tile) continue;
            int i = payees.FindIndex(x => x.Player == c.Grantor);
            if (i < 0) continue;
            int cut = payees[i].Amount * c.Percent / 100;
            payees[i] = (payees[i].Player, payees[i].Amount - cut);
            if (c.Beneficiary != p.Id) payees.Add((c.Beneficiary, cut));
        }
        foreach (var (player, amount) in payees) Charge(p.Id, player, amount, DebtReason.Rent, tile);
    }

    /// <summary>Pays immediately when possible, otherwise queues a debt and enters debt resolution.</summary>
    public void Charge(int debtor, int creditor, int amount, DebtReason reason, int tile = -1, TurnPhase? resume = null)
    {
        if (amount <= 0) return;
        var p = State.Players[debtor];
        if (State.Debts.Count == 0 && p.Money >= amount)
        {
            if (reason == DebtReason.Rent && creditor >= 0)
            {
                Emit(new RentPaid(debtor, creditor, tile, amount));
            }
            else
            {
                var why = reason switch
                {
                    DebtReason.Tax => MoneyReason.Tax,
                    DebtReason.JailFine => MoneyReason.JailFine,
                    DebtReason.Contract => MoneyReason.Contract,
                    _ => MoneyReason.Card,
                };
                Emit(new MoneyTransferred(debtor, creditor, amount, why));
            }
            return;
        }
        Emit(new DebtIncurred(
            new DebtState { Debtor = debtor, Creditor = creditor, Amount = amount, Reason = reason, Tile = tile },
            resume));
    }

    /// <summary>Settles queued debts in order for as long as the debtor at the front can pay.</summary>
    private void TrySettleDebts()
    {
        if (State.Debts.Count == 0) return;
        while (State.Debts.Count > 0)
        {
            var d = State.Debts[0];
            if (State.Players[d.Debtor].Money < d.Amount) break;
            Emit(new DebtPaid(d.Clone()));
        }
        if (State.Debts.Count == 0 && State.Phase == TurnPhase.DebtResolution) ResumeAfterDebts();
    }

    private void ResumeAfterDebts()
    {
        if (CheckGameEnd()) return;
        if (Current.Bankrupt)
        {
            AdvanceTurn();
            return;
        }
        if (State.ResumePhase is { } resume) SetPhase(resume);
        else SettlePhase();
    }

    // ------------------------------------------------------------------ jail

    private string? PayJailFine(PayJailFineCommand c)
    {
        if (!IsCurrent(c.PlayerId) || State.Phase != TurnPhase.PreRoll) return "You cannot do that now";
        var p = Current;
        if (!p.InJail) return "You are not in detention";
        if (p.Money < Board.JailFine) return "Not enough money";
        Emit(new MoneyTransferred(p.Id, -1, Board.JailFine, MoneyReason.JailFine));
        Emit(new PlayerReleasedFromJail(p.Id, JailExit.Fine));
        return null;
    }

    private string? UseJailCard(UseJailCardCommand c)
    {
        if (!IsCurrent(c.PlayerId) || State.Phase != TurnPhase.PreRoll) return "You cannot do that now";
        var p = Current;
        if (!p.InJail) return "You are not in detention";
        if (p.JailCards <= 0) return "You have no card";
        Emit(new PlayerReleasedFromJail(p.Id, JailExit.Card));
        return null;
    }

    // ------------------------------------------------------------------ bankruptcy / game end

    private string? DeclareBankruptcy(DeclareBankruptcyCommand c)
    {
        var debt = State.Debts.FirstOrDefault(d => d.Debtor == c.PlayerId);
        bool resign = IsCurrent(c.PlayerId) && InPhase(TurnPhase.PreRoll, TurnPhase.PostRoll, TurnPhase.BuyDecision);
        if (debt == null && !resign) return "You cannot declare bankruptcy now";
        int creditor = debt?.Creditor ?? -1;
        if (creditor >= 0 && State.Players[creditor].Bankrupt) creditor = -1;

        var before = DistrictOwners();
        Emit(new PlayerBankrupt(c.PlayerId, creditor));
        EmitDistrictChanges(before);
        if (CheckGameEnd()) return null;
        if (Rules.EndOnFirstBankruptcy)
        {
            EndGame(GameEndReason.FirstBankruptcy);
            return null;
        }
        if (State.Debts.Count > 0)
        {
            SetPhase(TurnPhase.DebtResolution);
            TrySettleDebts();
        }
        else if (State.Phase == TurnPhase.DebtResolution)
        {
            ResumeAfterDebts();
        }
        else if (IsCurrent(c.PlayerId))
        {
            AdvanceTurn();
        }
        return null;
    }

    private bool CheckGameEnd()
    {
        if (State.IsOver) return true;
        var active = State.Players.Where(p => !p.Bankrupt).ToList();
        bool over = Rules.TeamsEnabled
            ? active.Select(p => p.Team).Distinct().Count() <= 1
            : active.Count <= 1;
        if (!over) return false;
        EndGame(GameEndReason.LastStanding);
        return true;
    }

    private void EndGame(GameEndReason reason)
    {
        if (State.IsOver) return;
        var standings = Calc.Standings(State);
        int[] winners;
        if (Rules.TeamsEnabled)
        {
            int bestTeam = State.Players
                .GroupBy(p => p.Team)
                .OrderByDescending(g => g.Any(p => !p.Bankrupt) ? 1 : 0)
                .ThenByDescending(g => g.Sum(p => Calc.NetWorth(State, p.Id)))
                .ThenBy(g => g.Key)
                .First().Key;
            winners = State.Players.Where(p => p.Team == bestTeam).Select(p => p.Id).ToArray();
        }
        else if (reason == GameEndReason.LastStanding)
        {
            winners = State.Players.Where(p => !p.Bankrupt).Select(p => p.Id).ToArray();
        }
        else
        {
            int best = standings[0].NetWorth;
            winners = standings
                .Where(x => x.NetWorth == best && !State.Players[x.Player].Bankrupt)
                .Select(x => x.Player).ToArray();
        }
        Emit(new GameEnded(winners, reason, standings.ToArray()));
    }

    // ------------------------------------------------------------------ district tracking

    private int[] DistrictOwners()
    {
        var result = new int[Board.Districts.Count];
        for (int i = 0; i < result.Length; i++)
        {
            int owner = -2;
            foreach (int t in Board.DistrictTiles(Board.Districts[i].Id))
            {
                int o = State.Properties[t]!.Owner;
                if (owner == -2) owner = o;
                else if (owner != o) owner = -1;
            }
            result[i] = owner < 0 ? -1 : owner;
        }
        return result;
    }

    private void EmitDistrictChanges(int[] before)
    {
        var after = DistrictOwners();
        for (int i = 0; i < after.Length; i++)
            if (after[i] >= 0 && after[i] != before[i])
                Emit(new DistrictCompleted(after[i], Board.Districts[i].Id));
    }

    private bool CompletesDistrict(int player, int tile)
    {
        var def = Board.Tiles[tile];
        if (def.Type != TileType.Street) return false;
        foreach (int t in Board.DistrictTiles(def.District))
            if (t != tile && State.Properties[t]!.Owner != player) return false;
        return true;
    }

    private void DealStartingProperties()
    {
        var pool = new List<int>();
        for (int i = 0; i < Board.Count; i++)
            if (Board.Tiles[i].IsOwnable) pool.Add(i);
        for (int round = 0; round < Rules.StartingProperties; round++)
        {
            foreach (var p in State.Players)
            {
                if (pool.Count == 0) return;
                // Nobody should be handed a complete district before the first roll.
                int pick = Random.Next(pool.Count);
                for (int attempt = 0; attempt < 8 && CompletesDistrict(p.Id, pool[pick]); attempt++) pick = Random.Next(pool.Count);
                Emit(new PropertyGranted(p.Id, pool[pick]));
                pool.RemoveAt(pick);
            }
        }
    }
}
