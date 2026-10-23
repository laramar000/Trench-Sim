using Rules.Core.State;

namespace Client.Core;

public enum TargetTone
{
    // Visible and accepted by the preview, not in cover.
    Clear,

    // Accepted by the preview, the target in cover.
    Cover,

    // The preview says the server would refuse it.
    Refused,
}

// Battlefield UI, stage K5: the one bold line drawn for Shoot, Charge and Fight, from the attacker's base edge to the
// target's base edge, toned by the preview (clear, in cover, refused). Built only from the preview and the positions in
// the state it was made on; the sight rays stay in the preview's words ("4 of 5 sight rays clear"). Nothing new in the rules.
public sealed record TargetLine(ModelId Attacker, ModelId Target, TablePoint From, TablePoint To, TargetTone Tone)
{
    public static TargetLine? Of(MatchState state, Preview? preview)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (preview is not { Kind: PreviewKind.Shoot or PreviewKind.Charge or PreviewKind.Fight, Target: { } target }
            || state.FindModel(preview.Model)?.Position is not { } a || state.FindModel(target)?.Position is not { } t)
        {
            return null;
        }
        var from = TablePoint.Of(a);
        var to = TablePoint.Of(t);
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        var ra = Previews.RadiusOf(state, preview.Model);
        var rt = Previews.RadiusOf(state, target);
        // From base edge to base edge; bases in contact (or overlapping in the picture) give a line between the centres.
        if (length > ra + rt + 1e-6)
        {
            var (ux, uy) = (dx / length, dy / length);
            from = new TablePoint(from.X + ux * ra, from.Y + uy * ra);
            to = new TablePoint(to.X - ux * rt, to.Y - uy * rt);
        }
        var tone = !preview.Legal ? TargetTone.Refused
            : preview.Facts.Any(f => f.StartsWith(Previews.CoverFact, StringComparison.Ordinal)) ? TargetTone.Cover
            : TargetTone.Clear;
        return new TargetLine(preview.Model, target, from, to, tone);
    }
}
