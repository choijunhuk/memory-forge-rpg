// Phase2 combat balance (docs/RULES_NOW_PLAY.md §6, user-authorized 2026-10-23): one immutable, optional, launch-only difficulty preset.
// A fresh owned story launch records the preset's identity ({"id", "sha256"}) in its Run record AND, independently, in its launch fact, and
// its Run starts with the preset's difficulty. Validation takes the expected preset from the fact. A launch (and fact) without the member
// is a legacy launch and keeps the original 100/100. Core, data/game_data.json, the account policies v1-v4, every
// existing launch builder and the support, equipment and growth registries are unchanged; tutorial, growth and weekly battles never use it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    /// <summary>The launch member "balancePolicy": exactly {"id", "sha256"} naming the one registered preset. Null, another shape, a
    /// missing or extra key, or an unknown version or hash is refused.</summary>
    sealed class LaunchBalancePolicy
    {
        public string Id = "", Sha256 = "";

        public Dictionary<string, object?> ToGraph() => GraphJson.Object("id", Id, "sha256", Sha256);

        public static LaunchBalancePolicy Parse(JsonElement j)
        {
            if (j.ValueKind != JsonValueKind.Object) throw new InvalidDataException("launch balance policy shape");
            BattleDataLoader.Fields(j, "id", "sha256");
            var policy = new LaunchBalancePolicy { Id = Envelopes.Text(j, "id"), Sha256 = Envelopes.Hex(j, "sha256") };
            ExpeditionBalancePolicy.RequireIdentity(policy);
            return policy;
        }
    }

    static class ExpeditionBalancePolicy
    {
        public const string Version = "expedition-balance-20261023-v1";
        public const string Hash = "da8a49e5a31a46e303ec3456b078d8c7c76ce8072e467f43ca847cc293f729d4";
        // The only content this preset was measured on: the unchanged data/game_data.json the account policies are bound to.
        public const string MainContentSha256 = "b6d3543a1d87f0a28e4824e0c1ae45a1eabc676d7abefcd2e57e23b4abc0054a";
        public const string Mode = "story";
        public const int Hp = 130, Damage = 110;
        const string Resource = "MemoryForge.Server.ExpeditionBalanceV1.json";

        static readonly object Gate = new object();
        static bool verified;

        /// <summary>Loads the embedded policy once and proves its exact bytes and members. Failures are InvalidDataException (never a
        /// type-initializer wrapper), so startup can refuse with its own message.</summary>
        public static void Verify()
        {
            lock (Gate)
            {
                if (verified) return;
                byte[] bytes = ReadResource();
                if (GraphJson.Sha256(bytes) != Hash) throw new InvalidDataException("expedition balance policy hash");
                CheckSource(bytes);
                verified = true;
            }
        }

        static byte[] ReadResource()
        {
            using var input = typeof(ExpeditionBalancePolicy).Assembly.GetManifestResourceStream(Resource)
                ?? throw new InvalidDataException("missing expedition balance policy");
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }

        static void CheckSource(byte[] bytes)
        {
            bool valid;
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                BattleDataLoader.Fields(root, "version", "mainContentSha256", "modes", "difficulty");
                var modes = root.GetProperty("modes");
                var difficulty = root.GetProperty("difficulty");
                BattleDataLoader.Fields(difficulty, "hp", "dmg");
                valid = Envelopes.Text(root, "version") == Version
                    && Envelopes.Hex(root, "mainContentSha256") == MainContentSha256
                    && modes.ValueKind == JsonValueKind.Array && modes.GetArrayLength() == 1
                    && modes[0].ValueKind == JsonValueKind.String && modes[0].GetString() == Mode
                    && difficulty.GetProperty("hp").TryGetInt32(out int hp) && hp == Hp
                    && difficulty.GetProperty("dmg").TryGetInt32(out int damage) && damage == Damage;
            }
            catch (Exception error) when (error is not InvalidDataException) { throw new InvalidDataException("expedition balance policy source"); }
            if (!valid) throw new InvalidDataException("expedition balance policy source");
        }

        public static void RequireIdentity(LaunchBalancePolicy policy)
        {
            Verify();
            if (policy.Id != Version || policy.Sha256 != Hash) throw new InvalidDataException("unknown expedition balance policy");
        }

        /// <summary>A recorded preset is valid only on the kind of launch Stamp produces: a v4 story launch of the bound content.</summary>
        public static void RequireLaunch(ExpeditionLaunch launch)
        {
            if (launch.BalancePolicy == null) throw new InvalidDataException("expedition balance policy absent");
            RequireIdentity(launch.BalancePolicy);
            if (launch.Mode != Mode || launch.PolicyVersion != SupportPolicy.Version || launch.PolicyHash != SupportPolicy.Hash
                || launch.ContentSha256 != MainContentSha256) throw new InvalidDataException("expedition balance launch");
        }

        /// <summary>A launch fact may pin the preset only when it is the v4 story launch fact Launch writes; grants never parse it.</summary>
        public static void RequireFact(ProgressionFact fact)
        {
            if (fact.BalancePolicy == null) throw new InvalidDataException("expedition balance policy absent");
            RequireIdentity(fact.BalancePolicy);
            if (fact.Kind != "launch" || fact.Request == null || fact.Request.Mode != Mode
                || fact.PolicyVersion != SupportPolicy.Version || fact.PolicyHash != SupportPolicy.Hash) throw new InvalidDataException("expedition balance launch fact");
        }

        /// <summary>The launch fact's independent copy of the preset its Run record holds (null stays null).</summary>
        public static LaunchBalancePolicy? Copy(LaunchBalancePolicy? policy)
        {
            if (policy == null) return null;
            RequireIdentity(policy);
            return new LaunchBalancePolicy { Id = policy.Id, Sha256 = policy.Sha256 };
        }

        static void RequireContent(PracticeContent content)
        {
            if (content.ContentSha256 != MainContentSha256) throw new InvalidDataException("expedition balance main content");
        }

        /// <summary>Records the preset on a launch the unchanged v4 builder just produced for a fresh story departure.</summary>
        public static void Stamp(PracticeContent content, ExpeditionLaunch launch)
        {
            Verify();
            RequireContent(content);
            if (launch.BalancePolicy != null) throw new InvalidOperationException("the launch already records a balance policy");
            launch.BalancePolicy = new LaunchBalancePolicy { Id = Version, Sha256 = Hash };
            RequireLaunch(launch);
        }

        /// <summary>Validation: re-applies the launch fact's parsed preset to the launch rebuilt by its historical builder.</summary>
        public static void Apply(PracticeContent content, ExpeditionLaunch expected, LaunchBalancePolicy recorded)
        {
            RequireIdentity(recorded);
            RequireContent(content);
            if (expected.BalancePolicy != null) throw new InvalidDataException("rebuilt launch already records a balance policy");
            expected.BalancePolicy = new LaunchBalancePolicy { Id = recorded.Id, Sha256 = recorded.Sha256 };
            RequireLaunch(expected);
        }

        public static Dictionary<string, object?> DifficultyGraph(LaunchBalancePolicy policy)
        {
            RequireIdentity(policy);
            return GraphJson.Object("hp", Hp, "dmg", Damage);
        }

        public static RunDifficulty Difficulty(LaunchBalancePolicy policy)
        {
            RequireIdentity(policy);
            return new RunDifficulty { Hp = Hp, Damage = Damage };
        }
    }
}
