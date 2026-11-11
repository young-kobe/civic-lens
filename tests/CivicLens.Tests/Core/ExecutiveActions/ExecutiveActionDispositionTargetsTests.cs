using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Tests.Core.ExecutiveActions;

public sealed class ExecutiveActionDispositionTargetsTests
{
    [Fact]
    public void RealNoteShapesGiveExactTargetsWithTheirSpansInsideTheNote()
    {
        const string notes = "Amends: EO 13224, September 23, 2001\r\nRevokes: EO 14056, December 1, 2021; Proc. 9822\r\nSee: EO 14186, January 27, 2025";

        var targets = ExecutiveActionDispositionTargets.Parse(notes);

        Assert.Equal([
            (ExecutiveActionRelation.Amends, ExecutiveActionKind.ExecutiveOrder, "13224", "EO 13224, September 23, 2001"),
            (ExecutiveActionRelation.Revokes, ExecutiveActionKind.ExecutiveOrder, "14056", "EO 14056, December 1, 2021"),
            (ExecutiveActionRelation.Revokes, ExecutiveActionKind.Proclamation, "9822", "Proc. 9822")],
            targets.Select(target => (target.Relation, target.Kind, target.Number, notes.Substring(target.Start, target.Length))));
    }

    [Theory]
    [InlineData("See: EO 14186, January 27, 2025")]
    [InlineData("Supersedes: EO 13224")]
    [InlineData("amends: EO 13224")]
    [InlineData("Amends: Space Policy Directive 3, June 18, 2018")]
    [InlineData("Amends: SPD 3")]
    [InlineData("Revokes: Memorandum of January 20, 2021")]
    [InlineData("Amends: EO 13224 and EO 13225")]
    [InlineData("Amends: EO 13224-13230")]
    [InlineData("Amends: EO 13224 (in part)")]
    [InlineData("Amends: EO 13224, Sept. 23, 2001")]
    [InlineData("Amends: EO 013224")]
    [InlineData("Amends: EO 1234567")]
    [InlineData("Amends: EO")]
    [InlineData("Revokes: Proclamation 9822")]
    [InlineData("Note: Amends: EO 13224")]
    [InlineData("")]
    public void UnsupportedOrAmbiguousReferencesGiveNoTarget(string notes) =>
        Assert.Empty(ExecutiveActionDispositionTargets.Parse(notes));

    [Fact]
    public void ASupportedReferenceBesideAnUnsupportedOneStaysATarget()
    {
        const string notes = "Amends: SPD 3; EO 13224 and EO 13225;  EO 12866, September 30, 1993 ";

        var target = Assert.Single(ExecutiveActionDispositionTargets.Parse(notes));

        Assert.Equal("12866", target.Number);
        Assert.Equal("EO 12866, September 30, 1993", notes.Substring(target.Start, target.Length));
    }

    [Fact]
    public void ARepeatedReferenceOfTheSameRelationIsOneTargetButARevocationOfTheSameOrderIsAnother()
    {
        var targets = ExecutiveActionDispositionTargets.Parse("Amends: EO 13224; EO 13224\nRevokes: EO 13224");

        Assert.Equal([ExecutiveActionRelation.Amends, ExecutiveActionRelation.Revokes], targets.Select(target => target.Relation));
    }

    [Fact]
    public void MoreTargetsThanTheMaximumGiveNoTargetsInsteadOfAGuess()
    {
        string Notes(int count) => "Revokes: " + string.Join("; ", Enumerable.Range(10_000, count).Select(number => $"EO {number}"));

        Assert.Equal(ExecutiveActionDispositionTargets.MaximumTargets, ExecutiveActionDispositionTargets.Parse(Notes(ExecutiveActionDispositionTargets.MaximumTargets)).Count);
        Assert.Empty(ExecutiveActionDispositionTargets.Parse(Notes(ExecutiveActionDispositionTargets.MaximumTargets + 1)));
    }
}
