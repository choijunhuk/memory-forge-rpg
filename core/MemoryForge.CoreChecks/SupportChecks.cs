// S4: real Battle commands over fresh authored content; support fixtures never grant account ownership or change production data.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MemoryForge.Core;

static class SupportChecks
{
    static int checks, failures;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL Support " + label); } }
    static string Graph(Battle battle) => JsonSerializer.Serialize(battle.State.ToJsonGraph());
    static BattleArgument Args(string caller, string? target = null, params string[] picks) => BattleArgument.Object(new[]
    {
        new KeyValuePair<string, BattleArgument>("caller", BattleArgument.String(caller)),
        new KeyValuePair<string, BattleArgument>("target", target == null ? BattleArgument.Null() : BattleArgument.String(target)),
        new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String)))
    });
    static string? Reject(Action command) { try { command(); return null; } catch (RuleException e) { return e.Code; } }

    public static int Run(string root)
    {
        checks = failures = 0;
        try
        {
            var data = BattleDataLoader.Load(root);
            var battle = Battle.Create(data, new BattleSetup { Party = new List<BattleActorSetup> { new BattleActorSetup { CharId = "sera", Gauge = 100, Power4 = 160, Hp = 81 } },
                Hp = 81, MaxHp = 81, Seed = 1234, Enemies = new List<string> { data.Enemies[0].Id } });
            battle.Command(new BattleRequest("support-test-begin", 0, "begin"));
            string before = Graph(battle);
            Check(Reject(() => battle.Command(new BattleRequest("support-not-selected", battle.State.V, "support", Args("sera")))) == "SUPPORT_NOT_SELECTED",
                "support without a frozen selection is rejected explicitly");
            Check(Graph(battle) == before, "unselected support changes no full state, RNG, gauge or events");
        }
        catch (Exception e) { Check(false, "execution: " + e); }
        foreach (var scenario in new (string Name, Action<string> Run)[] { ("atomic/readiness", Atomic), ("all authored ults", AuthoredUlts),
            ("source tokens", Tokens), ("queries/picks/overflow", QueryFixtures), ("Run/mode forwarding", RunAndModes), ("Run token save", RunTokenSave) })
            try { scenario.Run(root); } catch (Exception e) { Check(false, scenario.Name + ": " + e); }
        Console.WriteLine($"SupportChecks: {checks - failures}/{checks} passed");
        return failures;
    }

    const string Target = "s4_target";
    static BattleData Data(string root)
    {
        var data = BattleDataLoader.Load(root); data.Rules.HandDraw = 10;
        data.Enemies.Add(new BattleEnemyDef { Id = Target, Name = "S4 isolated target", Tier = "normal", Hp = 100000, Brk = 10000,
            Weak = data.Characters.Select(c => c.Attr).Distinct().ToList(), Pattern = new List<EnemyMoveDef> { new EnemyMoveDef { Type = "atk", Damage = 0, Hits = 1, Label = "TEST ONLY" } } });
        return data;
    }
    static BattleSupportSetup Source(string root, string id, int res = 0, bool advanced = false)
    {
        var run = RunDataLoader.Load(root); string type = run.Characters.Single(c => c.Id == id).WeaponType;
        var weapon = run.Memory.Battle.Weapons.First(w => w.Type == type && w.Rarity == (advanced ? "SSR" : "R"));
        var d = Stats.Derive(run.Stats, new GrowthEntry { CharId = id, Level = advanced ? 60 : 1, WeaponRarity = weapon.Rarity,
            WeaponLevel = advanced ? 60 : 1, Armor = advanced ? 15 : null, Accessory = advanced ? 15 : null, SkillLv = advanced ? 10 : 1, UltLv = advanced ? 10 : 1 });
        return new BattleSupportSetup { CharId = id, WeaponId = weapon.Id, Refine = advanced ? 5 : 1, Res = res,
            Power4 = d.Power4, Hp = d.Hp, GearBps = d.GearBps, SkillLv = d.SkillLv, UltLv = d.UltLv };
    }
    static string Caller(BattleSupportSetup source) => source.CharId == "sera" ? "harin" : "sera";
    static BattleSetup Setup(BattleData data, BattleSupportSetup source, int gauge = 100, params string[] deck) => new BattleSetup
    {
        Support = source, Hp = 10000, MaxHp = 10000, Seed = 1234, Enemies = new List<string> { Target },
        Party = new List<BattleActorSetup> { new BattleActorSetup { CharId = Caller(source), Gauge = gauge, Power4 = 160, Hp = 81 } },
        Deck = deck.Select(id => new BattleCardSpec { Id = id }).ToList()
    };
    static Battle Open(BattleData data, BattleSetup setup)
    {
        var battle = Battle.Create(data, setup); battle.Command(new BattleRequest("begin", 0, "begin")); return battle;
    }
    static BattleResponse Command(Battle b, string kind, BattleArgument? args = null, string? rid = null) =>
        b.Command(new BattleRequest(rid, b.State.V, kind, args));
    static BattleArgument Fields(params (string Key, string? Value)[] fields) => BattleArgument.Object(fields.Select(f =>
        new KeyValuePair<string, BattleArgument>(f.Key, f.Value == null ? BattleArgument.Null() : BattleArgument.String(f.Value))));
    static void Refused(Battle b, string code, BattleArgument args, string label)
    {
        string before = Graph(b);
        Check(Reject(() => Command(b, "support", args)) == code, label + " code " + code);
        Check(Graph(b) == before, label + " full rollback including RNG/events/gauge/Used");
    }
    static void Atomic(string root)
    {
        var d = Data(root); var input = Source(root, "sera", 6, true); string caller = Caller(input);
        var b = Open(d, Setup(d, input)); string target = b.State.Enemies[0].Uid;
        string before = Graph(b); var snapshot = b.State; snapshot.Support!.Source.CardMods.Clear(); snapshot.Support.Source.Power4 = 1;
        input.Power4 = 1;
        Check(Graph(b) == before, "setup and returned source graphs are detached");
        var info = b.SupportInfo(caller, target); var forecast = b.SupportForecast(caller, target); var choices = b.SupportChoices(caller);
        Check(info.Ready && info.Code == null && info.GaugeCost == 100 && !info.Used && info.CharId == "sera", "ready query names the actual source and full100 cost");
        Check(forecast.Error == null && forecast.Hidden && forecast.EnemyHpLoss == 0 && forecast.EnemyHpLosses.Count == 0, "Sera draw masks all forecast outcomes");
        Check(choices.Error == null && choices.Uid == "" && choices.Requests.Count == 0, "authored support has no fabricated hand source or manual choices");
        Check(Graph(b) == before, "all support queries preserve the complete live graph");
        Refused(b, "UNKNOWN_ACTOR", Args("sera", target), "nonparty supporter cannot call itself");
        Refused(b, "BAD_TARGET", Args(caller, "missing"), "invalid target");
        Refused(b, "PICK_INVALID", Args(caller, target, "missing"), "unused supplied pick");
        var request = new BattleRequest("one-support", b.State.V, "support", Args(caller, target));
        var reply = b.Command(request); string committed = Graph(b), receipt = JsonSerializer.Serialize(reply.ToJsonGraph());
        Check(b.State.Actors.Single().Gauge == 0 && b.State.Support!.Used, "commit spends100 and marks Used");
        Check(reply.Events[0].E == "support" && reply.Events[0].Actor == "sera" && reply.Events[0].Caller == caller && reply.Events.All(e => e.E != "ult"), "one attributed support event precedes authored effects");
        var used = b.SupportInfo(caller, target);
        var summary = (Dictionary<string, object?>)b.PublicView()["support"]!;
        Check(used.Used && !used.Ready && used.Code == "SUPPORT_ALREADY_USED" && used.GaugeCost == 100 && summary.Keys.SequenceEqual(new[] { "charId", "used", "gaugeCost" })
            && (string)summary["charId"]! == "sera" && (bool)summary["used"]! && (int)summary["gaugeCost"]! == 100, "used query and exact public summary expose no private source graph");
        Check(JsonSerializer.Serialize(b.Command(request).ToJsonGraph()) == receipt && Graph(b) == committed, "sameID retry returns exact receipt without another effect");
        Check(Reject(() => b.Command(new BattleRequest("racing-support", request.V, "support", Args(caller, target)))) == "STALE_VERSION" && Graph(b) == committed, "same-version competitor loses atomically");
        Refused(b, "SUPPORT_ALREADY_USED", Args(caller, target), "new request cannot reuse support");
        Command(b, "end"); Check(b.State.Support!.Used, "turn transition never resets Used");
        Check(JsonSerializer.Serialize(b.Command(request).ToJsonGraph()) == receipt, "receipt retry still wins after another command advanced the battle");
        var low = Open(d, Setup(d, Source(root, "sera"), 99)); Refused(low, "ULT_NOT_READY", Args("harin"), "99 gauge");
        var created = Battle.Create(d, Setup(d, Source(root, "sera"))); Refused(created, "WRONG_PHASE", Args("harin"), "before begin");
        var overlap = Setup(d, Source(root, "sera")); overlap.Party[0].CharId = "sera";
        Check(Reject(() => Battle.Create(d, overlap)) == "SUPPORT_IN_PARTY", "support is disjoint from every deployed actor");
        var noWeapon = Source(root, "sera"); noWeapon.WeaponId = "missing";
        Check(Reject(() => Battle.Create(d, Setup(d, noWeapon))) == "INVALID_SNAPSHOT", "missing support weapon is never defaulted");

        var lethalData = Data(root); lethalData.Enemies.Single(e => e.Id == Target).Hp = 1;
        var lethal = Open(lethalData, Setup(lethalData, Source(root, "kaya", 6, true)));
        var lethalReply = Command(lethal, "support", Args("sera"));
        Check(lethal.State.Phase == "VICTORY" && lethal.State.Support!.Used && lethal.State.Actors.Single().Gauge == 0, "lethal first damage still spends100/Used and never refunds Kaya gauge to caller");
        Check(lethalReply.Events.Count(e => e.E == "support") == 1 && lethalReply.Events.Count(e => e.E == "end") == 1, "lethal support emits and ends once");
        var nonterminalSetup = Setup(lethalData, Source(root, "kaya", 6, true));
        lethalData.Enemies.Add(new BattleEnemyDef { Id = "s4_survivor", Name = "S4 survivor", Tier = "normal", Hp = 100000, Brk = 1000,
            Pattern = new List<EnemyMoveDef> { new EnemyMoveDef { Type = "charge" } } });
        nonterminalSetup.Enemies.Add("s4_survivor"); var kill = Open(lethalData, nonterminalSetup); Command(kill, "support", Args("sera"));
        Check(kill.State.Phase == "PLAYER" && kill.State.Enemies.Count(e => !e.Alive) == 1 && kill.State.Actors.Single().Gauge == 0,
            "Kaya r6 nonterminal kill reaches its hook without refunding caller");
        var waves = Setup(lethalData, Source(root, "sera"));
        var multi = Battle.CreateMode(lethalData, waves, new BattleMode { Kind = "dungeon", Waves = new List<List<string>> { new List<string> { Target } } });
        Command(multi, "begin"); Command(multi, "support", Args("harin"));
        Check(multi.State.Mode!.Wave == 2 && multi.State.Support!.Used, "same Battle wave transition retains Used");
        var fresh = Open(d, Setup(d, Source(root, "sera"))); Check(!fresh.State.Support!.Used, "a distinct new Battle resets Used");
    }

    // Explicit effect oracle: an ordinary ult with the same source stats/ranks/weapon. Remove ONLY supporter ordinary passive,
    // start/rule/capacity resonance and party-only weapon auras; caller/HP/deck remain the same. Rien's EchoActive and actor gauges
    // are excluded explicitly, because support does not deploy Rien or own a gauge. No four-person battle is constructed.
    static void AuthoredUlts(string root)
    {
        var ids = Data(root).Characters.Select(c => c.Id).ToArray();
        foreach (string id in ids) foreach (int res in Enumerable.Range(0, 7)) foreach (bool advanced in new[] { false, true })
        {
            string label = id + "/r" + res + "/" + (advanced ? "advanced" : "lv1");
            var data = Data(root); var source = Source(root, id, res, advanced); string growth = data.Cards.First(c => c.Growth == true).Id;
            var setup = Setup(data, source, 100, growth); var b = Open(data, setup); var before = b.State;
            var ordinaryData = Data(root); var ch = ordinaryData.Characters.Single(c => c.Id == id);
            ch.Passive = new PassiveDef { Id = "s4_inert", Params = new Dictionary<string, int> { ["max"] = 3 } };
            ResonanceFx OnlySource(ResonanceFx fx) => fx.Type == "multi" ? new ResonanceFx { Type = "multi", List = fx.List!.Select(OnlySource).ToList() }
                : new[] { "rule", "capacity", "battleStart", "passiveParam" }.Contains(fx.Type) ? new ResonanceFx { Type = "multi", List = new List<ResonanceFx>() } : fx;
            ch.Resonance = ch.Resonance.Select(OnlySource).ToList();
            var weapon = ordinaryData.Weapons.Single(w => w.Id == source.WeaponId);
            if (new[] { "w_healUp", "w_exhaustGuard", "w_debtDmg", "w_counterDmg" }.Contains(weapon.Passive.Id)) weapon.Passive.Id = "s4_disabled_party_aura";
            var oracleSetup = Setup(ordinaryData, source, 100, growth); oracleSetup.Support = null;
            oracleSetup.Party.Add(new BattleActorSetup { CharId = id, WeaponId = source.WeaponId, Refine = source.Refine, Res = res, Gauge = 100,
                Power4 = source.Power4, Hp = source.Hp, GearBps = source.GearBps, SkillLv = source.SkillLv, UltLv = source.UltLv });
            var oracle = Open(ordinaryData, oracleSetup); var prior = oracle.State;
            var actualSource = before.Support!.Source; var actor = prior.Actors.Single(a => a.Id == id);
            Check(actualSource.Power4 == actor.Power4 && actualSource.Vit == actor.Vit && actualSource.GearBps == actor.GearBps
                && actualSource.SkillLv == actor.SkillLv && actualSource.UltLv == actor.UltLv && actualSource.SkillLv == source.SkillLv + (res >= 3 ? 2 : 0)
                && actualSource.UltLv == source.UltLv + (res >= 5 ? 2 : 0), label + " frozen real-stat source/resonance exactly once vs ordinary actor");
            Check(before.Actors.Count == 1 && before.Hp == 10000 && before.MaxHp == 10000 && before.Cards.Count == 1 && before.GrowthCap == data.Rules.GrowthCapPerBattle
                && before.DebtCap == data.Rules.DebtCap && before.EchoStart == 0 && before.ExhaustStart == 0 && !before.OverloadFreeUsed,
                label + " adds no actor/HP/deck/rules/start effects");
            string live = Graph(b); var forecast = b.SupportForecast(Caller(source));
            var description = b.SupportInfo(Caller(source)).Text;
            if (res == 6 && id == "aila") Check(description.Contains("초과 회복") && description.Contains("100%"), label + " query describes the actual ult-local overflow hook");
            if (res == 6 && id == "sera") Check(description.Contains("다음 턴") && description.Contains("50%"), label + " query describes the actual ult-local next-turn hook");
            Check(b.SupportChoices(Caller(source)).Requests.Count == 0 && Graph(b) == live, label + " pure honest empty choices");
            var response = Command(b, "support", Args(Caller(source))); var oracleReply = Command(oracle, "ult", Fields(("actor", id)));
            Check(JsonSerializer.Serialize(response.Events.Skip(1).Select(e => e.ToJsonGraph())) == JsonSerializer.Serialize(oracleReply.Events.Skip(1).Select(e => e.ToJsonGraph())), label + " authored ult/replace/extra effect events and order");
            var after = b.State; var expected = oracle.State;
            Check(after.Block - before.Block == expected.Block - prior.Block && after.Hp - before.Hp == expected.Hp - prior.Hp && after.Counter == expected.Counter
                && after.ApNext == expected.ApNext && after.FreeNext == expected.FreeNext && after.Enemies.Select(e => (e.Hp, e.Brk, e.Status.Mark)).SequenceEqual(expected.Enemies.Select(e => (e.Hp, e.Brk, e.Status.Mark)))
                && after.Cards.Values.Select(c => (c.Id, c.Growth, c.CostMod, string.Join(",", c.Temp))).SequenceEqual(expected.Cards.Values.Select(c => (c.Id, c.Growth, c.CostMod, string.Join(",", c.Temp)))), label + " ordinary ult effect oracle outcome");
            Check(after.Actors.Single().Gauge == 0 && after.Support!.Used && (id != "rien" || !after.EchoActive && after.Echo == 0 && after.FreeNext == (res == 6 ? 3 : 2)), label + " caller cost, one use and absent Rien echo guard");
            Check(forecast.Error == null && (!forecast.Hidden || forecast.EnemyHpLoss == 0 && forecast.EnemyDamage == 0 && forecast.BlockDelta == 0
                && forecast.HpDelta == 0 && !forecast.Defeat && forecast.EnemyHpLosses.Count == 0), label + " hidden forecasts suppress every outcome");
        }
    }

    static void Tokens(string root)
    {
        var d = Data(root);
        BattleCardDef Card(string id, string owner, int cost, params EffectDef[] effects)
        {
            var card = new BattleCardDef { Id = id, Name = "S4 " + id, Owner = owner, Cost = cost, Rarity = "SR", Type = "atk", Reservation = "y", Attr = "wielder", Effects = effects.ToList() };
            d.Cards.Add(card); return card;
        }
        var token = Card("s4_token", "nia", 0, new EffectDef { Op = "dmg", V = 10 }, new EffectDef { Op = "blk", V = 10 }, new EffectDef { Op = "gauge", N = 50 }); token.Growth = true;
        Card("s4_common", "common", 0); Card("s4_foreign", "sera", 0); Card("s4_curse", "common", 0).Rarity = "C";
        Card("s4_copy", "common", 0, new EffectDef { Op = "copyPick" });
        d.Parts.Add(new BattlePartDef { Id = "s4_echo", Name = "S4 echo", Slot = "circuit", Rarity = "R", Fx = new BattlePartFx { EchoCopy = 1 } });
        d.Weapons.Add(new BattleWeaponDef { Id = "s4_weapon", Type = "device", Rarity = "SSR", Passive = new WeaponPassiveDef { Id = "w_blockUp", Values = Enumerable.Repeat(5000, 5).ToList() } });
        var nia = d.Characters.Single(c => c.Id == "nia");
        nia.Resonance[0] = new ResonanceFx { Type = "cardMod", Cards = new List<string> { "s4_token" }, Prop = "dmg", Add = 5 };
        nia.Ult.Effects = new[] { "s4_token", "s4_common", "s4_foreign", "s4_curse" }.Select(id => new EffectDef { Op = "create", Card = id, N = 1, Zone = "hand" }).ToList();
        nia.Ult.Effects.Add(new EffectDef { Op = "tune", Part = "s4_echo", Pick = "all" });
        nia.Ult.Effects.Add(new EffectDef { Op = "gauge", N = 50 });
        var input = Source(root, "nia", 3, true); input.WeaponId = "s4_weapon";
        var b = Open(d, Setup(d, input, 100, "s4_copy")); Command(b, "support", Args("sera"));
        var s = b.State; var own = s.Cards.Values.Single(c => c.Id == "s4_token");
        Check(s.Cards.Values.Where(c => c.Id != "s4_token").All(c => c.SupportSourceId == null) && own.SupportSourceId == "nia" && own.Token && !own.ToJsonGraph().ContainsKey("key"), "CREATE marks only the supporter's own temporary card, never common/deployed/curse cards or persistent keys");
        Check(s.Actors.Single().Gauge == 0, "support self-gauge has no absent recipient and never refunds caller");
        Check(b.EffectiveCard(own.Uid).Effects[0].V == 15, "provenanced token uses frozen source card modifiers");
        string copy = s.Cards.Values.Single(c => c.Id == "s4_copy").Uid;
        Command(b, "play", BattleArgument.Object(new[] { new KeyValuePair<string, BattleArgument>("uid", BattleArgument.String(copy)),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(new[] { BattleArgument.String(own.Uid) })) }));
        var copied = b.State.Cards.Values.Single(c => c.Id == "s4_token" && c.EchoCopy == true);
        Check(copied.SupportSourceId == "nia" && copied.Token && !copied.ToJsonGraph().ContainsKey("key"), "copyPick retains the original support provenance and remains ephemeral");
        var frozen = b.State.Support!.Source;
        int damage = (int)((long)15 * frozen.Power4 * (10000 + 400 * (frozen.SkillLv - 1)) * (10000 + frozen.GearBps) / (160L * 10000 * 10000));
        int block = (int)((long)10 * frozen.Vit * (10000 + 400 * (frozen.SkillLv - 1)) * (15000 + frozen.GearBps) / (81L * 10000 * 10000));
        var preview = b.Forecast(own.Uid); string before = Graph(b);
        Check(preview.Error == null && !preview.Hidden && preview.EnemyHpLoss == damage && preview.BlockDelta == block && Graph(b) == before,
            "token forecast uses source Power4/Vit/skill/gear/weapon instead of party averages");
        int hp = b.State.Enemies.Single().Hp; Command(b, "play", Fields(("uid", own.Uid)));
        Check(hp - b.State.Enemies.Single().Hp == damage && b.State.Actors.Single().Gauge == 0 && b.State.LastOwner == null && b.State.OwnersThisTurn.Count == 0,
            "token resolves with source stats without gauge redirection or fourth-owner bookkeeping");
        Check(b.State.PendingCopies!.Any(p => p.Id == "s4_token" && p.SupportSourceId == "nia"), "pending echo copy carries explicit source provenance");
        Command(b, "end");
        var delivered = b.State.Cards.Values.Single(c => c.Id == "s4_token" && c.Temp.Contains("echoHalf"));
        Check(delivered.SupportSourceId == "nia" && delivered.Token && !delivered.ToJsonGraph().ContainsKey("key"), "future-turn echo delivery retains source and no memory key");
        var reserved = b.State.Cards[own.Uid];
        Command(b, "play", Fields(("uid", reserved.Uid), ("mode", "next")));
        Check(b.State.Reservations.Any(r => r.Uid == own.Uid) && b.State.Cards[own.Uid].SupportSourceId == "nia", "isolated reservable token keeps source by actual reservation UID");
        hp = b.State.Enemies.Single().Hp; Command(b, "end");
        Check(hp - b.State.Enemies.Single().Hp == damage && b.State.Support!.Used && b.State.Actors.Single().Gauge == 0,
            "later reserved resolver still uses frozen source and keeps Used/gauge isolation");
        var forged = Setup(d, Source(root, "nia"), 100, "s4_token");
        typeof(BattleCardSpec).GetField("SupportSourceId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(forged.Deck[0], "nia");
        Check(Reject(() => Battle.Create(d, forged)) == "INVALID_SNAPSHOT", "forged setup provenance cannot turn an initial non-token card into a supporter token");

        var copyData = Data(root); string growth = copyData.Cards.First(c => c.Growth == true).Id;
        copyData.Characters.Single(c => c.Id == "nia").Ult.Effects = new List<EffectDef> { new EffectDef { Op = "copyPick" } };
        var copySetup = Setup(copyData, Source(root, "nia"), 100, growth); copySetup.Party[0].CharId = "yuri";
        var foreign = Open(copyData, copySetup); string original = foreign.State.Zones.Hand.Single(); Command(foreign, "support", Args("yuri", null, original));
        Check(foreign.State.Cards.Values.Single(c => c.EchoCopy == true).SupportSourceId == null,
            "support copying a deployed card retains that card's normal source rather than adopting supporter provenance");
    }

    static void QueryFixtures(string root)
    {
        var d = Data(root); var source = Source(root, "nia");
        d.Cards.Add(new BattleCardDef { Id = "s4_free", Name = "S4 free", Owner = "common", Rarity = "R", Cost = 0, Reservation = "n", Effects = new List<EffectDef> { new EffectDef { Op = "freeNext", N = 1 } } });
        d.Cards.Add(new BattleCardDef { Id = "s4_price", Name = "S4 price", Owner = "common", Rarity = "R", Cost = 2, Reservation = "y", Effects = new List<EffectDef> { new EffectDef { Op = "dmg", V = 1 } } });
        var nia = d.Characters.Single(c => c.Id == "nia"); nia.Ult.Effects = new List<EffectDef> { new EffectDef { Op = "costdown", N = 1, Pick = "one" } };
        var b = Open(d, Setup(d, source, 100, "s4_free", "s4_price"));
        string price = b.State.Cards.Values.Single(c => c.Id == "s4_price").Uid;
        Check(b.SupportChoices("sera").Candidates.Contains(price), "effect-list choices offer the actual eligible hand card without a fake source UID");
        Command(b, "play", Fields(("uid", b.State.Cards.Values.Single(c => c.Id == "s4_free").Uid)));
        Check(b.State.FreeNext == 1 && b.SupportChoices("sera").Candidates.Count == 0, "support costdown does not simulate consuming the next-card-free charge");
        Refused(b, "PICK_INVALID", Args("sera", null, price), "ineffective manual support pick");
        Command(b, "support", Args("sera")); Check(b.State.FreeNext == 1, "support itself preserves the free-next-card charge");

        var p = Data(root); p.Cards.Add(d.Cards.Single(c => c.Id == "s4_price"));
        p.Characters.Single(c => c.Id == "nia").Ult.Effects = new List<EffectDef> { new EffectDef { Op = "pull" } };
        var pull = Open(p, Setup(p, Source(root, "nia"), 100, "s4_price"));
        string uid = pull.State.Zones.Hand.Single(); Command(pull, "play", Fields(("uid", uid), ("mode", "after")));
        string original = Graph(pull); var slots = pull.ReservationSlots(); var choice = pull.SupportChoices("sera", new[] { uid }); var forecast = pull.SupportForecast("sera", null, new[] { uid });
        Check(choice.Error == null && forecast.Error == null && !forecast.Hidden && Graph(pull) == original && slots.After.Used == 1, "pull choice/forecast uses the actual reservation board purely");
        Command(pull, "support", Args("sera", null, uid));
        Check(pull.State.Reservations.Single().Due == pull.State.Turn + 1 && pull.State.Reservations.Single().Kind == "next", "actual support pull advances the original reservation");
        var overflow = Data(root); overflow.Characters.Single(c => c.Id == "sera").Ult.Effects = new List<EffectDef> { new EffectDef { Op = "dmg", V = int.MaxValue } };
        var large = Source(root, "sera"); large.Power4 = int.MaxValue;
        var throwing = Open(overflow, Setup(overflow, large)); Refused(throwing, "BATTLE_NUMBER_OUT_OF_RANGE", Args("harin"), "effect overflow");
        string kept = Graph(throwing); Check(throwing.SupportForecast("harin").Error == "BATTLE_NUMBER_OUT_OF_RANGE" && Graph(throwing) == kept, "throwing forecast also preserves full state/use/RNG");
    }

    static RunPartyEntry Build(string root, string id, bool advanced = false)
    {
        var source = Source(root, id, 6, advanced);
        return RunPartyEntry.FromJsonGraph(new Dictionary<string, object?> { ["charId"] = id, ["level"] = advanced ? 60 : 1, ["asc"] = advanced ? 4 : 0,
            ["res"] = 6, ["weaponId"] = source.WeaponId, ["weaponInstanceId"] = "owned-" + id, ["weaponLevel"] = advanced ? 60 : 1,
            ["refine"] = source.Refine, ["skillLv"] = advanced ? 10 : 1, ["ultLv"] = advanced ? 10 : 1,
            ["armor"] = advanced ? 15 : null, ["accessory"] = advanced ? 15 : null });
    }
    static void RunAndModes(string root)
    {
        var data = RunDataLoader.Load(root); var support = Build(root, "nia", true);
        var setup = new RunSetup { Seed = 1234, Support = support, MemoryPolicy = "v2", RunId = "s4-run", MemoryVersion = 0,
            Party = new List<RunPartyEntry> { Build(root, "sera"), Build(root, "harin"), Build(root, "yuri") } };
        var run = MemoryForge.Core.Run.Create(data, setup); var s = run.State;
        Check(s.Party.Count == 3 && s.Support!.CharId == "nia" && (string)s.Support.ToJsonGraph()["weaponInstanceId"]! == "owned-nia", "Run freezes the complete separate build including exact instance metadata");
        var noSupport = new RunSetup { Seed = 1234, MemoryPolicy = "v2", RunId = "s4-run", MemoryVersion = 0, Party = setup.Party };
        var baseline = MemoryForge.Core.Run.Create(data, noSupport).State;
        Check(s.MaxHp == baseline.MaxHp && JsonSerializer.Serialize(s.Deck.Select(c => c.ToJsonGraph())) == JsonSerializer.Serialize(baseline.Deck.Select(c => c.ToJsonGraph()))
            && !baseline.ToJsonGraph().ContainsKey("support"), "support adds no Run party HP/deck/memory and absence is omitted");
        support.Level = 1; support.WeaponLevel = 1; support.SkillLv = 1;
        Check(run.State.Support!.Level == 60 && run.State.Support.SkillLv == 10, "later selection/growth/equipment input mutations cannot change active Run");
        run.Enter(run.Available().First(n => run.NodeById(n)?.Type == "battle"));
        Check(run.State.Battle!.Support!.Source.Power4 == s.Support!.Power4 && run.State.Battle.Support.Source.SkillLv == 12 && !run.State.Battle.Support.Used, "Run forwards frozen derived stats and resonance once into each actual Battle");
        string before = JsonSerializer.Serialize(run.State.ToJsonGraph());
        Check(run.BattleSupportInfo("sera").Code == "ULT_NOT_READY" && run.BattleSupportForecast("sera").Error == "ULT_NOT_READY"
            && run.BattleSupportChoices("sera").Error == "ULT_NOT_READY" && JsonSerializer.Serialize(run.State.ToJsonGraph()) == before, "Run support queries forward pure actual battle readiness");
        var missing = Build(root, "nia"); missing.Level = null; setup.Support = missing;
        Check(Reject(() => MemoryForge.Core.Run.Create(data, setup)) == "INVALID_SNAPSHOT", "Run refuses reference-stat support input");
        var mismatch = Build(root, "nia"); mismatch.WeaponId = data.Memory.Battle.Weapons.First(w => w.Type != data.Characters.Single(c => c.Id == "nia").WeaponType).Id; setup.Support = mismatch;
        Check(Reject(() => MemoryForge.Core.Run.Create(data, setup)) == "INVALID_SNAPSHOT", "Run refuses mismatched support weapon instead of substituting");
        var duplicate = Build(root, "nia"); var graph = duplicate.ToJsonGraph(); graph["weaponInstanceId"] = "owned-sera"; setup.Support = RunPartyEntry.FromJsonGraph(graph);
        Check(Reject(() => MemoryForge.Core.Run.Create(data, setup)) == "INVALID_SNAPSHOT", "Run refuses supporter/deployed instance collision");

        using var game = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "game_data.json")));
        using var modesJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "content_modes.json")));
        var modes = ContentModesDataLoader.Load(game.RootElement.GetProperty("progression"), modesJson.RootElement, RunDataLoader.Load(root));
        var account = new ContentAccount { Level = 60, Stamina = 1000, Owned = new List<string> { "sera", "harin", "yuri", "nia" } };
        var party = setup.Party.Cast<RunPartyEntry?>().ToList(); var chosen = Build(root, "nia", true);
        var weekly = ContentModes.WeeklyBoss.Start(modes, account, new WeeklyBossStartOptions { BossId = modes.WeeklyBoss.Bosses[0], Difficulty = modes.WeeklyBoss.Difficulty[0].Lv,
            Party = party, Support = chosen, Seed = 1234, Practice = true });
        Check(weekly.State.Support!.Source.Id == "nia" && !weekly.State.Support.Used && weekly.State.Actors.Count == 3, "weekly launch forwards support without a fourth actor");
        var dungeon = modes.Dungeon.Dungeons[0]; var tier = modes.Dungeon.Tiers[0];
        modes.Dungeon.Encounters = new Dictionary<string, Dictionary<string, List<List<string>>>> { [dungeon.Id] = new Dictionary<string, List<List<string>>> { [tier.Tier.ToString()] = new List<List<string>> { new List<string> { modes.Run.Memory.Battle.Enemies[0].Id }, new List<string> { modes.Run.Memory.Battle.Enemies[0].Id } } } };
        var battle = ContentModes.Dungeon.Start(modes, account, new DungeonStartOptions { DungeonId = dungeon.Id, Tier = tier.Tier, Party = party, Support = chosen, Seed = 1234 });
        Check(battle.State.Support!.Source.Id == "nia" && !battle.State.Support.Used && battle.State.Actors.Count == 3, "isolated authored-wave dungeon forwards support");
        account.Owned.Remove("nia");
        Check(Reject(() => ContentModes.WeeklyBoss.Start(modes, account, new WeeklyBossStartOptions { BossId = modes.WeeklyBoss.Bosses[0], Difficulty = modes.WeeklyBoss.Difficulty[0].Lv, Party = party, Support = chosen })) == "NOT_OWNED", "mode launch enforces actual declared ownership");
    }

    static void RunTokenSave(string root)
    {
        var data = RunDataLoader.Load(root);
        // Isolated content fixture: every real card can earn gauge with an actual command; no account/owned-state injection.
        foreach (var card in data.Memory.Battle.Cards) { card.Cost = 0; card.Effects = new List<EffectDef> { new EffectDef { Op = "gauge", Who = "all", N = 100 } }; }
        data.Memory.Battle.Cards.Add(new BattleCardDef { Id = "s4_run_token", Name = "S4 ephemeral", Owner = "nia", Rarity = "SR", Cost = 0, Reservation = "n", Attr = "metal" });
        data.Memory.Battle.Characters.Single(c => c.Id == "nia").Ult.Effects = new List<EffectDef> {
            new EffectDef { Op = "create", Card = "s4_run_token", N = 1, Zone = "hand" }, new EffectDef { Op = "dmg", Target = "all", V = 100000, Hits = 3 } };
        foreach (var enemy in data.Memory.Battle.Enemies) enemy.Hp = 1;
        var setup = new RunSetup { Seed = 1234, MemoryPolicy = "v2", RunId = "s4-token-save", MemoryVersion = 0, Support = Build(root, "nia", true),
            Party = new List<RunPartyEntry> { Build(root, "sera"), Build(root, "harin"), Build(root, "yuri") } };
        var run = MemoryForge.Core.Run.Create(data, setup); int battles = 0;
        for (int step = 0; step < 100 && run.State.Phase != "VICTORY" && run.State.Phase != "DEFEAT"; step++)
        {
            var s = run.State;
            if (s.Phase == "MAP") run.Enter(run.Available().First());
            else if (s.Phase == "BATTLE")
            {
                Check(s.Battle!.Support != null && !s.Battle.Support.Used && s.Battle.Support.Source.UltLv == 12, "each actual Run battle resets only Used, never re-applies resonance");
                run.BattleCommand("play", Fields(("uid", s.Battle.Zones.Hand[0])));
                var reply = run.BattleCommand("support", Args("sera"));
                Check(reply.Events.Any(e => e.E == "support") && run.State.Phase == "REWARD", "earned-gauge support reaches actual Run terminal capture"); battles++;
            }
            else if (s.Phase == "ARCHIVE") run.Archive("return");
            else run.Proceed();
        }
        Check(run.State.Phase == "VICTORY" && battles >= 2, "bounded actual-command driver reaches the real memory save through multiple Battles");
        string after = JsonSerializer.Serialize(run.State.MemoryAfter!.ToJsonGraph());
        Check(!after.Contains("s4_run_token") && !after.Contains("supportSourceId") && !run.State.Deck.Any(c => c.Id == "s4_run_token"), "created support tokens never enter Run deck or terminal memory");
        var empty = new Dictionary<string, object?>(); var store = MemoryV2.Migrate(data.Memory, empty, empty).ToJsonGraph();
        var request = new Dictionary<string, object?> { ["requestId"] = "s4-save", ["runId"] = "s4-token-save", ["expectedMemoryVersion"] = 0, ["selectedChangeIds"] = new List<string>() };
        string saved = JsonSerializer.Serialize(run.CommitMemory(request, store));
        Check(run.State.Phase == "DONE" && !saved.Contains("s4_run_token") && !saved.Contains("supportSourceId"), "actual memory commit cannot persist support tokens/provenance or grant ownership");
    }
}
