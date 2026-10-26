using Krane.ScopeZoomPresets;
using Xunit;

public class GroupKeysTests
{
    [Fact]
    public void Name_FormatsDecimals()
    {
        Assert.Equal("1x-6x", GroupKeys.Name(1, 6));
        Assert.Equal("2.5x-8x", GroupKeys.Name(2.5f, 8));
        Assert.Equal("6.5x-20x", GroupKeys.Name(6.5f, 20));
    }

    [Fact]
    public void Find_ListedRange()
    {
        Assert.Equal("1x-6x", GroupKeys.Find(new[] { 1f, 6f }));
        Assert.Equal("7x-35x", GroupKeys.Find(new[] { 7f, 35f }));
    }

    [Fact]
    public void Find_ReversedAndDuplicatedLabels()
    {
        Assert.Equal("1x-6x", GroupKeys.Find(new[] { 6f, 1f }));
        Assert.Equal("3x-10x", GroupKeys.Find(new[] { 3f, 3f, 10f, 10f }));
    }

    [Fact]
    public void Find_UnlistedOrSingleValueIsNull()
    {
        Assert.Null(GroupKeys.Find(new[] { 2f, 8f }));
        Assert.Null(GroupKeys.Find(new[] { 1f }));
        Assert.Null(GroupKeys.Find(new float[0]));
        Assert.Null(GroupKeys.Find(null));
    }

    [Fact]
    public void Ranges_HasAllTwelveGroups()
    {
        Assert.Equal(12, GroupKeys.Ranges.Length);
    }
}
