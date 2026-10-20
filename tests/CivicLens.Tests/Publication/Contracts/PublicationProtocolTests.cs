using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublicationProtocolTests
{
    [Theory]
    [InlineData(32, true)]
    [InlineData(64, true)]
    [InlineData(31, false)]
    [InlineData(33, false)]
    [InlineData(65, false)]
    public void AnyRecordIdHasExactlyThe32Or64HexLengthsSoARouteCanRefuseAnIdBeforeTheLookup(int length, bool expected)
    {
        Assert.Equal(expected, PublicationProtocol.IsAnyRecordId(new string('a', length)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyRecordIdIsNotValid(string? value)
    {
        Assert.False(PublicationProtocol.IsAnyRecordId(value));
    }

    [Fact]
    public void UpperCaseAndNonHexCharactersAreNotValidInARecordId()
    {
        Assert.False(PublicationProtocol.IsAnyRecordId(new string('A', 64)));
        Assert.False(PublicationProtocol.IsAnyRecordId(new string('g', 32)));
    }
}
