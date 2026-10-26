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

    [Fact]
    public void VariableEntry_FirstNonFixedEntry()
    {
        // NcSTAR ADO: variable optic first, fixed 1x backup second.
        var ado = new[] { new[] { 3f, 9f }, new[] { 1f } };
        Assert.Same(ado[0], GroupKeys.VariableEntry(ado));
        // Fixed entry first, variable second.
        var swapped = new[] { new[] { 1f }, new[] { 3f, 9f } };
        Assert.Same(swapped[1], GroupKeys.VariableEntry(swapped));
    }

    [Fact]
    public void VariableEntry_DuplicatedLabelsStillVariable()
    {
        var zooms = new[] { new[] { 3f, 3f, 10f, 10f } };
        Assert.Same(zooms[0], GroupKeys.VariableEntry(zooms));
    }

    [Fact]
    public void VariableEntry_NoneOrNullIsNull()
    {
        Assert.Null(GroupKeys.VariableEntry(new[] { new[] { 1f } }));
        Assert.Null(GroupKeys.VariableEntry(new[] { new[] { 4f, 4f } }));
        Assert.Null(GroupKeys.VariableEntry(new[] { (float[])null, new float[0] }));
        Assert.Null(GroupKeys.VariableEntry(null));
    }

    [Fact]
    public void VariableEntry_PrefersSelectedVariableEntry()
    {
        var zooms = new[] { new[] { 1f, 4f }, new[] { 3f, 12f } };
        Assert.Same(zooms[1], GroupKeys.VariableEntry(zooms, 1));
        Assert.Same(zooms[0], GroupKeys.VariableEntry(zooms, 0));
    }

    [Fact]
    public void VariableEntry_FixedSelectedFallsBackToFirstVariable()
    {
        var ado = new[] { new[] { 3f, 9f }, new[] { 1f } };
        Assert.Same(ado[0], GroupKeys.VariableEntry(ado, 1));
    }

    [Fact]
    public void VariableEntry_SelectedOutOfRangeFallsBackToFirstVariable()
    {
        var zooms = new[] { new[] { 3f, 9f }, new[] { 1f } };
        Assert.Same(zooms[0], GroupKeys.VariableEntry(zooms, 5));
        Assert.Same(zooms[0], GroupKeys.VariableEntry(zooms, -1));
    }

    [Fact]
    public void IsVariable_Entry()
    {
        Assert.True(GroupKeys.IsVariable(new[] { 3f, 9f }));
        Assert.False(GroupKeys.IsVariable(new[] { 1f }));
        Assert.False(GroupKeys.IsVariable(new[] { 2f, 2f }));
        Assert.False(GroupKeys.IsVariable(null));
    }

    [Fact]
    public void MinLabel_SmallestEntryValue()
    {
        Assert.Equal(3f, GroupKeys.MinLabel(new[] { 3f, 9f }));
        Assert.Equal(3f, GroupKeys.MinLabel(new[] { 10f, 3f, 10f, 3f }));
        Assert.Equal(1f, GroupKeys.MinLabel(new[] { 1f }));
    }

    [Fact]
    public void Invariant_UsesDotAndKeepsPrecision()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1.37", GroupKeys.Invariant(1.37f));
            Assert.Equal("23.4", GroupKeys.Invariant(23.4f));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Find_IgnoresEntryOrder()
    {
        Assert.Equal(GroupKeys.Find(new[] { 3f, 9f }), GroupKeys.Find(new[] { 9f, 3f }));
        Assert.Equal("3x-9x", GroupKeys.Find(new[] { 3f, 9f }));
    }
}
