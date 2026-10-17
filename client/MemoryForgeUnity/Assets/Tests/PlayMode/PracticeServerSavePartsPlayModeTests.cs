#if UNITY_5_3_OR_NEWER
// P1-SERVER-SAVE-PARTS-TESTS (SAVE-081-12) §8.12/§14.7/§17.4: view checks of attaching a held part on the terminal save screen of
// MemoryForgeServerPracticeClient (OpenSaveParts/CloseSaveParts/SavePartsOpen/ApplySavePartAsync, buttons SavePart:<partId> and
// SavePartTarget:<key>) over the shared PROTOCOL FIXTURE of PracticeServerSessionPlayModeTests and the save fixtures of
// PracticeServerSavePlayModeTests (World, post, View/Until/Refused/Sent, SaveRun/PreviewReply/Saved/SaveServer/Mutations/Previews;
// nothing is copied). e_pierce (engraving, 관통) and c_precise (circuit, 정밀 회로) are the real content ids the view names; card ids,
// keys and change rows are fixture data. The fixture's applyPart commit stands in for Core's ApplyFoundPart (first matching copy leaves
// partsFound, the part takes its slot, nothing is refunded) and its preview answers carry the attached parts; they are protocol data,
// not Core rules or server acceptance (Root runs the actual host and the real owned run separately). Every wait is bounded; held
// replies are released in finally. NOT_RUN by the writing job: Root runs them in the actual Unity Editor.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        const string Pierce = "e_pierce", Precise = "c_precise";

        [UnityTest]
        public IEnumerator AnOwnedPartGoesOntoAPublishedCardOnceAndOnlyTheServersRebuiltSaveIsShown()
        {
            PartsRun(Pierce, Precise, Pierce);
            var posted = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            post = SaveServer(PartPreview, request =>
            {
                if (request.Path == PracticeCommandOutbox.MemoryCommitRoute(RunA)) return Saved();
                posted.TrySetResult(true);
                return Applied(Pierce, "g1", release.Task);
            });
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.CanCommitSave, Is.True);

            // The sheet is local: while it is open nothing is chosen, assigned or saved; closing it sends nothing and keeps the selection
            // and its preview.
            Assert.That(DrawnButton(view, "OpenSaveParts"), Is.Not.Null);
            Assert.That(view.OpenSaveParts(), Is.True);
            Assert.That(view.SavePartsOpen, Is.True);
            Assert.That(view.ToggleChange(SeraUp), Is.False);
            Assert.That(view.OpenAssign(), Is.False);
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            // One button per distinct owned id with its count (two copies of e_pierce are two parts); no card before a part is chosen.
            Assert.That(Buttons(view, "SavePart:").Select(b => b.name), Is.EquivalentTo(new[] { "SavePart:" + Pierce, "SavePart:" + Precise }));
            Assert.That(ButtonText(DrawnButton(view, "SavePart:" + Pierce)!), Does.Contain("관통").And.Contain("2"));
            Assert.That(ButtonText(DrawnButton(view, "SavePart:" + Precise)!), Does.Contain("정밀 회로"));
            Assert.That(Buttons(view, "SavePartTarget:"), Is.Empty);
            view.CloseSaveParts();
            Assert.That(view.SavePartsOpen, Is.False);
            Assert.That(view.SaveSelection, Is.EqualTo(new[] { RinAdd }));
            Assert.That(view.CanCommitSave, Is.True, "cancelling the sheet kept the current preview");
            Assert.That(Mutations(), Is.Empty);

            // Choosing a part offers every published non-weapon card, never the weapon.
            Assert.That(view.OpenSaveParts(), Is.True);
            DrawnButton(view, "SavePart:" + Pierce)!.onClick.Invoke();
            Assert.That(Buttons(view, "SavePartTarget:").Select(b => b.name),
                Is.EquivalentTo(new[] { "SavePartTarget:k1", "SavePartTarget:g1", "SavePartTarget:c1" }));
            var target = DrawnButton(view, "SavePartTarget:g1")!;
            try
            {
                target.onClick.Invoke();
                // The earlier selection and preview were dropped before sending: only a preview asked afterwards may authorize a save.
                Assert.That(view.SaveSelection, Is.Empty);
                Assert.That(view.SavePreview, Is.Null);
                yield return Until(() => posted.Task.IsCompleted, "the part is posted");
                // The same (now stale) button, the other copy of the same part and every save action are refused at once.
                target.onClick.Invoke();
                Assert.That(Refused(view.ApplySavePartAsync(Pierce, "k1")), Is.True);
                Assert.That(Refused(view.CommitSaveAsync()), Is.True);
                Assert.That(view.ToggleChange(SeraUp), Is.False);
                // Nothing is shown as attached before the server answers.
                Assert.That(Owned(view), Is.EqualTo(new[] { Pierce, Precise, Pierce }));
                Assert.That(ShownCard(view, "g1")["mods"], Is.Null);
                release.TrySetResult(true);
                yield return Until(() => !view.Busy && view.Snapshot!.Context.ServerVersion == 4, "the part resolves");
            }
            finally { release.TrySetResult(true); }
            yield return null;
            yield return null;
            Assert.That(Mutations().Length, Is.EqualTo(1), "the second click sent nothing");
            Sent(Mutations().Single(), "applyPart", 3, new JObject { ["partId"] = Pierce, ["key"] = "g1" });
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.SavePartsOpen, Is.False, "the sheet of the older context is gone");
            // The run moved; the memory did not: no save by itself and the account memory version is unchanged.
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
            Assert.That(Owned(view), Is.EqualTo(new[] { Precise, Pierce }), "exactly one of the two copies was used");
            Assert.That(ShownCard(view, "g1")["mods"]!["e"]!.Value<string>(), Is.EqualTo(Pierce));

            // The server's rebuilt change list and a fresh preview of no selection at the new version; the part rides on the add change.
            yield return Previewed(view);
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.Empty);
            Assert.That(view.SavePreview.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(Drawn(view), Does.Contain("g1 add · 부품 " + Pierce));
            Assert.That(Drawn(view), Does.Not.Contain("[관통]"));
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(Drawn(view), Does.Contain("[관통]"), "the attached part comes from the server's preview deck");
            var saving = view.CommitSaveAsync();
            yield return Until(() => saving.IsCompleted, "the save resolves");
            Assert.That(saving.Result, Is.True);
            SentSave(Mutations()[1], 4, 2, SeraAdd);
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("DONE"));
            Assert.That(Mutations().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator UnownedMissingOrWeaponChoicesAreRefusedLocallyAndTheSheetKeepsNoChoiceAcrossCancelRefreshOrLeave()
        {
            // A long inventory of one id and a long deck: one button with its count, every non-weapon card reachable.
            PartsRun(Enumerable.Repeat(Precise, 12).ToArray());
            var deck = (JArray)world.Extra!["deck"]!;
            for (int i = 0; i < 14; i++) deck.Add(new JObject { ["key"] = "n" + i, ["id"] = "strike", ["owner"] = "sera" });
            post = SaveServer(PartPreview, request => Error(500, "INTERNAL_ERROR"));
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.OpenSaveParts(), Is.True);
            Assert.That(Buttons(view, "SavePart:").Select(b => b.name), Is.EqualTo(new[] { "SavePart:" + Precise }));
            Assert.That(ButtonText(DrawnButton(view, "SavePart:" + Precise)!), Does.Contain("정밀 회로").And.Contain("12"));

            // Only an owned part and a published non-weapon card are sent; a refused local gate sends and drops nothing.
            Assert.That(Refused(view.ApplySavePartAsync(Pierce, "g1")), Is.True, "a part that is not owned");
            Assert.That(Refused(view.ApplySavePartAsync("p-unknown", "g1")), Is.True, "an id outside the inventory");
            Assert.That(Refused(view.ApplySavePartAsync(Precise, "w1")), Is.True, "a weapon card");
            Assert.That(Refused(view.ApplySavePartAsync(Precise, "k9")), Is.True, "a card the deck does not publish");
            Assert.That(view.SaveSelection, Is.EqualTo(new[] { SeraAdd }));
            Assert.That(Mutations(), Is.Empty);

            DrawnButton(view, "SavePart:" + Precise)!.onClick.Invoke();
            var targets = Buttons(view, "SavePartTarget:");
            var keys = new[] { "k1", "g1", "c1" }.Concat(Enumerable.Range(0, 14).Select(i => "n" + i));
            Assert.That(targets.Select(b => b.name), Is.EquivalentTo(keys.Select(k => "SavePartTarget:" + k)));
            var corners = new Vector3[4];
            foreach (var row in targets)
            {
                var scroll = row.GetComponentInParent<UnityEngine.UI.ScrollRect>();
                Assert.That(scroll, Is.Not.Null, row.name + " sits in a scroll");
                var content = scroll.content;
                ((RectTransform)row.transform).GetWorldCorners(corners);
                Assert.That(content.InverseTransformPoint(corners[0]).y, Is.GreaterThanOrEqualTo(content.rect.yMin - 0.5f), row.name + " ends inside the content");
                Assert.That(content.InverseTransformPoint(corners[1]).y, Is.LessThanOrEqualTo(content.rect.yMax + 0.5f), row.name + " starts inside the content");
            }

            // Cancel: a reopened sheet has no part chosen.
            view.CloseSaveParts();
            Assert.That(view.OpenSaveParts(), Is.True);
            Assert.That(Buttons(view, "SavePartTarget:"), Is.Empty);

            // Another context: the refresh closes the sheet unsent and drops its choice.
            DrawnButton(view, "SavePart:" + Precise)!.onClick.Invoke();
            world.Version = 4;
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(view.SavePartsOpen, Is.False);
            yield return Previewed(view);
            Assert.That(view.OpenSaveParts(), Is.True);
            Assert.That(Buttons(view, "SavePartTarget:"), Is.Empty);

            // Leaving drops it with the session; the new session starts without a sheet.
            DrawnButton(view, "SavePart:" + Precise)!.onClick.Invoke();
            view.enabled = false;
            Assert.That(view.SavePartsOpen, Is.False);
            view.enabled = true;
            yield return Previewed(view);
            Assert.That(view.SavePartsOpen, Is.False);
            Assert.That(view.OpenSaveParts(), Is.True);
            Assert.That(Buttons(view, "SavePartTarget:"), Is.Empty);
            Assert.That(Mutations(), Is.Empty, "no choice above was ever sent");
        }

        [UnityTest]
        public IEnumerator ACoreRefusalOfAPartChangesNothingShownAndOnlyAFreshPreviewReopensSaving()
        {
            PartsRun(Pierce, Pierce);
            // The fixture stands in for Core's verdict on the part (its NO_PART/BAD_CARD/INVALID_PART are Core's, not the view's).
            post = SaveServer(PartPreview, request => Error(422, "NO_PART"));
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.OpenSaveParts(), Is.True);
            var refused = view.ApplySavePartAsync(Pierce, "k1");
            Assert.That(view.SaveSelection, Is.Empty, "the selection was dropped before sending");
            Assert.That(view.SavePreview, Is.Null);
            yield return Until(() => refused.IsCompleted, "the refused part resolves");
            Assert.That(refused.Result, Is.False);
            Sent(Mutations().Single(), "applyPart", 3, new JObject { ["partId"] = Pierce, ["key"] = "k1" });
            Assert.That(Outbox().Load(), Is.Null, "a proven Core refusal clears the request");
            Assert.That(view.Notice, Does.Contain("반영되지 않았어요"));
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(3));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
            Assert.That(Owned(view), Is.EqualTo(new[] { Pierce, Pierce }));
            Assert.That(ShownCard(view, "k1")["mods"], Is.Null);
            Assert.That(view.SavePreview == null || view.SavePreview.SelectedChangeIds.Count == 0, Is.True, "the sent selection is not reused");
            yield return Previewed(view);
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.Empty);
            Assert.That(view.CanCommitSave, Is.True);

            // Nothing was used: the same copy is still the player's, and its later commit uses exactly one of the two.
            post = SaveServer(PartPreview, request => Applied(Pierce, "k1"));
            if (!view.SavePartsOpen) Assert.That(view.OpenSaveParts(), Is.True);
            var applying = view.ApplySavePartAsync(Pierce, "k1");
            yield return Until(() => applying.IsCompleted, "the part resolves");
            Assert.That(applying.Result, Is.True);
            Sent(Mutations()[1], "applyPart", 3, new JObject { ["partId"] = Pierce, ["key"] = "k1" });
            Assert.That(Owned(view), Is.EqualTo(new[] { Pierce }));
            Assert.That(ShownCard(view, "k1")["mods"]!["e"]!.Value<string>(), Is.EqualTo(Pierce));
            Assert.That(view.Snapshot!.Account.MemoryVersion, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator EachApplicationUsesOneCopyReplacesOnlyItsSlotAndRefundsNothing()
        {
            PartsRun(Pierce, Precise, Pierce);
            post = SaveServer(PartPreview, request =>
            {
                var args = JObject.Parse(request.Text)["args"]!;
                return Applied(args["partId"]!.Value<string>()!, args["key"]!.Value<string>()!);
            });
            var view = View();
            yield return Previewed(view);
            // An engraving, then a circuit beside it, then the second engraving copy over the first: each at the version it was sent from.
            var steps = new[] { (Pierce, 3, new[] { Precise, Pierce }), (Precise, 4, new[] { Pierce }), (Pierce, 5, Array.Empty<string>()) };
            foreach (var (part, version, left) in steps)
            {
                if (!view.SavePartsOpen) Assert.That(view.OpenSaveParts(), Is.True, part);
                var applying = view.ApplySavePartAsync(part, "g1");
                yield return Until(() => applying.IsCompleted, "the part resolves");
                Assert.That(applying.Result, Is.True, part);
                Sent(Mutations().Last(), "applyPart", version, new JObject { ["partId"] = part, ["key"] = "g1" });
                Assert.That(Owned(view), Is.EqualTo(left), part);
                yield return Previewed(view);
                Assert.That(view.SavePreview!.Context.ServerVersion, Is.EqualTo(version + 1));
            }
            var mods = (JObject)ShownCard(view, "g1")["mods"]!;
            Assert.That(mods["c"]!.Value<string>(), Is.EqualTo(Precise), "the circuit stays when the engraving is replaced");
            Assert.That(mods["e"]!.Value<string>(), Is.EqualTo(Pierce));
            Assert.That(Owned(view), Is.Empty, "the replaced engraving was not given back");
            // An empty inventory has no sheet and nothing to send.
            Assert.That(view.OpenSaveParts(), Is.False);
            Assert.That(view.SavePartsOpen, Is.False);
            Assert.That(Refused(view.ApplySavePartAsync(Pierce, "k1")), Is.True);
            Assert.That(Mutations().Length, Is.EqualTo(3));
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
            Assert.That(view.Snapshot.Context.ServerVersion, Is.EqualTo(6));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2), "only the run's version moved");
        }

        [UnityTest]
        public IEnumerator AnUncertainPartSendLocksTheSaveAndItsExactRetryNeverUsesTheSecondCopy()
        {
            PartsRun(Pierce, Pierce);
            byte[] receipt = Array.Empty<byte>();
            // The server commits the part and its reply never arrives.
            post = SaveServer(PartPreview, request => { receipt = Applied(Pierce, "g1").Body; return new Reply(200, receipt) { Drop = true }; });
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.OpenSaveParts(), Is.True);
            var applying = view.ApplySavePartAsync(Pierce, "g1");
            yield return Until(() => applying.IsCompleted, "the lost reply resolves");
            Assert.That(applying.Result, Is.False);
            Sent(Mutations().Single(), "applyPart", 3, new JObject { ["partId"] = Pierce, ["key"] = "g1" });
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(Mutations()[0].Text));
            Assert.That(view.SessionState!.Pending, Is.Not.Null);
            // Nothing is shown as attached while unresolved: the last good state, stale.
            Assert.That(view.SnapshotStale, Is.True);
            Assert.That(Owned(view), Is.EqualTo(new[] { Pierce, Pierce }));
            Assert.That(ShownCard(view, "g1")["mods"], Is.Null);
            // The other copy and every save action are refused before anything is sent; no preview is asked either.
            int previews = Previews().Length;
            Assert.That(view.OpenSaveParts(), Is.False);
            Assert.That(Refused(view.ApplySavePartAsync(Pierce, "k1")), Is.True);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            Assert.That(view.ToggleChange(SeraUp), Is.False);
            Assert.That(view.OpenAssign(), Is.False);
            yield return null;
            yield return null;
            Assert.That(Previews().Length, Is.EqualTo(previews));
            Assert.That(Mutations().Length, Is.EqualTo(1));

            // Reconnect: the new session reads the server's run (one copy already used there) and still holds the stored request.
            view.enabled = false;
            Assert.That(view.SavePartsOpen, Is.False);
            view.enabled = true;
            yield return Until(() => !view.Busy && view.SessionState?.Snapshot?.Context.ServerVersion == 4, "the reconnect reads the server");
            Assert.That(view.SessionState!.Pending, Is.Not.Null);
            Assert.That(Owned(view), Is.EqualTo(new[] { Pierce }));
            Assert.That(view.OpenSaveParts(), Is.False);
            Assert.That(Refused(view.ApplySavePartAsync(Pierce, "k1")), Is.True, "the remaining copy waits for the unresolved request");
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            Assert.That(Mutations().Length, Is.EqualTo(1));

            // The explicit retry sends the stored bytes; the server answers its stored receipt and uses nothing more.
            post = SaveServer(PartPreview, request => new Reply(200, receipt));
            var retry = view.RetryAsync();
            yield return Until(() => retry.IsCompleted, "the retry resolves");
            Assert.That(retry.Result, Is.True);
            Assert.That(Mutations().Length, Is.EqualTo(2));
            Assert.That(Mutations()[1].Body, Is.EqualTo(Mutations()[0].Body));
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(((JArray)world.Extra!["partsFound"]!).Count, Is.EqualTo(1), "the fixture server used one copy in all");
            Assert.That(Owned(view), Is.EqualTo(new[] { Pierce }));
            Assert.That(ShownCard(view, "g1")["mods"]!["e"]!.Value<string>(), Is.EqualTo(Pierce));
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
            yield return Previewed(view);
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.Empty);
            Assert.That(view.SavePreview.Context.ServerVersion, Is.EqualTo(4));
            // The remaining copy is still the player's to attach.
            Assert.That(view.OpenSaveParts(), Is.True);
            Assert.That(DrawnButton(view, "SavePart:" + Pierce), Is.Not.Null);
        }

        // ---- part fixtures (protocol data on the shared World) ----

        // SaveRun's victory save with the held parts in state.partsFound (one entry per copy) and a weapon card w1 beside k1, g1 and c1.
        void PartsRun(params string[] owned)
        {
            SaveRun(defeat: false);
            world.Extra!["partsFound"] = new JArray(owned.Cast<object>().ToArray());
            ((JArray)world.Extra["deck"]!).Add(new JObject { ["key"] = "w1", ["id"] = "blade", ["owner"] = "sera", ["wielder"] = "sera", ["weapon"] = true });
        }

        // The fixture's applyPart commit in place of Core's ApplyFoundPart: the run moves to the next version, the FIRST matching copy leaves
        // partsFound, the part takes its content slot of the card's mods (c circuit, e engraving; the other slot stays, nothing is refunded),
        // and the server's rebuilt change rows of that card name it. world.MemoryVersion is untouched. No new change kind is invented.
        Reply Applied(string partId, string key, Task? hold = null) => Commit(state =>
        {
            var owned = (JArray)state["partsFound"]!;
            owned.RemoveAt(owned.Select(p => p.Value<string>()).ToList().IndexOf(partId));
            var card = ((JArray)state["deck"]!).OfType<JObject>().First(c => c["key"]!.Value<string>() == key);
            if (!(card["mods"] is JObject mods)) card["mods"] = mods = new JObject();
            mods[partId == Precise ? "c" : "e"] = partId;
            foreach (var column in ((JObject)state["memory"]!).Properties())
                foreach (var change in ((JArray)column.Value["changes"]!).OfType<JObject>())
                    if (change["key"]!.Value<string>() == key) change["label"] = key + " " + change["kind"]!.Value<string>() + " · 부품 " + partId;
        }, hold);

        // PreviewReply with each listed card's mods as the fixture world's deck now carries them (the server's resulting decks).
        Reply PartPreview(string[] ids)
        {
            var json = JObject.Parse(StrictUtf8.GetString(PreviewReply(ids).Body));
            var deck = ((world.Extra?["deck"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
            foreach (var column in ((JObject)json["preview"]!["memory"]!).Properties())
                foreach (var spec in ((JArray)column.Value).OfType<JObject>())
                    if (deck.FirstOrDefault(c => c["key"]!.Value<string>() == spec["key"]!.Value<string>())?["mods"] is JObject mods) spec["mods"] = mods.DeepClone();
            return new Reply(200, Utf8(json));
        }

        static string?[] Owned(MemoryForgeServerPracticeClient view) => Shown(view)["partsFound"]!.Values<string>().ToArray();
        static JObject ShownCard(MemoryForgeServerPracticeClient view, string key) =>
            ((JArray)Shown(view)["deck"]!).OfType<JObject>().First(c => c["key"]!.Value<string>() == key);
        static List<UnityEngine.UI.Button> Buttons(MemoryForgeServerPracticeClient view, string prefix) =>
            view.GetComponentsInChildren<UnityEngine.UI.Button>().Where(b => b.name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        static string ButtonText(UnityEngine.UI.Button button) =>
            string.Join("\n", button.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text));
    }
}
#endif
