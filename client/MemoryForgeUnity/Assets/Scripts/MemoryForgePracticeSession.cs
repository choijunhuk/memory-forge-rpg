#nullable enable
using System.Collections.Generic;

namespace MemoryForge.Client
{
    // EXPEDITION-RETURN-UX-20261021: the app-session hand-off between the expedition practice and the home (공방). The practice's
    // memory store lives in its scene's component, so a scene change alone would drop it. Only the expedition's explicit 공방으로
    // 돌아가기 holds it here (the store graph exactly as saved, with version, history and receipts, the last save, the departure
    // numbering and the chosen length), and only the home's successful explicit departure (the length chosen in 출격 준비) consumes it
    // in the next expedition component. A failed departure keeps it pending; entering or returning to the home creates no Run. It is
    // memory of this app session only: no PlayerPrefs, file, account or server write, so closing the app discards it as before.
    public static class MemoryForgePracticeSession
    {
        // What a returning practice hands over: detached copies, so nothing the old component still holds can change them.
        internal sealed class Held
        {
            public Dictionary<string, object?> Store = null!;
            public Dictionary<string, object?>? LastSave;
            public int LastSaveCount, Departures, RequestSerial;
            public string Length = "short";
        }

        static Held? held;
        static string? notice;

        /// <summary>True while a returned practice's session memory waits for the next departure from the home.</summary>
        public static bool HasHeldPractice => held != null;
        /// <summary>The length of the waiting practice's last departure (the home may preselect it); null when none waits.</summary>
        public static string? HeldLength => held?.Length;
        /// <summary>The session memory version the waiting practice will depart from; null when none waits.</summary>
        public static long? HeldMemoryVersion
        {
            get
            {
                if (held == null) return null;
                held.Store.TryGetValue("version", out var version);
                return version switch { long n => n, int n => n, _ => 0 };
            }
        }

        /// <summary>The home calls this once after it shows: one short line of what the return actually did (the expedition's
        /// outcome and its memory result), or null when nothing returned since the last call. Reading it clears it.</summary>
        public static string? ConsumeReturnNotice()
        {
            string? line = notice; notice = null; return line;
        }

        /// <summary>Forgets any held practice and unread notice. For tests only: a real session keeps them until the next departure.</summary>
        public static void ResetForTests() { held = null; notice = null; MemoryForgeLobbyClient.ReturnCompanion = null; }

        internal static Held? Peek() => held;
        internal static string? PeekNotice() => notice;
        internal static void Hold(Held? state, string? line) { held = state; notice = line; }
        internal static Held? Take()
        {
            var state = held; held = null; return state;
        }
    }
}
