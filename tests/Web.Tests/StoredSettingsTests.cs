using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class StoredSettingsTests
{
    [Fact]
    public void Missing_fields_stay_null_not_zero()
    {
        var s = StoredSettings.Parse("{\"Theme\":\"frost\"}")!;
        Assert.Equal("frost", s.Theme);
        Assert.Null(s.Master); Assert.Null(s.Sfx); Assert.Null(s.Music); Assert.Null(s.Muted);
    }

    [Fact]
    public void Full_blob_round_trips() =>
        Assert.Equal(0.25, StoredSettings.Parse("{\"Master\":0.25,\"Muted\":true}")!.Master);

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("{bad")]
    public void Invalid_returns_null(string? raw) => Assert.Null(StoredSettings.Parse(raw));
}
