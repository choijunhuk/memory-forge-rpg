using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MemoryForge.Core;

// Test-bot policy only. The live Core has no public constructor for an existing BattleState.
static class SupportGaugeBot
{
    static readonly ConstructorInfo Restore = typeof(Battle).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(BattleData), typeof(BattleState) }, null)
        ?? throw new MissingMethodException("Battle(BattleData, BattleState) test probe");
    static readonly MethodInfo CopyState = typeof(BattleState).GetMethod("Copy", BindingFlags.Instance | BindingFlags.NonPublic, null,
        Type.EmptyTypes, null) ?? throw new MissingMethodException("BattleState.Copy() test probe");

    public static bool Allows(BattleData data, BattleState state, string uid, string target)
    {
        var trial = (Battle)Restore.Invoke(new object[] { data, (BattleState)CopyState.Invoke(state, null)! });
        var args = BattleArgument.Object(new[]
        {
            new KeyValuePair<string, BattleArgument>("uid", BattleArgument.String(uid)),
            new KeyValuePair<string, BattleArgument>("mode", BattleArgument.String("now")),
            new KeyValuePair<string, BattleArgument>("target", BattleArgument.String(target))
        });
        try
        {
            trial.Command(new BattleRequest(null, state.V, "play", args));
            var after = trial.State;
            if (after.Phase != "PLAYER" || !after.Actors.Any(a => a.Gauge > state.Actors.Single(before => before.Id == a.Id).Gauge)) return false;
            if (after.Actors.Any(a => a.Gauge >= 100)) return true;
            trial.Command(new BattleRequest(null, after.V, "end"));
            return trial.State.Phase == "PLAYER";
        }
        catch (RuleException) { return false; }
    }
}
