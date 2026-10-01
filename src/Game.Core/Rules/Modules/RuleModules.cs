using Game.Core.Board;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Rules.Modules;

/// <summary>
/// An optional rules package. Modes are compositions of modules selected by
/// <see cref="GameRules"/>, never forks of the engine.
/// </summary>
public interface IRuleModule
{
    string Id { get; }

    void OnMatchStarted(GameEngine engine) { }

    /// <summary>Called after the first turn of a new round has started.</summary>
    void OnRoundStarted(GameEngine engine) { }

    void AfterCommand(GameEngine engine) { }
}

public static class RuleModules
{
    public static List<IRuleModule> For(GameRules rules)
    {
        var list = new List<IRuleModule> { new ClassicRules() };
        if (rules.AbilitiesEnabled) list.Add(new AbilityRuleModule());
        if (rules.PropertySharesEnabled) list.Add(new ShareOwnershipModule());
        if (rules.ContractsEnabled) list.Add(new ContractRuleModule());
        if (rules.MarketEventsEnabled) list.Add(new MarketRuleModule());
        if (rules.PublicProjectsEnabled) list.Add(new PublicProjectModule());
        if (rules.SecretObjectivesEnabled) list.Add(new MissionRuleModule());
        return list;
    }
}

/// <summary>The base property-trading ruleset implemented by the engine itself.</summary>
public sealed class ClassicRules : IRuleModule
{
    public string Id => "classic";
}

/// <summary>Fractional ownership. Rent splitting and control live in the shared reducers.</summary>
public sealed class ShareOwnershipModule : IRuleModule
{
    public string Id => "shares";
}

public sealed class AbilityRuleModule : IRuleModule
{
    private static readonly Ability[] Pool = { Ability.Investor, Ability.Builder, Ability.Negotiator, Ability.Banker };

    public string Id => "abilities";

    public void OnMatchStarted(GameEngine engine)
    {
        foreach (var p in engine.State.Players)
            if (p.Ability == Ability.None)
                engine.Emit(new AbilityAssigned(p.Id, Pool[engine.Random.Next(Pool.Length)]));
    }
}

public sealed class ContractRuleModule : IRuleModule
{
    public string Id => "contracts";

    public void OnRoundStarted(GameEngine engine) => engine.TickContracts();
}

public sealed class MissionRuleModule : IRuleModule
{
    public string Id => "missions";

    public void OnMatchStarted(GameEngine engine)
    {
        var s = engine.State;
        int transit = s.Board.TilesOfType(TileType.Transit).Count();
        foreach (var p in s.Players)
        {
            var pool = ObjectiveCatalog.All
                .Where(o => o.Kind != ObjectiveKind.OwnTransits || transit >= o.Target)
                .Where(o => o.Kind != ObjectiveKind.CompleteTrades || s.Rules.TradingEnabled)
                .Where(o => o.Kind != ObjectiveKind.WinAuctions || s.Rules.AuctionsEnabled)
                .Select(o => o.Id).ToList();
            var ids = new List<int>();
            for (int i = 0; i < s.Rules.ObjectivesPerPlayer && pool.Count > 0; i++)
            {
                int pick = engine.Random.Next(pool.Count);
                ids.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            engine.Emit(new ObjectivesAssigned(p.Id, ids.ToArray()));
        }
    }

    public void AfterCommand(GameEngine engine)
    {
        var s = engine.State;
        foreach (var p in s.Players)
        {
            if (p.Bankrupt) continue;
            for (int slot = 0; slot < p.Objectives.Count; slot++)
            {
                var o = p.Objectives[slot];
                if (o.Completed || o.Id < 0) continue;
                var def = ObjectiveCatalog.Get(o.Id);
                if (ObjectiveCatalog.IsSatisfied(s, p.Id, def))
                    engine.Emit(new ObjectiveCompleted(p.Id, slot, def.Id, def.Reward));
            }
        }
    }
}

public sealed class MarketRuleModule : IRuleModule
{
    public const int EconomyPeriod = 4;
    public const int EventPeriod = 3;

    private static readonly (EconomyPhase To, int Weight)[][] Transitions =
    {
        /* Stable    */ new[] { (EconomyPhase.Boom, 3), (EconomyPhase.Recession, 2), (EconomyPhase.Bubble, 1), (EconomyPhase.Stable, 2) },
        /* Boom      */ new[] { (EconomyPhase.Bubble, 3), (EconomyPhase.Stable, 3), (EconomyPhase.Recession, 1) },
        /* Recession */ new[] { (EconomyPhase.Recovery, 4), (EconomyPhase.Recession, 1) },
        /* Bubble    */ new[] { (EconomyPhase.Recession, 4), (EconomyPhase.Stable, 1) },
        /* Recovery  */ new[] { (EconomyPhase.Stable, 3), (EconomyPhase.Boom, 2) },
    };

    public string Id => "market";

    public static ActiveEffect? EconomyEffect(EconomyPhase phase) => phase switch
    {
        EconomyPhase.Boom => Economy("Boom", "Boom: rents +20%, prices +10%", 20, 10, 0),
        EconomyPhase.Recession => Economy("Recession", "Recession: rents -20%, prices -15%", -20, -15, 0),
        EconomyPhase.Bubble => Economy("Property Bubble", "Bubble: prices +35%, rents +10%", 10, 35, 0),
        EconomyPhase.Recovery => Economy("Recovery", "Recovery: prices -10%, construction -15%", 0, -10, -15),
        _ => null,
    };

    private static ActiveEffect Economy(string name, string description, int rent, int price, int build) => new()
    {
        Source = "economy", Name = name, Description = description, Scope = EffectScope.All,
        RentPercent = rent, PricePercent = price, BuildCostPercent = build, RoundsLeft = -1,
    };

    public void OnRoundStarted(GameEngine engine)
    {
        var s = engine.State;
        if (s.Round % EconomyPeriod == 3)
        {
            var options = Transitions[(int)s.Economy];
            int roll = engine.Random.Next(options.Sum(o => o.Weight));
            var next = options[^1].To;
            foreach (var o in options)
            {
                if (roll < o.Weight)
                {
                    next = o.To;
                    break;
                }
                roll -= o.Weight;
            }
            if (next != s.Economy) engine.Emit(new EconomyChanged(next, EconomyEffect(next)));
        }
        if (s.Round % EventPeriod == 2) engine.Emit(new CityEventOccurred(RollCityEvent(engine)));
    }

    private static ActiveEffect RollCityEvent(GameEngine engine)
    {
        var board = engine.Board;
        var rng = engine.Random;
        DistrictDef District() => board.Districts[rng.Next(board.Districts.Count)];
        ActiveEffect Event(string name, string description, EffectScope scope, string value, int rounds) => new()
        {
            Source = "event", Name = name, Description = description, Scope = scope, ScopeValue = value, RoundsLeft = rounds,
        };

        switch (rng.Next(9))
        {
            case 0:
            {
                var e = Event("Metro Expansion", "Properties near transit earn +15% rent", EffectScope.NearTransit, "", 5);
                e.RentPercent = 15;
                return e;
            }
            case 1:
            {
                var d = District();
                var e = Event("Street Festival", $"{d.Name} earns +25% rent", EffectScope.District, d.Id, 3);
                e.RentPercent = 25;
                return e;
            }
            case 2:
            {
                var d = District();
                var e = Event("Flash Flood", $"{d.Name} earns -50% rent", EffectScope.District, d.Id, 2);
                e.RentPercent = -50;
                return e;
            }
            case 3:
            {
                int side = rng.Next(4);
                var e = Event("Tourism Boom", "One side of the board earns +20% rent", EffectScope.Side, side.ToString(), 4);
                e.RentPercent = 20;
                return e;
            }
            case 4:
            {
                var e = Event("Property Crash", "All purchase prices -25%", EffectScope.All, "", 3);
                e.PricePercent = -25;
                return e;
            }
            case 5:
            {
                bool relief = rng.Next(2) == 0;
                var e = Event("Tax Reform", relief ? "Taxes are halved" : "Taxes rise by half", EffectScope.All, "", 4);
                e.TaxPercent = relief ? -50 : 50;
                return e;
            }
            case 6:
            {
                var e = Event("Infrastructure Drive", "Construction costs -20%", EffectScope.All, "", 4);
                e.BuildCostPercent = -20;
                return e;
            }
            case 7:
            {
                var e = Event("Transit Strike", "Transit lines earn -50% rent", EffectScope.TileType, nameof(TileType.Transit), 2);
                e.RentPercent = -50;
                return e;
            }
            default:
            {
                var e = Event("Energy Crunch", "Utilities earn double rent", EffectScope.TileType, nameof(TileType.Utility), 3);
                e.RentPercent = 100;
                return e;
            }
        }
    }
}

public sealed class PublicProjectModule : IRuleModule
{
    public const int ProposalPeriod = 5;

    private static readonly (string Name, int Cost)[] Catalog =
    {
        ("New Airport", 1000), ("Metro Line", 800), ("Grand Stadium", 600), ("Harbor Bridge", 900),
    };

    public string Id => "projects";

    public void OnRoundStarted(GameEngine engine)
    {
        var s = engine.State;
        if (s.Project != null)
        {
            if (s.Project.RoundsLeft == 0) engine.Emit(new ProjectFailed(s.Project.Name));
            return;
        }
        if (s.Round % ProposalPeriod != 2) return;
        var pick = Catalog[engine.Random.Next(Catalog.Length)];
        engine.Emit(new ProjectProposed(new ProjectState
        {
            Name = pick.Name, Cost = pick.Cost, Side = engine.Random.Next(4),
            RentPercent = 20, EffectRounds = 6, RoundsLeft = 4,
        }));
    }
}
