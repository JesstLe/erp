using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class LegacyCustomerPhotoExportTests
{
    [Fact]
    public void ParsesExplicitCarePhotoExclusion()
    {
        var input = Path.Combine(Path.GetTempPath(), $"erp-legacy-extra-input-{Guid.NewGuid():N}");
        var output = Path.Combine(Path.GetTempPath(), $"erp-legacy-extra-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(input);
        try
        {
            var options = LegacyExtraExportOptions.Parse(
            [
                "extras",
                "--input", input,
                "--output", output,
                "--skip-care-photos", "true"
            ]);

            Assert.True(options.SkipCarePhotos);
        }
        finally
        {
            Directory.Delete(input, recursive: true);
        }
    }

    [Fact]
    public void ResolvesRelativeLegacyPhotoAgainstMemberPage()
    {
        const string html = """
            <div id="div-image1"><div class="preview">
              <img src="../picture/21091626/member/example_1.jpg" class="img-thumbnail">
            </div></div>
            """;

        var photos = LegacyCustomerPhotoExportEngine.ParsePhotoUris(html);

        var photo = Assert.Single(photos);
        Assert.Equal(1, photo.Slot);
        Assert.Equal("https://app5.siweicloud.com/swshop/picture/21091626/member/example_1.jpg",
            photo.Uri.AbsoluteUri);
    }

    [Fact]
    public void IgnoresNonPictureImagesAndKeepsTwoDistinctSlots()
    {
        const string html = """
            <img src="../public/theme/icon.png">
            <img src="../picture/a1/member/first.jpg">
            <img src="../picture/a1/member/second.webp">
            <img src="../picture/a1/member/second.webp">
            """;

        var photos = LegacyCustomerPhotoExportEngine.ParsePhotoUris(html);

        Assert.Equal(2, photos.Count);
        Assert.Equal([1, 2], photos.Select(x => x.Slot));
    }

    [Fact]
    public void ResolvesRelativeCarePhotoAgainstNursePage()
    {
        const string html = """
            <img src="../picture/21091626/nurse/120260626133419.jpg">
            """;

        var photos = LegacyCustomerPhotoExportEngine.ParseCarePhotoUris(html);

        var photo = Assert.Single(photos);
        Assert.Equal("https://app5.siweicloud.com/swshop/picture/21091626/nurse/120260626133419.jpg",
            photo.Uri.AbsoluteUri);
    }
}
