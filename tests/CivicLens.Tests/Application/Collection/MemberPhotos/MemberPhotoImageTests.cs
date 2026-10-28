using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.MemberPhotos;

public sealed class MemberPhotoImageTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("image/jpeg; charset=binary")]
    public void AJpegOfThePublishedSizeIsValid(string contentType) =>
        Assert.True(MemberPhotoImage.IsValid(contentType, MemberPhotoFixtures.Jpeg()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    public void AnotherMediaTypeIsInvalidBecauseAnErrorPageCanKeepAPhotoUrl(string? contentType) =>
        Assert.False(MemberPhotoImage.IsValid(contentType, MemberPhotoFixtures.Jpeg()));

    [Theory]
    [InlineData(0xFF, 0xD8, 0x00)]
    [InlineData(0x89, 0x50, 0x4E)]
    [InlineData(0x3C, 0x68, 0x74)]
    public void ABodyWithoutTheJpegSignatureIsInvalid(int first, int second, int third)
    {
        var body = MemberPhotoFixtures.Jpeg();
        (body[0], body[1], body[2]) = ((byte)first, (byte)second, (byte)third);

        Assert.False(MemberPhotoImage.IsValid("image/jpeg", body));
    }

    [Theory]
    [InlineData(224, 275)]
    [InlineData(226, 275)]
    [InlineData(225, 274)]
    [InlineData(225, 276)]
    [InlineData(450, 550)]
    [InlineData(275, 225)]
    public void AnyOtherSizeIsInvalidBecauseTheSourceFixesOneFrame(int width, int height) =>
        Assert.False(MemberPhotoImage.IsValid("image/jpeg", MemberPhotoFixtures.Jpeg(width, height)));

    [Fact]
    public void AJpegCutBeforeItsFrameHeaderIsInvalid()
    {
        var body = MemberPhotoFixtures.Jpeg();

        Assert.False(MemberPhotoImage.IsValid("image/jpeg", body[..24]));
        Assert.False(MemberPhotoImage.IsValid("image/jpeg", body[..27]));
        Assert.False(MemberPhotoImage.IsValid("image/jpeg", []));
    }

    [Fact]
    public void AJpegWithNoFrameHeaderBeforeTheScanIsInvalid()
    {
        byte[] body = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0x00, 0x00, 0xFF, 0xD9];

        Assert.False(MemberPhotoImage.IsValid("image/jpeg", body));
    }
}
