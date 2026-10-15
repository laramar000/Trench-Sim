using System.Security.Cryptography;
using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Server.Tests;

// Signed match records and the verifier: every check, and records forged by someone who holds the key (so replay, not
// the signature, has to catch them).
public class RecordTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly TrustedKey[] Trusted = [new(RecordKeys.KeyIdOf(Key), Key)];
    private static readonly Lazy<MatchRecordBody> Ranged = new(() => TestRecords.Completed("ranged", 1));
    private static readonly Lazy<MatchRecordBody> Melee = new(() => TestRecords.Completed("melee", 3));
    private static readonly Lazy<MatchRecordBody> Stopped = new(() => TestRecords.Aborted("ranged", 2, 12));

    private static RecordVerdict Check(MatchRecordBody body) => RecordVerifier.Verify(RecordFormat.Sign(body, Key), Trusted);

    private static void AssertInvalid(VerdictReason reason, RecordVerdict verdict)
    {
        Assert.Equal(VerdictKind.Invalid, verdict.Kind);
        Assert.Equal(reason, verdict.Reason);
        Assert.False(verdict.IsCompletedMatch);
    }

    [Theory]
    [InlineData("ranged", 1)]
    [InlineData("ranged", 7)]
    [InlineData("melee", 3)]
    [InlineData("melee", 11)]
    public void ARecordOfAFinishedMatchVerifiesAsCompletedWithTheServersHash(string scenario, int seed)
    {
        var body = TestRecords.Completed(scenario, seed);
        var verdict = Check(body);
        Assert.Equal(VerdictKind.Completed, verdict.Kind);
        Assert.True(verdict.IsCompletedMatch);
        Assert.Equal(body.Events[^1].Hash, verdict.FinalHash);
        Assert.IsType<MatchEnded>(body.Events[^1].Event);
        Assert.Equal(StateHash.Compute(Reducer.Replay(body.Events.Select(e => e.Event))), verdict.FinalHash);
    }

    [Fact]
    public void AnAbortedRecordVerifiesAsAbortedAndNeverAsCompleted()
    {
        var verdict = Check(Stopped.Value);
        Assert.Equal(VerdictKind.Aborted, verdict.Kind);
        Assert.False(verdict.IsCompletedMatch);
        Assert.Equal(nameof(AbortReason.Disconnected), verdict.Body!.Result.Reason);
        Assert.Null(verdict.Body.Result.Winner);
    }

    [Fact]
    public void ManyRecordsRoundTripAndVerify()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            foreach (var scenario in new[] { "ranged", "melee" })
            {
                var body = TestRecords.Completed(scenario, seed);
                var file = RecordFormat.Sign(body, Key);
                var verdict = RecordVerifier.Verify(file, Trusted);
                Assert.Equal(VerdictKind.Completed, verdict.Kind);
                Assert.Equal(RecordFormat.EncodeBody(body), RecordFormat.EncodeBody(verdict.Body!));
            }
        }
    }

    // ---- The signature ----

    [Fact]
    public void ChangingAnyByteOfTheBodyBreaksTheSignature()
    {
        var file = RecordFormat.Sign(Ranged.Value, Key);
        var text = Encoding.UTF8.GetString(file);
        var start = text.IndexOf("\"body\":", StringComparison.Ordinal) + 7;
        var end = text.IndexOf(",\n\"signature\"", StringComparison.Ordinal);
        var random = new Random(5);
        for (var i = 0; i < 300; i++)
        {
            var copy = (byte[])file.Clone();
            var at = random.Next(start, end);
            copy[at] = copy[at] == (byte)'1' ? (byte)'2' : (byte)'1';
            var verdict = RecordVerifier.Verify(copy, Trusted);
            Assert.NotEqual(VerdictKind.Completed, verdict.Kind);
            Assert.True(verdict.Reason is VerdictReason.BadSignature or VerdictReason.Malformed, $"{verdict.Reason} at {at}");
        }
    }

    [Fact]
    public void TheSignatureCoversTheVersionAndKeyIdToo()
    {
        var file = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, Key));
        // Another key id (even a trusted one) cannot carry this signature.
        var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherId = RecordKeys.KeyIdOf(other);
        var swapped = file.Replace(Trusted[0].KeyId, otherId);
        Assert.Equal(VerdictReason.BadSignature, RecordVerifier.Verify(Encoding.UTF8.GetBytes(swapped), [.. Trusted, new TrustedKey(otherId, other)]).Reason);
    }

    [Fact]
    public void ReformattingOutsideTheBodyKeepsTheSignature()
    {
        var file = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, Key));
        var reformatted = file.Replace("{\"format\":", "{\n  \"format\" :  ").Replace(",\n\"signature\":", " ,\n\n  \"signature\" : ");
        Assert.Equal(VerdictKind.Completed, RecordVerifier.Verify(Encoding.UTF8.GetBytes(reformatted), Trusted).Kind);
    }

    [Fact]
    public void AnUnknownKeyIsUnverifiableAndAWrongSignatureIsInvalid()
    {
        var file = RecordFormat.Sign(Ranged.Value, Key);
        var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verdict = RecordVerifier.Verify(file, [new TrustedKey(RecordKeys.KeyIdOf(stranger), stranger)]);
        Assert.Equal(VerdictKind.Unverifiable, verdict.Kind);
        Assert.Equal(VerdictReason.UntrustedKey, verdict.Reason);
        Assert.Equal(VerdictReason.UntrustedKey, RecordVerifier.Verify(file, []).Reason);

        // A forger signs with their own key but claims the trusted key's id.
        var forged = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, stranger)).Replace(RecordKeys.KeyIdOf(stranger), Trusted[0].KeyId);
        AssertInvalid(VerdictReason.BadSignature, RecordVerifier.Verify(Encoding.UTF8.GetBytes(forged), Trusted));
    }

    [Fact]
    public void ARecordCannotBringItsOwnKey()
    {
        var file = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, Key));
        var withKey = file.Replace(",\n\"signature\":", ",\"publicKey\":\"" + Key.ExportSubjectPublicKeyInfoPem().ReplaceLineEndings("\\n") + "\",\n\"signature\":");
        AssertInvalid(VerdictReason.Malformed, RecordVerifier.Verify(Encoding.UTF8.GetBytes(withKey), []));
    }

    [Fact]
    public void OnlyEs256OnP256IsAccepted()
    {
        var file = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, Key));
        var verdict = RecordVerifier.Verify(Encoding.UTF8.GetBytes(file.Replace("\"ES256\"", "\"ES384\"")), Trusted);
        Assert.Equal(VerdictKind.Unverifiable, verdict.Kind);
        Assert.Equal(VerdictReason.UnsupportedAlgorithm, verdict.Reason);

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(RecordKeys.FromPem(p384.ExportSubjectPublicKeyInfoPem()));
        Assert.Null(RecordKeys.FromPem("not a key"));
        Assert.NotNull(RecordKeys.FromPem(Key.ExportSubjectPublicKeyInfoPem()));
    }

    // ---- The outer layout ----

    [Theory]
    [InlineData("{\"format\":\"trench-sim.match-record\",\"recordVersion\":1,\"body\":{},\"body\":{},\"signature\":{}}")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{\"format\":\"trench-sim.match-record\",\"recordVersion\":1}")]
    [InlineData("{\"format\":\"trench-sim.match-record\",\"recordVersion\":1,\"body\":{},\"signature\":{\"alg\":\"ES256\",\"keyId\":\"x\",\"value\":\"y\"}} trailing")]
    public void ABrokenLayoutIsMalformed(string text)
    {
        AssertInvalid(VerdictReason.Malformed, RecordVerifier.Verify(Encoding.UTF8.GetBytes(text), Trusted));
    }

    [Fact]
    public void ADuplicateBodyIsRefusedSoThereIsNoDoubtWhichWasSigned()
    {
        var file = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, Key));
        var body = file[(file.IndexOf("\"body\":", StringComparison.Ordinal) + 7)..file.IndexOf(",\n\"signature\"", StringComparison.Ordinal)];
        var twice = file.Replace(",\n\"signature\":", ",\"body\":" + body + ",\n\"signature\":");
        AssertInvalid(VerdictReason.Malformed, RecordVerifier.Verify(Encoding.UTF8.GetBytes(twice), Trusted));
    }

    [Fact]
    public void AnUnknownRecordVersionOrFormatIsUnverifiable()
    {
        var file = Encoding.UTF8.GetString(RecordFormat.Sign(Ranged.Value, Key));
        foreach (var changed in new[] { file.Replace("\"recordVersion\":1", "\"recordVersion\":2"), file.Replace("trench-sim.match-record", "other-format") })
        {
            var verdict = RecordVerifier.Verify(Encoding.UTF8.GetBytes(changed), Trusted);
            Assert.Equal(VerdictKind.Unverifiable, verdict.Kind);
            Assert.Equal(VerdictReason.UnsupportedRecordVersion, verdict.Reason);
        }
    }

    [Fact]
    public void AnOversizedOrNonUtf8RecordIsRefusedBeforeParsing()
    {
        AssertInvalid(VerdictReason.Malformed, RecordVerifier.Verify(new byte[RecordFormat.MaxBytes + 1], Trusted));
        AssertInvalid(VerdictReason.Malformed, RecordVerifier.Verify([0x7b, 0xff, 0x7d], Trusted));
        var deep = new string('[', 100) + new string(']', 100);
        AssertInvalid(VerdictReason.Malformed, RecordVerifier.Verify(Encoding.UTF8.GetBytes($"{{\"format\":{deep}}}"), Trusted));
    }

    // ---- Historical semantics: never verify under other rules ----

    [Theory]
    [InlineData("1.0.3", 4, 1)]
    [InlineData("1.0.2", 3, 1)]
    [InlineData("1.0.2", 5, 1)]
    [InlineData("1.0.2", 4, 2)]
    [InlineData("1.0.2", 4, 0)]
    public void ARecordFromOtherRulesIsUnverifiableNotSilentlyVerified(string rules, int hashVersion, int replay)
    {
        var body = TestRecords.With(Ranged.Value, engine: new RecordEngine(rules, hashVersion, replay, 2, "other-build"));
        var verdict = Check(body);
        Assert.Equal(VerdictKind.Unverifiable, verdict.Kind);
        Assert.Equal(VerdictReason.UnsupportedRules, verdict.Reason);
        Assert.Contains("other-build", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void GoldenRecordsStillVerify()
    {
        // Committed records signed with a throwaway key; only its public half is committed. If an engine change alters
        // what a stored log means, these fail: bump RecordFormat.ReplayCompatibility on purpose and regenerate them
        // (TRENCH_SIM_UPDATE_GOLDEN=1), which signs with a fresh key and replaces test-signing.pub.
        var folder = Path.Combine(Repository.Root(), "tests", "Server.Tests", "golden");
        if (Environment.GetEnvironmentVariable("TRENCH_SIM_UPDATE_GOLDEN") == "1")
        {
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(Path.Combine(folder, "test-signing.pub"), signer.ExportSubjectPublicKeyInfoPem());
            File.WriteAllBytes(Path.Combine(folder, "ranged-1.json"), RecordFormat.Sign(TestRecords.Completed("ranged", 1), signer));
            File.WriteAllBytes(Path.Combine(folder, "melee-3.json"), RecordFormat.Sign(TestRecords.Completed("melee", 3), signer));
            File.WriteAllBytes(Path.Combine(folder, "aborted-ranged-2.json"), RecordFormat.Sign(TestRecords.Aborted("ranged", 2, 12), signer));
        }
        var key = RecordKeys.FromPem(File.ReadAllText(Path.Combine(folder, "test-signing.pub")))!;
        var expected = new Dictionary<string, VerdictKind>
        {
            ["ranged-1.json"] = VerdictKind.Completed,
            ["melee-3.json"] = VerdictKind.Completed,
            ["aborted-ranged-2.json"] = VerdictKind.Aborted,
        };
        foreach (var (name, kind) in expected)
        {
            var verdict = RecordVerifier.VerifyFile(Path.Combine(folder, name), [key]);
            Assert.True(verdict.Kind == kind, $"{name}: {verdict.Kind} {verdict.Reason} {verdict.Detail}");
        }
    }

    // ---- Records forged by a key holder: replay has to catch them ----

    [Fact]
    public void RemovingDuplicatingSwappingOrRenumberingEventsIsCaught()
    {
        var events = Ranged.Value.Events;
        var mid = events.Count / 2;

        var removed = events.Where((_, i) => i != mid).ToList();
        Assert.False(Check(TestRecords.With(Ranged.Value, removed, TestRecords.ResultFor(Ranged.Value.Result, removed))).IsCompletedMatch);

        var duplicated = events.Take(mid + 1).Append(events[mid]).Concat(events.Skip(mid + 1)).ToList();
        Assert.Equal(VerdictReason.BadSequence, Check(TestRecords.With(Ranged.Value, duplicated, TestRecords.ResultFor(Ranged.Value.Result, duplicated))).Reason);

        var renumbered = events.Select((e, i) => e with { Seq = i + 1 }).ToList();
        (renumbered[mid], renumbered[mid + 1]) = (renumbered[mid + 1] with { Seq = mid + 1 }, renumbered[mid] with { Seq = mid + 2 });
        var swapped = Check(TestRecords.With(Ranged.Value, renumbered));
        Assert.Equal(VerdictKind.Invalid, swapped.Kind);
        Assert.True(swapped.Reason is VerdictReason.IllegalEvent or VerdictReason.HashMismatch, swapped.Reason.ToString());

        var gap = events.Select(e => e.Seq > mid ? e with { Seq = e.Seq + 1 } : e).ToList();
        AssertInvalid(VerdictReason.BadSequence, Check(TestRecords.With(Ranged.Value, gap)));
    }

    [Fact]
    public void AChangedHashIsCaught()
    {
        var events = Ranged.Value.Events.ToList();
        events[5] = events[5] with { Hash = new string('0', 64) };
        AssertInvalid(VerdictReason.HashMismatch, Check(TestRecords.With(Ranged.Value, events)));
    }

    [Fact]
    public void AnEventThatCannotFollowIsIllegal()
    {
        var events = Ranged.Value.Events.ToList();
        // A second MatchCreated in the middle of the match.
        events[10] = events[10] with { Event = events[0].Event };
        AssertInvalid(VerdictReason.BadSetup, Check(TestRecords.With(Ranged.Value, events)));

        // An end of the match while it is still going.
        var early = events.Take(10).Append(new RecordedEvent(11, new MatchEnded(Seat.One, OutcomeReason.OpponentFled), new string('a', 64))).ToList();
        AssertInvalid(VerdictReason.IllegalEvent, Check(TestRecords.With(Ranged.Value, early, TestRecords.ResultFor(Ranged.Value.Result, early))));
    }

    [Fact]
    public void AChangedDieIsCaughtByReplayWhenIllegalButALegalOneOnlyByTheSignature()
    {
        // Every die lives inside its event. A key holder who changes a die and re-hashes everything consistently is
        // caught by replay only when the change breaks the rules. A legal change verifies: a valid signature proves who
        // wrote the record, not that the dice were fair.
        var events = Ranged.Value.Events.ToList();
        var index = events.FindIndex(e => e.Event is RollOffResolved);
        Assert.True(index > 0);
        var rollOff = (RollOffResolved)events[index].Event;
        var last = rollOff.Rounds[^1];

        // Illegal: the other seat wins the roll-off, so what follows no longer fits.
        var swapped = new RollOffResolved(rollOff.Purpose, [.. rollOff.Rounds.SkipLast(1), new RollOffRound(last.SeatTwo, last.SeatOne)], rollOff.Winner.Opponent());
        var illegal = Rehash(events, index, swapped);
        var verdict = Check(TestRecords.With(Ranged.Value, illegal, TestRecords.ResultFor(Ranged.Value.Result, illegal)));
        Assert.Equal(VerdictKind.Invalid, verdict.Kind);
        Assert.Equal(VerdictReason.IllegalEvent, verdict.Reason);

        // Legal: the winner's die becomes a 6; the same seat still wins. Signed by the key holder, it verifies.
        var raised = rollOff.Winner == Seat.One ? new RollOffRound(6, last.SeatTwo) : new RollOffRound(last.SeatOne, 6);
        var legal = Rehash(events, index, new RollOffResolved(rollOff.Purpose, [.. rollOff.Rounds.SkipLast(1), raised], rollOff.Winner));
        var accepted = Check(TestRecords.With(Ranged.Value, legal, TestRecords.ResultFor(Ranged.Value.Result, legal)));
        Assert.Equal(VerdictKind.Completed, accepted.Kind);
    }

    // Replaces one event and recomputes every hash from there, as a forger holding the key would.
    private static List<RecordedEvent> Rehash(List<RecordedEvent> events, int index, GameEvent replacement)
    {
        var result = new List<RecordedEvent>();
        var state = MatchState.None;
        for (var i = 0; i < events.Count; i++)
        {
            var e = i == index ? replacement : events[i].Event;
            try
            {
                state = Reducer.Apply(state, e);
            }
            catch (InvalidOperationException)
            {
                // Keep the rest unchanged; the verifier must stop here.
                result.AddRange(events.Skip(i).Select(x => x with { Event = i == index ? replacement : x.Event }).Take(1));
                result.AddRange(events.Skip(i + 1));
                return result;
            }
            result.Add(new RecordedEvent(i + 1, e, StateHash.Compute(state)));
        }
        return result;
    }

    [Fact]
    public void AClaimedCompletionWithoutMatchEndedIsNotCompleted()
    {
        var events = Ranged.Value.Events.Take(Ranged.Value.Events.Count - 1).ToList();
        var verdict = Check(TestRecords.With(Ranged.Value, events, TestRecords.ResultFor(Ranged.Value.Result, events)));
        AssertInvalid(VerdictReason.NotCompleted, verdict);
    }

    [Fact]
    public void AnAbortedRecordRelabelledCompletedIsNotCompleted()
    {
        var body = Stopped.Value;
        var relabelled = body.Result with { Kind = RecordKind.Completed, Winner = Seat.One, Reason = nameof(OutcomeReason.VictoryPoints), Seat = null };
        AssertInvalid(VerdictReason.NotCompleted, Check(TestRecords.With(body, result: relabelled)));
    }

    [Fact]
    public void AWrongWinnerOrReasonIsAResultMismatch()
    {
        var result = Ranged.Value.Result;
        var other = result.Winner == Seat.One ? Seat.Two : Seat.One;
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(Ranged.Value, result: result with { Winner = other })));
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(Ranged.Value, result: result with { Reason = nameof(OutcomeReason.OpponentFled) })));
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(Ranged.Value, result: result with { Events = result.Events - 1 })));
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(Ranged.Value, result: result with { Hash = new string('f', 64) })));
    }

    [Fact]
    public void EventsAfterMatchEndedAreRefused()
    {
        var events = Ranged.Value.Events.Append(Ranged.Value.Events[^1] with { Seq = Ranged.Value.Events.Count + 1 }).ToList();
        var verdict = Check(TestRecords.With(Ranged.Value, events, TestRecords.ResultFor(Ranged.Value.Result, events)));
        AssertInvalid(VerdictReason.IllegalEvent, verdict);
    }

    [Fact]
    public void AnAbortedRecordCannotHideAWinnerOrEndWithMatchEnded()
    {
        var body = Stopped.Value;
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(body, result: body.Result with { Winner = Seat.One })));
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(body, result: body.Result with { Reason = "Victory" })));
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(body, result: body.Result with { Reason = "1" })));
        var completed = Ranged.Value;
        var asAborted = completed.Result with { Kind = RecordKind.Aborted, Winner = null, Reason = nameof(AbortReason.Disconnected) };
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(completed, result: asAborted)));
    }

    [Fact]
    public void ASwappedSetupIsCaught()
    {
        var melee = Melee.Value;
        AssertInvalid(VerdictReason.BadSetup, Check(TestRecords.With(Ranged.Value, fingerprint: melee.ContentFingerprint)));
        var events = Ranged.Value.Events.ToList();
        events[0] = melee.Events[0];
        AssertInvalid(VerdictReason.BadSetup, Check(TestRecords.With(Ranged.Value, events)));
        AssertInvalid(VerdictReason.BadSetup, Check(TestRecords.With(Ranged.Value, Ranged.Value.Events.Skip(1).Select((e, i) => e with { Seq = i + 1 }).ToList())));
    }

    [Fact]
    public void AnUnrecordedStubIsNeverAFinishedMatchAndCannotCarryEvents()
    {
        var stub = TestRecords.With(Ranged.Value, [], new RecordResult(RecordKind.Unrecorded, 0, null, null, "RecordLost", null));
        var verdict = Check(stub);
        Assert.Equal(VerdictKind.Unrecorded, verdict.Kind);
        Assert.False(verdict.IsCompletedMatch);
        Assert.Null(verdict.FinalHash);

        var padded = TestRecords.With(Ranged.Value, result: new RecordResult(RecordKind.Unrecorded, 0, null, null, "RecordLost", null));
        AssertInvalid(VerdictReason.ResultMismatch, Check(padded));
        AssertInvalid(VerdictReason.ResultMismatch, Check(TestRecords.With(stub, result: stub.Result with { Winner = Seat.One })));
    }

    [Fact]
    public void TruncationAnywhereIsNeverCompleted()
    {
        var file = RecordFormat.Sign(Ranged.Value, Key);
        for (var length = 0; length < file.Length - 2; length += 1024)
        {
            var verdict = RecordVerifier.Verify(file.AsSpan(0, length), Trusted);
            Assert.NotEqual(VerdictKind.Completed, verdict.Kind);
        }
        Assert.Equal(VerdictKind.Completed, RecordVerifier.Verify(file, Trusted).Kind);
    }

    [Fact]
    public void RandomMutationsNeverThrowAndNeverChangeACompletedResult()
    {
        var file = RecordFormat.Sign(Melee.Value, Key);
        var originalBody = RecordFormat.EncodeBody(Melee.Value);
        var random = new Random(42);
        var completed = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var copy = new List<byte>(file);
            for (var edits = random.Next(1, 4); edits > 0; edits--)
            {
                var at = random.Next(copy.Count);
                switch (random.Next(3))
                {
                    case 0:
                        copy[at] = (byte)random.Next(256);
                        break;
                    case 1:
                        copy.RemoveAt(at);
                        break;
                    default:
                        copy.Insert(at, (byte)" \n{}[],:\"0aZ"[random.Next(12)]);
                        break;
                }
            }
            var verdict = RecordVerifier.Verify(copy.ToArray(), Trusted);
            if (verdict.Kind == VerdictKind.Completed)
            {
                // Only an edit outside the signed body (whitespace) can keep it Completed, and then nothing changed.
                completed++;
                Assert.Equal(originalBody, RecordFormat.EncodeBody(verdict.Body!));
            }
        }
        Assert.True(completed < 1000, $"{completed} mutations still verified");
    }
}

internal static class Repository
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TrenchSim.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("TrenchSim.sln not found above the test output directory.");
    }
}
