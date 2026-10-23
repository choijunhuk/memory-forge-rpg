using System;
using System.Collections.Generic;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    static class RecruitmentGrants
    {
        // The same SR duplicate row as Gacha.ApplyOwnership; no draw, pity advance, ticket spend or separate receipt.
        public static List<PullResult> ApplyState(GachaConfig cfg, GachaState state, int chapter)
        {
            AccountPolicy.Stage(chapter);
            state.Crystals = checked(state.Crystals + AccountPolicy.Value(chapter, "freeCrystals"));
            state.Tickets[RecruitmentContent.LaunchTicket] = checked(state.Tickets[RecruitmentContent.LaunchTicket] + AccountPolicy.Value(chapter, "standardTicket"));
            var results = new List<PullResult>();
            if (chapter != 0) return results;
            foreach (string id in AccountPolicy.Starters)
            {
                state.OwnedChars.TryGetValue(id, out int copies);
                if (copies >= RecruitmentContent.MaxCopies) throw new InvalidOperationException("starter copies limit");
                int shards = copies == 0 ? 0 : copies >= 7 ? cfg.ShardSrOverflow : cfg.ShardSr;
                state.OwnedChars[id] = checked(copies + 1); state.Shards = checked(state.Shards + shards);
                results.Add(new PullResult { Kind = "char", Rarity = "SR", Id = id, IsNew = copies == 0, Resonance = Math.Min(6, copies), Shards = shards });
            }
            return results;
        }
        public static void Apply(RecruitmentContent content, AccountEnvelope e, ProgressionFact fact)
        {
            var record = e.Recruitment ?? new RecruitmentRecord(); var g = record.ToState();
            var results = ApplyState(content.Config, g, fact.Chapter);
            record.FreeCrystals = checked(record.FreeCrystals + AccountPolicy.Value(fact.Chapter, "freeCrystals")); record.FromState(g);
            record.Ledger.Add(new LedgerEntry { Kind = LedgerEntry.Grant, RequestId = fact.RequestId, Revision = fact.Revision,
                Chapter = fact.Chapter, PolicyVersion = fact.PolicyVersion, Results = results });
            e.Recruitment = record;
        }
    }
}
