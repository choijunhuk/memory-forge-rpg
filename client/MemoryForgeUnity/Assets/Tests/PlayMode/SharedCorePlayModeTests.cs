using System.Collections.Generic;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;

namespace MemoryForge.PlayModeTests
{
    public class SharedCorePlayModeTests
    {
        [Test]
        public void SharedCoreHashesTheSameJsonInTheRunningPlayer()
        {
            Assert.That(Application.isPlaying, Is.True);
            Assert.That(CanonicalState.Hash(null), Is.EqualTo("74234e98afe7498fb5daf1f36ac2d78acc339464f950703b8c019892f982b90b"));
            var state = new Dictionary<string, object>
            {
                ["2"] = new[] { 3, 1 }, ["10"] = "기억", ["log"] = new object()
            };
            Assert.That(CanonicalState.Serialize(state), Is.EqualTo("{\"10\":\"기억\",\"2\":[3,1]}"));
        }

        [Test]
        public void SharedRngAndReservationsKeepTheirStateAcrossARealTurn()
        {
            Assert.That(Application.isPlaying, Is.True);
            var rng = new Rng(777);
            rng.Next();
            var resumed = new Rng(rng.State);
            Assert.That(resumed.Next(), Is.EqualTo(rng.Next()));
            var book = new ReservationBook();
            book.Add("card-a", "enemy-a", 1, "after");
            Assert.That(book.StartTurn(2), Is.Empty);
            Assert.That(book.Used("next", 2), Is.EqualTo(1));
            var due = book.StartTurn(3);
            Assert.That(due.Count, Is.EqualTo(1));
            Assert.That(due[0].CardUid, Is.EqualTo("card-a"));
            Assert.That(due[0].Due, Is.EqualTo(3));
            Assert.That(book.Snapshot(), Is.Empty);
        }
    }
}
