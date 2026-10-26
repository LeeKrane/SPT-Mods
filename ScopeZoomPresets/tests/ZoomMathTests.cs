using Krane.ScopeZoomPresets;
using Xunit;

public class ZoomMathTests
{
    // Razor HD Gen 2: MinMaxFov x=26.5 (1x), y=4.4167 (6x).
    private const float Wide = 26.5f;
    private const float Narrow = 4.4167f;

    [Fact]
    public void PercentToFov_Edges()
    {
        Assert.Equal(Wide, ZoomMath.PercentToFov(0, Wide, Narrow), 3);
        Assert.Equal(Narrow, ZoomMath.PercentToFov(100, Wide, Narrow), 3);
        Assert.Equal((Wide + Narrow) / 2, ZoomMath.PercentToFov(50, Wide, Narrow), 3);
    }

    [Fact]
    public void PercentToFov_ClampsOutOfRange()
    {
        Assert.Equal(Narrow, ZoomMath.PercentToFov(150, Wide, Narrow), 3);
        Assert.Equal(Wide, ZoomMath.PercentToFov(-10, Wide, Narrow), 3);
    }

    [Fact]
    public void FovToPercent_InvertsPercentToFov()
    {
        float fov = ZoomMath.PercentToFov(70, Wide, Narrow);
        Assert.Equal(70, ZoomMath.FovToPercent(fov, Wide, Narrow), 3);
    }

    [Fact]
    public void FovToPercent_ZeroRangeIsZero()
    {
        Assert.Equal(0, ZoomMath.FovToPercent(10, 10, 10));
    }

    [Fact]
    public void ApproxZoom_AtWideEdgeIsMinZoom()
    {
        Assert.Equal(1f, ZoomMath.ApproxZoom(Wide, Wide, 1f), 3);
    }

    [Fact]
    public void ApproxZoom_AtNarrowEdgeNearLabel()
    {
        float zoom = ZoomMath.ApproxZoom(Narrow, Wide, 1f);
        Assert.InRange(zoom, 5.5f, 6.5f);
    }

    [Fact]
    public void ToggleTarget_GoesToFartherPoint()
    {
        float lo = ZoomMath.PercentToFov(15, Wide, Narrow);
        float hi = ZoomMath.PercentToFov(70, Wide, Narrow);
        Assert.Equal(hi, ZoomMath.ToggleTarget(lo, lo, hi));
        Assert.Equal(lo, ZoomMath.ToggleTarget(hi, lo, hi));
        // Close to hi: farther is lo.
        Assert.Equal(lo, ZoomMath.ToggleTarget(hi + 0.5f, lo, hi));
    }

    [Fact]
    public void ToggleTarget_TieGoesToHi()
    {
        Assert.Equal(10f, ZoomMath.ToggleTarget(15f, 20f, 10f));
    }

    [Fact]
    public void ToggleTarget_EqualPointsReturnsThatPoint()
    {
        Assert.Equal(12f, ZoomMath.ToggleTarget(20f, 12f, 12f));
    }

    [Fact]
    public void Order_SwapsWhenLoAboveHi()
    {
        float lo = 80, hi = 20;
        ZoomMath.Order(ref lo, ref hi);
        Assert.Equal(20, lo);
        Assert.Equal(80, hi);
    }

    [Fact]
    public void FormatLabel_PercentAndZoom()
    {
        Assert.Equal("70% · ~4.2x", ZoomMath.FormatLabel(70, 4.24f));
    }
}
