namespace Client.Core;

// What the screen shows once a match is over (ended or aborted): the result in words and the final state hash and
// event count of this client's hash-checked copy of the match. The hash is what `trench-cli verify` prints for the
// server's signed record, so a player can compare the two. Nothing here is computed by the client beyond what its
// copy already holds; the server decided the result.
public sealed record MatchEnd(bool Aborted, string Result, int Events, string Hash, string? Note)
{
    public IEnumerable<string> Lines()
    {
        yield return $"Result: {Result}";
        yield return $"Final state hash: {Hash}";
        yield return $"Events: {Events}";
        if (Note is not null)
        {
            yield return Note;
        }
    }
}
