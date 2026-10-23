// P1-RUN-CLIENT-CONTENT: the packaged run-content.json sidecar carries the original §7.5–7.7 events and §8.9–8.14 memory
// costs/capacity plus progression stats into the actual Unity runtime. Original values are copied verbatim from
// data/game_data.json (rules.capacityBase/memoryCost, progression.stats, progression.levelling, events); the initial hashes are the
// actual JS Create output already recorded in RunProgressionPlayModeTests. Content bridge only, not Run UI/save/server/device acceptance.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MemoryForge.Client;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using CoreRun = MemoryForge.Core.Run;

namespace MemoryForge.PlayModeTests
{
    public class RunClientContentPlayModeTests
    {
        const string Original = @"{""meta"":{""version"":""0.7.0""},
""rules"":{""capacityBase"":6,""memoryCost"":{""addB"":1,""addA"":2,""addS"":3,""addCommon"":1,""upgrade"":1,""partCommon"":1,""partRare"":2,""evolution"":2,""remove"":1}},
""progression"":{""stats"":{""levelCap"":60,""referencePower"":40,""referenceHpPerChar"":81,""charPower"":{""base"":20,""num"":14,""den"":60},
""weaponPower"":{""SSR"":{""base"":2,""k"":6},""SR"":{""base"":1,""k"":5},""R"":{""base"":1,""k"":3}},""hpBase"":60,
""gear"":{""maxEnhance"":15,""armorFlatBase"":2,""armorBpsBase"":300,""armorBpsPer"":60,""accessoryBpsBase"":250,""accessoryBpsPer"":50},
""skill"":{""maxLevel"":10,""bpsPerLevel"":400,""resonanceBonus"":2},""capacity"":{""base"":6,""ascension"":[{""asc"":2,""add"":1},{""asc"":4,""add"":1}]},""stageScale"":{""refWeapon"":""SR""}},
""levelling"":{""charXp"":{""k"":40,""a"":4,""sqDiv"":10},""weaponXpRatio"":0.5}},
""events"":[
{""id"":""ev_promise"",""name"":""멈춘 약속"",""text"":""시곗바늘이 멈춘 광장에서 누군가 지키지 못한 약속을 내민다."",""choices"":[
{""label"":""약속을 대신 짊어진다"",""text"":""최대 체력 -10%, 무작위 희귀 부품 1개"",""fx"":{""maxHpPct"":-10,""partRare"":1}},
{""label"":""대가를 치른다"",""text"":""동전 -40, 무작위 일반 부품 1개"",""fx"":{""coins"":-40,""part"":1},""need"":{""coins"":40}},
{""label"":""지나친다"",""text"":""아무 일도 일어나지 않는다"",""fx"":{}}]},
{""id"":""ev_mirror"",""name"":""깨진 거울"",""text"":""조각난 거울마다 다른 순간의 내가 비친다."",""choices"":[
{""label"":""한 조각을 집는다"",""text"":""체력 -6, 카드 1장 복제"",""fx"":{""hp"":-6,""cardCopy"":1}},
{""label"":""조각을 묻는다"",""text"":""동전 -30, 카드 1장 제거"",""fx"":{""coins"":-30,""cardRemove"":1},""need"":{""coins"":30}},
{""label"":""지나친다"",""text"":""아무 일도 일어나지 않는다"",""fx"":{}}]},
{""id"":""ev_busker"",""name"":""떠도는 악사"",""text"":""태엽 손풍금을 든 악사가 동전 몇 닢에 노래를 들려주겠다고 한다."",""choices"":[
{""label"":""노래를 청한다"",""text"":""동전 -25, 무작위 일반 유물 1개"",""fx"":{""coins"":-25,""relic"":""common""},""need"":{""coins"":25}},
{""label"":""같이 쉰다"",""text"":""체력 15 회복"",""fx"":{""heal"":15}},
{""label"":""지나친다"",""text"":""아무 일도 일어나지 않는다"",""fx"":{}}]},
{""id"":""ev_merchant"",""name"":""기억 상인"",""text"":""\""흠 없는 기억은 비싸고, 얼룩진 기억은 싸지요.\"""",""choices"":[
{""label"":""변주를 산다"",""text"":""동전 -60, 카드 변주 1회"",""fx"":{""coins"":-60,""variation"":1},""need"":{""coins"":60}},
{""label"":""얼룩을 판다"",""text"":""덱에 얼룩 1장, 동전 +50"",""fx"":{""curse"":""smudge"",""coins"":50}},
{""label"":""지나친다"",""text"":""아무 일도 일어나지 않는다"",""fx"":{}}]},
{""id"":""ev_archive"",""name"":""잊힌 서고"",""text"":""먼지 쌓인 서가에 아무도 빌려 가지 않은 기억이 꽂혀 있다."",""choices"":[
{""label"":""정리한다"",""text"":""최대 체력 -5, 카드 2장 제거"",""fx"":{""maxHp"":-5,""cardRemove"":2}},
{""label"":""한 권 빌린다"",""text"":""무작위 고급 카드 1장 획득"",""fx"":{""card"":""A""}},
{""label"":""지나친다"",""text"":""아무 일도 일어나지 않는다"",""fx"":{}}]},
{""id"":""ev_rift"",""name"":""시간의 틈"",""text"":""공기가 갈라진 틈 너머로 조금 이른 미래가 보인다."",""choices"":[
{""label"":""먼저 들어간다"",""text"":""체력 -8, 다음 전투 첫 턴 행동력 +2"",""fx"":{""hp"":-8,""nextApBonus"":2}},
{""label"":""틈을 닫는다"",""text"":""체력 10 회복"",""fx"":{""heal"":10}}]},
{""id"":""ev_spirit"",""name"":""온실의 정령"",""text"":""작은 정령이 시든 잎을 내밀며 고개를 갸웃한다."",""choices"":[
{""label"":""돌봐 준다"",""text"":""성장 카드 1장 성장 +1(이번 탐사 유지)"",""fx"":{""growth"":1}},
{""label"":""함께 쉰다"",""text"":""체력 12 회복"",""fx"":{""heal"":12}}]},
{""id"":""ev_contracts"",""name"":""계약서 더미"",""text"":""서명만 하면 되는 계약서가 바람에 날린다."",""choices"":[
{""label"":""서명한다"",""text"":""동전 +80, 덱에 위약금 1장"",""fx"":{""coins"":80,""curse"":""penalty_note""}},
{""label"":""지나친다"",""text"":""아무 일도 일어나지 않는다"",""fx"":{}}]}]}";

        // Source key of every StatsConfig property that StatsDataLoader reads (WeaponXpRatioNum/Den are checked against levelling.weaponXpRatio).
        static readonly Dictionary<string, string> StatPaths = new Dictionary<string, string>
        {
            ["LevelCap"] = "stats.levelCap", ["ReferencePower"] = "stats.referencePower", ["ReferenceHpPerChar"] = "stats.referenceHpPerChar",
            ["CharPowerBase"] = "stats.charPower.base", ["CharPowerNum"] = "stats.charPower.num", ["CharPowerDen"] = "stats.charPower.den", ["HpBase"] = "stats.hpBase",
            ["WeaponSsrBase"] = "stats.weaponPower.SSR.base", ["WeaponSsrK"] = "stats.weaponPower.SSR.k", ["WeaponSrBase"] = "stats.weaponPower.SR.base",
            ["WeaponSrK"] = "stats.weaponPower.SR.k", ["WeaponRBase"] = "stats.weaponPower.R.base", ["WeaponRK"] = "stats.weaponPower.R.k",
            ["GearMax"] = "stats.gear.maxEnhance", ["ArmorFlatBase"] = "stats.gear.armorFlatBase", ["ArmorBpsBase"] = "stats.gear.armorBpsBase",
            ["ArmorBpsPer"] = "stats.gear.armorBpsPer", ["AccessoryBpsBase"] = "stats.gear.accessoryBpsBase", ["AccessoryBpsPer"] = "stats.gear.accessoryBpsPer",
            ["SkillMax"] = "stats.skill.maxLevel", ["StageRefWeapon"] = "stats.stageScale.refWeapon",
            ["XpK"] = "levelling.charXp.k", ["XpA"] = "levelling.charXp.a", ["XpSqDiv"] = "levelling.charXp.sqDiv"
        };
        static readonly Dictionary<string, string> CostKeys = new Dictionary<string, string>
        {
            ["AddB"] = "addB", ["AddA"] = "addA", ["AddS"] = "addS", ["AddCommon"] = "addCommon", ["Upgrade"] = "upgrade",
            ["PartCommon"] = "partCommon", ["PartRare"] = "partRare", ["Evolution"] = "evolution", ["Remove"] = "remove"
        };

        static JObject Parse(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                return JObject.Load(reader);
        }
        static string SidecarText()
        {
            var asset = Resources.Load<TextAsset>(ClientRunContent.ResourcePath);
            Assert.That(asset, Is.Not.Null, "packaged Resources/" + ClientRunContent.ResourcePath + ".json");
            try { return asset.text; }
            finally { Resources.UnloadAsset(asset); }
        }
        static JToken Plain(object value) => JToken.FromObject(value, new JsonSerializer());
        static string Hash(CoreRun run) => CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = run.State.ToJsonGraph(), ["memory"] = null });

        [Test]
        public void PackagedSidecarCarriesEveryOriginalCostCapacityStatAndEvent()
        {
            Assert.That(Application.isPlaying, Is.True);
            var original = Parse(Original);
            var battle = ClientBattleContent.Load();
            var data = ClientRunContent.Load();

            Assert.That(typeof(MemoryCostConfig).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name), Is.EquivalentTo(CostKeys.Keys));
            foreach (var pair in CostKeys)
                Assert.That(typeof(MemoryCostConfig).GetField(pair.Key)!.GetValue(data.Memory.Costs), Is.EqualTo((int)original["rules"]!["memoryCost"]![pair.Value]!), "Costs." + pair.Key);
            Assert.That(data.Memory.CapacityBase, Is.EqualTo((int)original["rules"]!["capacityBase"]!));
            Assert.That(data.Memory.AscensionCapacity.Select(r => new[] { r.Asc, r.Add }),
                Is.EqualTo(((JArray)original["progression"]!["stats"]!["capacity"]!["ascension"]!).Select(r => new[] { (int)r["asc"]!, (int)r["add"]! })));

            var progression = original["progression"]!;
            Assert.That(StatPaths.Keys.Concat(new[] { "WeaponXpRatioNum", "WeaponXpRatioDen" }),
                Is.EquivalentTo(typeof(StatsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)));
            foreach (var pair in StatPaths)
            {
                object? actual = typeof(StatsConfig).GetProperty(pair.Key)!.GetValue(data.Stats);
                var expected = (JValue)progression.SelectToken(pair.Value)!;
                Assert.That(actual, Is.EqualTo(expected.Value is long number ? (object)(int)number : expected.Value), "Stats." + pair.Key);
            }
            Assert.That((decimal)data.Stats.WeaponXpRatioNum / data.Stats.WeaponXpRatioDen, Is.EqualTo((decimal)progression["levelling"]!["weaponXpRatio"]!));

            var events = Plain(data.Events.Select(e => e.ToJsonGraph()).ToList());
            Assert.That(JToken.DeepEquals(events, original["events"]), Is.True, "events differ from data/game_data.json: " + events.ToString(Formatting.None));
            Assert.That(data.Events.Count, Is.EqualTo(8));

            // Every sidecar member is accounted for and equals what the runtime now holds.
            var sidecar = Parse(SidecarText());
            Assert.That(sidecar.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "Schema", "DataVersion", "SourceSha256", "SourceEventCount", "Stats", "Costs", "CapacityBase", "AscensionCapacity", "Events" }));
            Assert.That((string)sidecar["Schema"]!, Is.EqualTo(ClientRunContent.SchemaId));
            Assert.That((string)sidecar["DataVersion"]!, Is.EqualTo((string)original["meta"]!["version"]!));
            Assert.That((string)sidecar["DataVersion"]!, Is.EqualTo(battle.DataVersion));
            Assert.That((string)sidecar["SourceSha256"]!, Is.EqualTo(battle.SourceSha256));
            StringAssert.IsMatch("^[0-9a-f]{64}$", battle.SourceSha256);
            Assert.That((int)sidecar["SourceEventCount"]!, Is.EqualTo(((JArray)original["events"]!).Count));
            Assert.That(JToken.DeepEquals(sidecar["Stats"], Plain(data.Stats)), Is.True, "Stats");
            Assert.That(JToken.DeepEquals(sidecar["Costs"], Plain(data.Memory.Costs)), Is.True, "Costs");
            Assert.That((int)sidecar["CapacityBase"]!, Is.EqualTo(data.Memory.CapacityBase));
            Assert.That(JToken.DeepEquals(sidecar["AscensionCapacity"], Plain(data.Memory.AscensionCapacity)), Is.True, "AscensionCapacity");
            Assert.That(JToken.DeepEquals(sidecar["Events"], Plain(data.Events)), Is.True, "Events");

            // Battle, starting commons, characters and encounters come from the same-source battle content, in source order.
            Assert.That(data.Memory.Battle.Cards.Count, Is.EqualTo(battle.SourceCounts.Cards));
            Assert.That(data.Memory.Battle.Characters.Count, Is.EqualTo(battle.SourceCounts.Characters));
            Assert.That(data.Memory.StartingCommon.Select(p => p.Key + "=" + p.Value), Is.EqualTo(battle.StartingCommon.Select(p => p.Key + "=" + p.Value)));
            Assert.That(data.Characters.Select(c => c.Id + "/" + c.WeaponType), Is.EqualTo(battle.Battle.Characters.Select(c => c.Id + "/" + battle.CharacterWeaponTypes[c.Id])));
            Assert.That(data.Encounters.Select(z => z.Zone), Is.EqualTo(battle.Encounters.Keys));
            foreach (var zone in data.Encounters)
            {
                var source = battle.Encounters[zone.Zone];
                Assert.That(zone.Normal, Is.EqualTo(source.Normal)); Assert.That(zone.Elite, Is.EqualTo(source.Elite)); Assert.That(zone.Boss, Is.EqualTo(source.Boss));
            }
        }

        [Test]
        public void OriginalMemoryCostsAndCapacityReachTheActualCoreRules()
        {
            Assert.That(Application.isPlaying, Is.True);
            var cost = Parse(Original)["rules"]!["memoryCost"]!;
            var memory = ClientRunContent.Load().Memory;
            var baseline = MemoryV2.Baseline(memory, "sera").Select(c => (object?)c.ToJsonGraph()).ToList();
            Assert.That(MemoryV2.Cost(memory, "sera", baseline, baseline), Is.Zero);

            var changed = baseline.Select(c => (object?)new Dictionary<string, object?>((Dictionary<string, object?>)c!)).ToList();
            ((Dictionary<string, object?>)changed[0]!)["plus"] = true;
            Assert.That(MemoryV2.Cost(memory, "sera", changed, baseline), Is.EqualTo((long)(int)cost["upgrade"]!));
            changed.RemoveAt(changed.Count - 1);
            Assert.That(MemoryV2.Cost(memory, "sera", changed, baseline), Is.EqualTo((long)((int)cost["upgrade"]! + (int)cost["remove"]!)));
            // sera_timed is an authored rarity-A Sera card (data/game_data.json cards[]).
            Assert.That(memory.Battle.Cards.Single(c => c.Id == "sera_timed").Rarity, Is.EqualTo("A"));
            changed.Add(new Dictionary<string, object?> { ["id"] = "sera_timed", ["key"] = "unity:added" });
            Assert.That(MemoryV2.Cost(memory, "sera", changed, baseline), Is.EqualTo((long)((int)cost["upgrade"]! + (int)cost["remove"]! + (int)cost["addA"]!)));

            // rules.capacityBase 6, plus progression.stats.capacity.ascension +1 at asc 2 and +1 at asc 4; no resonance at res 0.
            Assert.That(new[] { 0, 1, 2, 3, 4, 5 }.Select(asc => MemoryV2.CapacityOf(memory, new MemoryCapacityEntry { CharId = "sera", Res = 0, Asc = asc })),
                Is.EqualTo(new[] { 6, 6, 7, 7, 8, 8 }));
        }

        [TestCase("legacy", "56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973")]
        [TestCase("v2", "11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3")]
        public void ActualRunCreateFromPackagedContentMatchesOriginalJs(string policy, string initial)
        {
            Assert.That(Application.isPlaying, Is.True);
            var setup = new RunSetup { Seed = 1234, Length = "short", MemoryPolicy = policy, Memory = new Dictionary<string, object?>(),
                Zones = new List<int> { 1 }, Difficulty = new RunDifficulty { Hp = 100, Damage = 100 } };
            if (policy == "v2") { setup.RunId = "unity-assignment"; setup.MemoryVersion = 0L; }
            foreach (string id in new[] { "sera", "harin", "nia" }) setup.Party.Add(new RunPartyEntry { CharId = id, Res = 0 });
            var run = CoreRun.Create(ClientRunContent.Load(), setup);
            Assert.That(Hash(run), Is.EqualTo(initial));
            Assert.That(run.State.Phase, Is.EqualTo("MAP"));
            // No level given: three party members at progression.stats.referenceHpPerChar 81.
            Assert.That(new[] { run.State.Hp, run.State.MaxHp }, Is.EqualTo(new[] { 243, 243 }));
            Assert.That(setup.Party.All(p => p.Power4 == null && p.Hp == null), Is.True, "actual Create must not mutate caller stats");
        }

        [Test]
        public void UnmodifiedPackagedTextLoadsThroughFromJson()
        {
            Assert.That(Application.isPlaying, Is.True);
            string text = SidecarText();
            Assert.That(ClientRunContent.FromJson(text, ClientBattleContent.Load()).Events.Count, Is.EqualTo(8));
            // Re-serialization alone (as the malformed cases do) must not be what makes them fail.
            Assert.That(ClientRunContent.FromJson(Parse(text).ToString(Formatting.None), ClientBattleContent.Load()).Events.Count, Is.EqualTo(8));
        }

        [TestCase("missing-stat")]
        [TestCase("missing-stat-ratio")]
        [TestCase("missing-stage-weapon")]
        [TestCase("null-stats")]
        [TestCase("missing-cost")]
        [TestCase("null-cost")]
        [TestCase("string-cost")]
        [TestCase("fractional-cost")]
        [TestCase("float-integral-cost")]
        [TestCase("overflow-cost")]
        [TestCase("negative-cost")]
        [TestCase("unknown-cost")]
        [TestCase("missing-capacity")]
        [TestCase("missing-ascension")]
        [TestCase("missing-ascension-add")]
        [TestCase("missing-events")]
        [TestCase("empty-events")]
        [TestCase("dropped-event")]
        [TestCase("missing-event-count")]
        [TestCase("missing-effect-key")]
        [TestCase("missing-need")]
        [TestCase("null-choice")]
        [TestCase("empty-choices")]
        [TestCase("unknown-effect")]
        [TestCase("duplicate-event-id")]
        [TestCase("unresolved-curse")]
        [TestCase("unresolved-card-rarity")]
        [TestCase("unresolved-stage-weapon")]
        [TestCase("sha-mismatch")]
        [TestCase("missing-sha")]
        [TestCase("version-mismatch")]
        [TestCase("missing-version")]
        [TestCase("schema")]
        [TestCase("type-metadata")]
        [TestCase("unknown-member")]
        [TestCase("duplicate-member")]
        [TestCase("trailing-content")]
        [TestCase("comment")]
        public void MalformedSidecarFailsClosed(string mutation)
        {
            Assert.That(Application.isPlaying, Is.True);
            string json = Mutate(SidecarText(), mutation);
            var error = Assert.Throws<InvalidDataException>(() => ClientRunContent.FromJson(json, ClientBattleContent.Load()));
            StringAssert.StartsWith("run-content: ", error!.Message);
        }

        static string Mutate(string json, string mutation)
        {
            switch (mutation)
            {
                case "duplicate-member": return "{\"CapacityBase\":" + (int)Parse(json)["CapacityBase"]! + "," + json.TrimStart().Substring(1);
                case "trailing-content": return json + "{}";
                case "comment": return "/* note */" + json;
            }
            var root = Parse(json);
            var stats = (JObject)root["Stats"]!; var costs = (JObject)root["Costs"]!; var events = (JArray)root["Events"]!;
            JObject Choice(int e, int c) => (JObject)events[e]["Choices"]![c]!;
            switch (mutation)
            {
                case "missing-stat": stats.Remove("XpSqDiv"); break;
                case "missing-stat-ratio": stats.Remove("WeaponXpRatioDen"); break;
                case "missing-stage-weapon": stats.Remove("StageRefWeapon"); break;
                case "null-stats": root["Stats"] = JValue.CreateNull(); break;
                case "missing-cost": costs.Remove("Upgrade"); break;
                case "null-cost": costs["Upgrade"] = JValue.CreateNull(); break;
                case "string-cost": costs["Upgrade"] = new JValue("1"); break;
                case "fractional-cost": costs["Upgrade"] = new JValue(1.5); break;
                case "float-integral-cost": costs["Upgrade"] = new JValue(1.0); break;
                case "overflow-cost": costs["Upgrade"] = new JValue(4294967297L); break;
                case "negative-cost": costs["Upgrade"] = new JValue(-1); break;
                case "unknown-cost": costs["Discount"] = new JValue(0); break;
                case "missing-capacity": root.Remove("CapacityBase"); break;
                case "missing-ascension": root.Remove("AscensionCapacity"); break;
                case "missing-ascension-add": ((JObject)root["AscensionCapacity"]![0]!).Remove("Add"); break;
                case "missing-events": root.Remove("Events"); break;
                case "empty-events": events.RemoveAll(); break;
                case "dropped-event": events.Last!.Remove(); break;
                case "missing-event-count": root.Remove("SourceEventCount"); break;
                // ev_promise "대가를 치른다": coins -40 with need.coins 40 must not become free.
                case "missing-effect-key": ((JObject)Choice(0, 1)["Effects"]!).Remove("Coins"); break;
                case "missing-need": Choice(0, 1).Remove("RequiredCoins"); break;
                case "null-choice": ((JArray)events[0]["Choices"]!)[0] = JValue.CreateNull(); break;
                case "empty-choices": ((JArray)events[0]["Choices"]!).RemoveAll(); break;
                case "unknown-effect": ((JObject)Choice(0, 0)["Effects"]!)["Gems"] = new JValue(1); break;
                case "duplicate-event-id": events[1]["Id"] = events[0]["Id"]!.DeepClone(); break;
                // ev_merchant "얼룩을 판다" adds the smudge curse; ev_archive "한 권 빌린다" draws a rarity-A card.
                case "unresolved-curse": ((JObject)Choice(3, 1)["Effects"]!)["Curse"] = new JValue("missing_curse"); break;
                case "unresolved-card-rarity": ((JObject)Choice(4, 1)["Effects"]!)["Card"] = new JValue("Z"); break;
                case "unresolved-stage-weapon": stats["StageRefWeapon"] = new JValue("UR"); break;
                case "sha-mismatch":
                    string sha = (string)root["SourceSha256"]!;
                    root["SourceSha256"] = (sha[0] == '0' ? "1" : "0") + sha.Substring(1); break;
                case "missing-sha": root.Remove("SourceSha256"); break;
                case "version-mismatch": root["DataVersion"] = new JValue("0.0.0-mismatch"); break;
                case "missing-version": root.Remove("DataVersion"); break;
                case "schema": root["Schema"] = new JValue("mf-unity-run-content-v0"); break;
                case "type-metadata": root.AddFirst(new JProperty("$type", "MemoryForge.Core.RunData, MemoryForge.Core")); break;
                case "unknown-member": root["Battle"] = new JObject(); break;
                default: throw new ArgumentException("unknown mutation " + mutation);
            }
            return root.ToString(Formatting.None);
        }

        [Test]
        public void EveryLoadReturnsFreshDataWithoutASharedMutableCache()
        {
            Assert.That(Application.isPlaying, Is.True);
            var first = ClientRunContent.Load(); var second = ClientRunContent.Load();
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Memory.Battle, Is.Not.SameAs(second.Memory.Battle));
            Assert.That(first.Stats, Is.Not.SameAs(second.Stats));
            Assert.That(first.Memory.Costs, Is.Not.SameAs(second.Memory.Costs));
            Assert.That(first.Events, Is.Not.SameAs(second.Events));
            var expected = Plain(second.Events); int upgrade = second.Memory.Costs.Upgrade, levelCap = second.Stats.LevelCap, rules = second.Memory.AscensionCapacity.Count;
            first.Memory.Costs.Upgrade = 0; first.Stats.LevelCap = 1; first.Memory.AscensionCapacity.Clear(); first.Events[0].Choices.Clear(); first.Events.Clear();
            first.Memory.Battle.Cards.Clear(); first.Memory.StartingCommon.Clear();
            foreach (var data in new[] { second, ClientRunContent.Load() })
            {
                Assert.That(data.Memory.Costs.Upgrade, Is.EqualTo(upgrade)); Assert.That(data.Stats.LevelCap, Is.EqualTo(levelCap));
                Assert.That(data.Memory.AscensionCapacity.Count, Is.EqualTo(rules)); Assert.That(JToken.DeepEquals(Plain(data.Events), expected), Is.True);
                Assert.That(data.Memory.Battle.Cards, Is.Not.Empty); Assert.That(data.Memory.StartingCommon, Is.Not.Empty);
            }
        }
    }
}
