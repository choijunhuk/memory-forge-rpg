#if UNITY_5_3_OR_NEWER
// P1-SERVER-MEMORY-SAVE-UI §8.9–8.12/§14.7/§17.4: view checks of the terminal memory save in MemoryForgeServerPracticeClient over the
// shared PROTOCOL FIXTURE of PracticeServerSessionPlayModeTests (its World, loopback host, post handler, View/Settled/Until/Sent/Commit
// helpers; nothing is copied). The fixture's memory-preview answers stand in for Core's PreviewMemory: their decks, used/capacities,
// errors and the delta:{cid}:{key}:{kind} change ids are protocol data written here, not content, Core rules or server acceptance (Root
// runs the actual host separately). Unity only: they drive the real uGUI view. Every wait is bounded; held replies are released in finally.
#nullable enable
using System;
using System.Collections;
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
        const string SeraUp = "delta:sera:k1:upgrade", SeraAdd = "delta:sera:g1:add", RinAdd = "delta:rin:c1:add";
        const string PreviewRoute = "/dev/practice/runs/" + RunA + "/memory-preview";

        [UnityTest]
        public IEnumerator AVictorySaveSendsExactlyTheFreshlyPreviewedSelectionOnceAndShowsOnlyTheConfirmedMemory()
        {
            SaveRun(defeat: false);
            var posted = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            post = SaveServer(ids => PreviewReply(ids), request => { posted.TrySetResult(true); return Saved(release.Task); });
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ShowingSave, Is.True);
            // No change chosen: the server's preview of no change is current, so keeping the saved deck can be saved at once.
            var first = view.SavePreview!;
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(first.SelectedChangeIds, Is.Empty);
            Assert.That(first.Context.ServerVersion, Is.EqualTo(3));
            Assert.That(view.CanCommitSave, Is.True);
            var text = Drawn(view);
            Assert.That(text, Does.Contain("출발 전에 있던 기억 1장 — 그대로 유지"));
            Assert.That(text, Does.Contain("변화 없이 저장"));

            // Two picks in another order: the selection is local, asked in the server's order (party, then each list), and the earlier
            // preview no longer counts.
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            Assert.That(view.SavePreview, Is.Null);
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            yield return Previewed(view);
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.SaveSelection, Is.EqualTo(new[] { SeraAdd, RinAdd }));
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.EqualTo(new[] { SeraAdd, RinAdd }));
            Assert.That(Asked(Previews().Last()), Is.EqualTo(new[] { SeraAdd, RinAdd }));
            Assert.That(view.CanCommitSave, Is.True);
            Assert.That(Drawn(view), Does.Contain("고른 변화 2개 저장하고 끝내기"));
            Assert.That(Mutations(), Is.Empty, "choosing and previewing sent no mutation");

            try
            {
                var saving = view.CommitSaveAsync();
                yield return Until(() => posted.Task.IsCompleted, "the save is posted");
                // A second click and every other action are refused at once; the selection is gone once sent.
                Assert.That(Refused(view.CommitSaveAsync()), Is.True);
                Assert.That(view.ToggleChange(SeraUp), Is.False);
                Assert.That(Refused(view.AssignAsync("c1", "sera")), Is.True);
                Assert.That(view.SaveSelection, Is.Empty);
                // Nothing is shown as saved before the server answers.
                Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
                Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
                release.TrySetResult(true);
                yield return Until(() => saving.IsCompleted, "the save resolves");
                Assert.That(saving.Result, Is.True);
            }
            finally { release.TrySetResult(true); }
            SentSave(Mutations().Single(), 3, 2, SeraAdd, RinAdd);
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.ShowingSave, Is.False);
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("DONE"));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(3));
            Assert.That(view.Notice, Does.Contain("고른 변화 2개를 기억에 저장했어요"));
            Assert.That(Drawn(view), Does.Contain("저장된 기억 버전 3"));
            Assert.That(DrawnButton(view, "CreateRun")!.interactable, Is.True, "the next run starts from the saved memory");
            Assert.That(Refused(view.CommitSaveAsync()), Is.True, "no save is open any more");
            Assert.That(Mutations().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ADefeatKeepsTheSavedDeckAndOnlyTheServersOneChangeVerdictClosesSaving()
        {
            SaveRun(defeat: true);
            // The fixture stands in for Core: on a defeat more than one asked change is DEFEAT_KEEP_ONE (across the whole party).
            post = SaveServer(ids => PreviewReply(ids, ids.Length > 1 ? "DEFEAT_KEEP_ONE" : null), request => Saved());
            var view = View();
            yield return Previewed(view);
            var text = Drawn(view);
            Assert.That(text, Does.Contain("출발 전 기억 덱은 그대로"));
            Assert.That(text, Does.Contain("파티 전체에서 1개까지"));
            Assert.That(text, Does.Contain("카드 한 장 단위가 아니라"));
            // The default is keeping the saved deck.
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.CanCommitSave, Is.True);

            // One change of each character is two changes across the party: choosing is local, the server's preview refuses it.
            Assert.That(view.ToggleChange(SeraUp), Is.True);
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.SavePreview!.Error, Is.EqualTo("DEFEAT_KEEP_ONE"));
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            Assert.That(Drawn(view), Does.Contain("지금 2개를 골랐어요"));
            Assert.That(Mutations(), Is.Empty);

            // Dropping one leaves one new change: the server's preview accepts it and exactly that one is saved.
            Assert.That(view.ToggleChange(SeraUp), Is.True);
            yield return Previewed(view);
            var accepted = view.SavePreview!;
            Assert.That(accepted.Error, Is.Null);
            Assert.That(accepted.SelectedChangeIds, Is.EqualTo(new[] { RinAdd }));
            var saving = view.CommitSaveAsync();
            yield return Until(() => saving.IsCompleted, "the save resolves");
            Assert.That(saving.Result, Is.True);
            SentSave(Mutations().Single(), 3, 2, RinAdd);
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("DONE"));
        }

        [UnityTest]
        public IEnumerator CapacityAndDomainRefusalsKeepSavingClosedAndChangeNothing()
        {
            SaveRun(defeat: false);
            string? refuse = null;
            post = SaveServer(ids => refuse != null ? Error(422, refuse) : PreviewReply(ids, ids.Contains(SeraAdd) ? "OVER_CAPACITY" : null, ids.Contains(SeraAdd) ? 8 : 3),
                request => Error(422, "OVER_CAPACITY"));
            var view = View();
            yield return Previewed(view);
            // An id the server never published is not selectable and nothing is asked for it.
            Assert.That(view.ToggleChange("delta:sera:k9:add"), Is.False);
            Assert.That(view.SavePreviewPending, Is.False);

            // The whole resulting deck is over sera's capacity: the server's OVER_CAPACITY closes saving and its numbers are shown.
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.SavePreview!.Error, Is.EqualTo("OVER_CAPACITY"));
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            var text = Drawn(view);
            Assert.That(text, Does.Contain("결과 용량 8/6"));
            Assert.That(text, Does.Contain("결과 덱 전체"));
            Assert.That(Mutations(), Is.Empty);

            // A refused preview (a 422 of the server's own rules) leaves no view: saving stays closed, and only an explicit re-ask repeats it.
            refuse = "INVALID_SELECTION";
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.SavePreview, Is.Null);
            Assert.That(view.SavePreviewFailure, Is.EqualTo("INVALID_SELECTION"));
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Drawn(view), Does.Contain("미리보기를 받지 못했어요"));
            int asked = Previews().Length;
            yield return null;
            yield return null;
            Assert.That(Previews().Length, Is.EqualTo(asked), "a failed preview is not re-asked by itself");
            refuse = null;
            var again = view.PreviewSaveAsync();
            yield return Until(() => again.IsCompleted, "the re-ask resolves");
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.Empty);
            Assert.That(view.CanCommitSave, Is.True);

            // A fresh, error-free preview and still a Core refusal of the save itself: nothing changes and the sent selection is not reused.
            Assert.That(view.ToggleChange(SeraUp), Is.True);
            yield return Previewed(view);
            Assert.That(view.CanCommitSave, Is.True);
            var saving = view.CommitSaveAsync();
            yield return Until(() => saving.IsCompleted, "the refused save resolves");
            Assert.That(saving.Result, Is.False);
            SentSave(Mutations().Single(), 3, 2, SeraUp);
            Assert.That(Outbox().Load(), Is.Null, "a proven Core refusal clears the request");
            Assert.That(view.Notice, Does.Contain("반영되지 않았어요"));
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
            Assert.That(view.SaveSelection, Is.Empty);
            yield return Previewed(view);
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.Empty);
            Assert.That(Mutations().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ADelayedPreviewOfAnOlderSelectionOrContextNeverEnablesSaving()
        {
            SaveRun(defeat: false);
            var gates = new System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource<bool>>();
            post = SaveServer(ids => PreviewReply(ids, hold: gates.TryDequeue(out var gate) ? gate.Task : null), request => Saved());
            var view = View();
            yield return Previewed(view);
            var old = new TaskCompletionSource<bool>();
            var next = new TaskCompletionSource<bool>();
            gates.Enqueue(old);
            gates.Enqueue(next);
            try
            {
                Assert.That(view.ToggleChange(SeraUp), Is.True);
                yield return Until(() => Previews().Length == 2, "the first preview is posted and held");
                Assert.That(view.SavePreviewPending, Is.True);
                // Changing the selection while its preview is held is allowed (local); the held answer can no longer count.
                Assert.That(view.ToggleChange(RinAdd), Is.True);
                Assert.That(view.SaveSelection, Is.EqualTo(new[] { SeraUp, RinAdd }));
                old.TrySetResult(true);
                yield return Until(() => Previews().Length == 3, "the old answer arrives and the new selection is asked");
                // The old answer (error-free, for SeraUp alone) was dropped: nothing is current and saving stays closed.
                Assert.That(Asked(Previews()[1]), Is.EqualTo(new[] { SeraUp }));
                Assert.That(view.SavePreview, Is.Null);
                Assert.That(view.CanCommitSave, Is.False);
                Assert.That(Refused(view.CommitSaveAsync()), Is.True);
                next.TrySetResult(true);
                yield return Previewed(view);
                Assert.That(view.SavePreview!.SelectedChangeIds, Is.EqualTo(new[] { SeraUp, RinAdd }));
                Assert.That(view.CanCommitSave, Is.True);
            }
            finally { old.TrySetResult(true); next.TrySetResult(true); }

            // Another context: a held preview whose session is replaced (leave, another device moves the run, return) never counts, and
            // the unsent selection is gone.
            var stale = new TaskCompletionSource<bool>();
            gates.Enqueue(stale);
            try
            {
                Assert.That(view.ToggleChange(SeraUp), Is.True);
                yield return Until(() => Previews().Length == 4, "a preview is posted and held");
                world.Version = 4;
                view.enabled = false;
                Assert.That(view.SaveSelection, Is.Empty);
                view.enabled = true;
                stale.TrySetResult(true);
                yield return Previewed(view);
            }
            finally { stale.TrySetResult(true); }
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(view.SaveSelection, Is.Empty);
            var current = view.SavePreview!;
            Assert.That(current.SelectedChangeIds, Is.Empty);
            Assert.That(current.Context.ServerVersion, Is.EqualTo(4));

            // An answer at another server version is not this context's: it never becomes the current preview.
            post = SaveServer(ids => PreviewReply(ids, version: world.Version + 1), request => Saved());
            Assert.That(view.ToggleChange(SeraUp), Is.True);
            yield return Until(() => !view.Busy && !view.SavePreviewPending, "the mismatched answer resolves");
            Assert.That(view.SavePreview, Is.Null);
            Assert.That(view.CanCommitSave, Is.False);
            post = SaveServer(ids => PreviewReply(ids), request => Saved());
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            yield return Previewed(view);
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.SavePreview!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(Mutations(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator AnUncertainSaveLocksEverythingShowsNoGainAndRetriesTheExactBytes()
        {
            SaveRun(defeat: false);
            byte[] receipt = Array.Empty<byte>();
            // The server saves and its reply never arrives.
            post = SaveServer(ids => PreviewReply(ids), request => { receipt = Saved().Body; return new Reply(200, receipt) { Drop = true }; });
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(SeraAdd), Is.True);
            yield return Previewed(view);
            var saving = view.CommitSaveAsync();
            yield return Until(() => saving.IsCompleted, "the lost reply resolves");
            Assert.That(saving.Result, Is.False);
            SentSave(Mutations().Single(), 3, 2, SeraAdd);
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(Mutations()[0].Text));
            Assert.That(view.SessionState!.Pending, Is.Not.Null);
            // No gain is shown while unresolved: the last good save screen, stale, with the stored request marked unconfirmed.
            Assert.That(view.SnapshotStale, Is.True);
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
            var text = Drawn(view);
            Assert.That(text, Does.Contain("같은 요청 다시 보내기"));
            Assert.That(text, Does.Contain("저장된 것으로 보지 않아요"));
            Assert.That(text, Does.Contain("확인 중인 저장 요청에 들어 있어요"));
            // Every new action is refused before anything is sent, and no preview is asked either.
            int previews = Previews().Length;
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            Assert.That(view.ToggleChange(SeraUp), Is.False);
            Assert.That(Refused(view.PreviewSaveAsync()), Is.True);
            Assert.That(Refused(view.AssignAsync("c1", "sera")), Is.True);
            Assert.That(view.OpenAssign(), Is.False);
            yield return null;
            yield return null;
            Assert.That(Previews().Length, Is.EqualTo(previews));
            Assert.That(Mutations().Length, Is.EqualTo(1));

            // The explicit retry sends the stored bytes again and then shows the server's confirmed memory.
            post = SaveServer(ids => PreviewReply(ids), request => new Reply(200, receipt));
            var retry = view.RetryAsync();
            yield return Until(() => retry.IsCompleted, "the retry resolves");
            Assert.That(retry.Result, Is.True);
            Assert.That(Mutations().Length, Is.EqualTo(2));
            Assert.That(Mutations()[1].Body, Is.EqualTo(Mutations()[0].Body));
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.ShowingSave, Is.False);
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("DONE"));
            Assert.That(view.Snapshot.Account.MemoryVersion, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator AnAssignmentDropsTheSelectionAndPreviewAndSendsOnlyTheExistingCommand()
        {
            SaveRun(defeat: false);
            post = SaveServer(ids => PreviewReply(ids), request => Error(422, "REASSIGNMENT_REQUIRES_SEPARATE_FLOW"));
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.CanCommitSave, Is.True);
            // The sheet is local: while it is open nothing is chosen or saved.
            Assert.That(view.OpenAssign(), Is.True);
            Assert.That(view.AssignOpen, Is.True);
            Assert.That(Drawn(view), Does.Contain("공용 카드는 누구의 기억에 넣을까요?"));
            Assert.That(view.ToggleChange(SeraUp), Is.False);
            Assert.That(view.CanCommitSave, Is.False);
            Assert.That(Refused(view.CommitSaveAsync()), Is.True);
            // Only a gained common card and a party member are offered; a refused local gate sends and drops nothing.
            Assert.That(Refused(view.AssignAsync("k1", "rin")), Is.True, "not a common card");
            Assert.That(Refused(view.AssignAsync("c1", "mira")), Is.True, "not in the party");
            Assert.That(view.SaveSelection, Is.EqualTo(new[] { RinAdd }));
            Assert.That(Mutations(), Is.Empty);

            // Core's refusal: the selection and preview were dropped before sending; the change list stays the server's.
            var refused = view.AssignAsync("c1", "sera");
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.SavePreview, Is.Null);
            yield return Until(() => refused.IsCompleted, "the refused assignment resolves");
            Assert.That(refused.Result, Is.False);
            Sent(Mutations().Single(), "assign", 3, new JObject { ["key"] = "c1", ["charId"] = "sera" });
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.Notice, Does.Contain("출발 전 기억에 있던 카드의 배정은 여기서 바꿀 수 없어요"));
            yield return Previewed(view);

            // A committed assignment: the server's change list moves (rin's add becomes sera's) and nothing is chosen by itself.
            post = SaveServer(ids => PreviewReply(ids), request => Commit(state =>
            {
                state["deck"]![2]!["assigned"] = "sera";
                var memory = (JObject)state["memory"]!;
                ((JArray)memory["rin"]!["changes"]!).Clear();
                ((JArray)memory["sera"]!["changes"]!).Add(ChangeRow("delta:sera:c1:add", "sera", "c1", "add"));
            }));
            var assigning = view.AssignAsync("c1", "sera");
            yield return Until(() => assigning.IsCompleted, "the assignment resolves");
            Assert.That(assigning.Result, Is.True);
            Sent(Mutations()[1], "assign", 3, new JObject { ["key"] = "c1", ["charId"] = "sera" });
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            yield return Previewed(view);
            Assert.That(view.SaveSelection, Is.Empty, "nothing is selected by itself after an assignment");
            var after = view.SavePreview!;
            Assert.That(after.SelectedChangeIds, Is.Empty);
            Assert.That(after.Context.ServerVersion, Is.EqualTo(4));
            view.CloseAssign();
            Assert.That(view.AssignOpen, Is.False);
            Assert.That(view.ToggleChange(RinAdd), Is.False, "an id the server no longer publishes");
            Assert.That(view.ToggleChange("delta:sera:c1:add"), Is.True);
            yield return Previewed(view);
            Assert.That(view.CanCommitSave, Is.True);
            Assert.That(Mutations().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator TheSaveAndDoneScreensComeBackOnlyFromTheServerAfterLeavingAndRefreshing()
        {
            SaveRun(defeat: false);
            post = SaveServer(ids => PreviewReply(ids), request => Saved());
            var view = View();
            yield return Previewed(view);
            Assert.That(view.ToggleChange(SeraUp), Is.True);
            yield return Previewed(view);
            // Leaving drops the unsent selection with the session; returning re-reads the server and starts from no selection.
            view.enabled = false;
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.Connected, Is.False);
            view.enabled = true;
            yield return Previewed(view);
            Assert.That(view.ShowingSave, Is.True);
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.Empty);
            // A refresh never keeps an unsent selection either.
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            yield return Previewed(view);
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            Assert.That(view.SaveSelection, Is.Empty);
            Assert.That(view.Notice, Does.Contain("다시 골라 주세요"));
            Assert.That(Mutations(), Is.Empty);

            // Keeping the saved deck is a save of no change; DONE shows the confirmed account memory and version, also after a reconnect.
            yield return Previewed(view);
            var saving = view.CommitSaveAsync();
            yield return Until(() => saving.IsCompleted, "the save resolves");
            Assert.That(saving.Result, Is.True);
            SentSave(Mutations().Single(), 3, 2);
            view.enabled = false;
            view.enabled = true;
            yield return Settled(view);
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("DONE"));
            var text = Drawn(view);
            Assert.That(text, Does.Contain("저장된 기억 버전 3"));
            Assert.That(text, Does.Contain("저장된 기억 덱 1장"), "sera's saved deck as the account read shows it");
            Assert.That(text, Does.Contain("기본 덱 (저장된 변화 없음)"), "rin has no saved deck in the account read");

            // The next run is the existing new-run command with useMemory.
            post = request =>
            {
                world.RunId = RunB; world.Version = 0; world.Finished = 1; world.Phase = "MAP"; world.Extra = null;
                return Reply.Json(200, RunJson(world, 0));
            };
            var creating = view.CreateRunAsync();
            yield return Until(() => creating.IsCompleted, "the new run resolves");
            Assert.That(creating.Result, Is.True);
            var create = Mutations().Last();
            Assert.That(create.Path, Is.EqualTo(PracticeCommandOutbox.CreateRunRoute));
            Assert.That(JObject.Parse(create.Text)["useMemory"]!.Value<bool>(), Is.True);
            Assert.That(view.Snapshot!.Run!.RunId, Is.EqualTo(RunB));
        }

        [UnityTest]
        public IEnumerator EveryChangeRowStaysReachableInsideOneScroll()
        {
            var many = new JArray(Enumerable.Range(0, 14).Select(i => (object)ChangeRow("delta:sera:n" + i + ":add", "sera", "n" + i, "add")).ToArray());
            SaveRun(false, many);
            post = SaveServer(ids => PreviewReply(ids), request => Saved());
            var view = View();
            yield return Previewed(view);
            var scroll = view.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().Single(s => s.name == "SaveList");
            var content = scroll.content;
            Assert.That(scroll.vertical, Is.True);
            Assert.That(content.rect.height, Is.GreaterThan(((RectTransform)scroll.transform).rect.height), "the list is taller than its viewport");
            var rows = content.GetComponentsInChildren<UnityEngine.UI.Button>().Where(b => b.name.StartsWith("Change:", StringComparison.Ordinal)).ToList();
            Assert.That(rows.Count, Is.EqualTo(15), "every published change has its row");
            var corners = new Vector3[4];
            foreach (var row in rows)
            {
                ((RectTransform)row.transform).GetWorldCorners(corners);
                Assert.That(content.InverseTransformPoint(corners[0]).y, Is.GreaterThanOrEqualTo(content.rect.yMin - 0.5f), row.name + " ends inside the content");
                Assert.That(content.InverseTransformPoint(corners[1]).y, Is.LessThanOrEqualTo(content.rect.yMax + 0.5f), row.name + " starts inside the content");
            }
            Assert.That(view.ToggleChange("delta:sera:n13:add"), Is.True, "the last row is selectable like the first");
        }

        [UnityTest]
        public IEnumerator TheSaveListKeepsItsScrollThroughSelectionAndPreviewRedrawsUntilTheContextChanges()
        {
            var many = new JArray(Enumerable.Range(0, 14).Select(i => (object)ChangeRow("delta:sera:n" + i + ":add", "sera", "n" + i, "add")).ToArray());
            SaveRun(false, many);
            post = SaveServer(ids => PreviewReply(ids), request => Saved());
            var view = View();
            yield return Previewed(view);
            // Scroll down to rin's change (below sera's fourteen) and choose it: every redraw until its preview arrives keeps the offset.
            SaveListContent(view).anchoredPosition = new Vector2(0, 400);
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            yield return Previewed(view);
            Assert.That(view.SavePreview!.SelectedChangeIds, Is.EqualTo(new[] { RinAdd }));
            Assert.That(SaveListContent(view).anchoredPosition.y, Is.EqualTo(400).Within(0.5f), "the selection and its preview redraw at the same place");

            // An offset past the end is clamped to the new content's bottom, still away from the top.
            var content = SaveListContent(view);
            content.anchoredPosition = new Vector2(0, 100000);
            Assert.That(view.ToggleChange(RinAdd), Is.True);
            yield return Previewed(view);
            content = SaveListContent(view);
            float bottom = content.rect.height - ((RectTransform)content.parent).rect.height;
            Assert.That(bottom, Is.GreaterThan(0));
            Assert.That(content.anchoredPosition.y, Is.GreaterThan(0).And.LessThanOrEqualTo(bottom + 0.5f));
            var rin = content.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "Change:" + RinAdd);
            var corners = new Vector3[4];
            ((RectTransform)rin.transform).GetWorldCorners(corners);
            var viewport = (RectTransform)content.parent;
            Assert.That(viewport.InverseTransformPoint(corners[1]).y, Is.LessThanOrEqualTo(viewport.rect.yMax + 0.5f), "rin's row is in the visible region");
            Assert.That(viewport.InverseTransformPoint(corners[0]).y, Is.GreaterThanOrEqualTo(viewport.rect.yMin - 0.5f));

            // Leaving and returning starts the list at the top again.
            view.enabled = false;
            view.enabled = true;
            yield return Previewed(view);
            Assert.That(SaveListContent(view).anchoredPosition.y, Is.EqualTo(0).Within(0.5f));

            // So does another context: the server moves the run on and a refresh draws it from the top.
            SaveListContent(view).anchoredPosition = new Vector2(0, 400);
            world.Version = 4;
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            yield return Previewed(view);
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(SaveListContent(view).anchoredPosition.y, Is.EqualTo(0).Within(0.5f));
            Assert.That(Mutations(), Is.Empty);
        }

        // ---- save fixtures (protocol data on the shared World) ----

        // A terminal save run: PublicView's VICTORY/DEFEAT state with pending {type: save, defeat}, a two-member party, a deck with a gained
        // common card assigned to rin, and the public state.memory {cid: {capacity, usedBefore, before, after, changes}}.
        void SaveRun(bool defeat, JArray? seraChanges = null)
        {
            world.Phase = defeat ? "DEFEAT" : "VICTORY";
            world.InBattle = false;
            world.Extra = new JObject
            {
                ["hp"] = defeat ? 0 : 30, ["coins"] = 12, ["pending"] = new JObject { ["type"] = "save", ["defeat"] = defeat },
                ["result"] = new JObject { ["outcome"] = defeat ? "defeat" : "return", ["floor"] = 2 },
                ["party"] = new JArray(new JObject { ["charId"] = "sera", ["res"] = 0 }, new JObject { ["charId"] = "rin", ["res"] = 0 }),
                ["deck"] = new JArray(
                    new JObject { ["key"] = "k1", ["id"] = "strike", ["owner"] = "sera", ["plus"] = true },
                    new JObject { ["key"] = "g1", ["id"] = "pull", ["owner"] = "sera", ["gained"] = true },
                    new JObject { ["key"] = "c1", ["id"] = "echo", ["owner"] = "common", ["assigned"] = "rin", ["gained"] = true }),
                ["relics"] = new JArray(), ["partsFound"] = new JArray(),
                ["memory"] = new JObject
                {
                    ["sera"] = MemoryColumn(6, 2, MemorySpec("k1", "strike"), seraChanges ?? new JArray(ChangeRow(SeraUp, "sera", "k1", "upgrade"), ChangeRow(SeraAdd, "sera", "g1", "add"))),
                    ["rin"] = MemoryColumn(6, 0, MemorySpec("r1", "guard"), new JArray(ChangeRow(RinAdd, "rin", "c1", "add")))
                }
            };
        }
        static JObject MemoryColumn(int capacity, int usedBefore, JObject before, JArray changes) => new JObject
        {
            ["capacity"] = capacity, ["usedBefore"] = usedBefore, ["before"] = new JArray(before), ["after"] = new JArray(before.DeepClone()), ["changes"] = changes
        };
        static JObject MemorySpec(string key, string id) => new JObject { ["key"] = key, ["id"] = id, ["plus"] = false };
        static JObject ChangeRow(string id, string cid, string key, string kind) =>
            new JObject { ["id"] = id, ["cid"] = cid, ["key"] = key, ["kind"] = kind, ["label"] = key + " " + kind };

        // The memory-preview reply {runId, version, preview {memory, used, error, capacities}} (Program.Preview's shape) at the world's
        // version unless told otherwise. The asked ids only add visible rows to the decks; Core builds the real ones.
        Reply PreviewReply(string[] ids, string? error = null, int seraUsed = 3, Task? hold = null, int? version = null)
        {
            var sera = new JArray(MemorySpec("k1", "strike"));
            var rin = new JArray(MemorySpec("r1", "guard"));
            if (ids.Contains(SeraAdd)) sera.Add(MemorySpec("g1", "pull"));
            if (ids.Contains(RinAdd)) rin.Add(new JObject { ["key"] = "c1", ["id"] = "echo", ["plus"] = false, ["assigned"] = "rin" });
            return new Reply(200, Utf8(new JObject
            {
                ["runId"] = RunA, ["version"] = version ?? world.Version,
                ["preview"] = new JObject
                {
                    ["memory"] = new JObject { ["sera"] = sera, ["rin"] = rin },
                    ["used"] = new JObject { ["sera"] = seraUsed, ["rin"] = ids.Contains(RinAdd) ? 1 : 0 },
                    ["error"] = error == null ? JValue.CreateNull() : (JToken)error,
                    ["capacities"] = new JObject { ["sera"] = 6, ["rin"] = 6 }
                }
            })) { Hold = hold };
        }

        // The fixture server saves: the run is DONE at the next version, the account at the next memory version, and the receipt carries
        // the commit result a memory-commit reply needs.
        Reply Saved(Task? hold = null)
        {
            world.Version++;
            world.Phase = "DONE";
            world.MemoryVersion++;
            world.Extra!["pending"] = new JObject { ["type"] = "done" };
            world.Extra.Remove("memory");
            var receipt = RunJson(world, world.Version);
            receipt["result"] = new JObject { ["memory"] = new JObject(), ["used"] = new JObject(), ["version"] = world.MemoryVersion };
            return new Reply(200, Utf8(receipt)) { Hold = hold };
        }

        // POSTs of the save: memory-preview answers by the asked ids (pure: the world does not move); any other POST is a mutation.
        Func<Seen, Reply> SaveServer(Func<string[], Reply> preview, Func<Seen, Reply> mutation) =>
            request => request.Path == PreviewRoute ? preview(Asked(request)) : mutation(request);
        // A preview body is exactly {selectedChangeIds} (no requestId, no expected versions).
        static string[] Asked(Seen request)
        {
            var body = JObject.Parse(request.Text);
            Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "selectedChangeIds" }), "a preview sends only its selection");
            return body["selectedChangeIds"]!.Values<string>().Select(id => id!).ToArray();
        }
        Seen[] Previews() => host.Posts.Where(p => p.Path == PreviewRoute).ToArray();
        Seen[] Mutations() => host.Posts.Where(p => p.Path != PreviewRoute).ToArray();

        static void SentSave(Seen seen, int expectedVersion, int expectedMemoryVersion, params string[] ids)
        {
            Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.MemoryCommitRoute(RunA)));
            var body = JObject.Parse(seen.Text);
            Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "requestId", "expectedVersion", "expectedMemoryVersion", "selectedChangeIds" }));
            Assert.That(body["expectedVersion"]!.Value<int>(), Is.EqualTo(expectedVersion));
            Assert.That(body["expectedMemoryVersion"]!.Value<int>(), Is.EqualTo(expectedMemoryVersion));
            Assert.That(body["selectedChangeIds"]!.Values<string>(), Is.EqualTo(ids));
        }

        // The preview of the current selection has settled (an answer with or without a view; nothing due or in flight).
        static IEnumerator Previewed(MemoryForgeServerPracticeClient view) => Until(() => !view.Busy && !view.SavePreviewPending
            && (view.SavePreview != null || view.SavePreviewFailure != null), "the save preview settles");
        // Every live text and button of the view's current canvas (a replaced canvas is inactive until destroyed and is not read).
        static string Drawn(MemoryForgeServerPracticeClient view) =>
            string.Join("\n", view.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text));
        static RectTransform SaveListContent(MemoryForgeServerPracticeClient view) =>
            view.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().Single(s => s.name == "SaveList").content;
        static UnityEngine.UI.Button? DrawnButton(MemoryForgeServerPracticeClient view, string name) =>
            view.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(b => b.name == name);
    }
}
#endif
