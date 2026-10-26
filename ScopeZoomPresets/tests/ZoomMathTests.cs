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
        Assert.Equal("0% · ~1.0x", ZoomMath.FormatLabel(0, 1f));
        Assert.Equal("100% · ~6.0x", ZoomMath.FormatLabel(100, 6f));
        Assert.Equal("101% · ~6.0x", ZoomMath.FormatLabel(101, 6f));
    }

    [Fact]
    public void InverseCurve_Identity()
    {
        Assert.Equal(0.3f, ZoomMath.InverseCurve(t => t, 0.3f), 4);
    }

    [Fact]
    public void InverseCurve_Quadratic()
    {
        // curve(t) = t^2, so curve(t) == 0.25 at t == 0.5.
        Assert.Equal(0.5f, ZoomMath.InverseCurve(t => t * t, 0.25f), 4);
    }

    [Fact]
    public void InverseCurve_EdgesAndClamp()
    {
        Assert.Equal(0f, ZoomMath.InverseCurve(t => t * t, 0f), 4);
        Assert.Equal(1f, ZoomMath.InverseCurve(t => t * t, 1f), 4);
        Assert.Equal(0f, ZoomMath.InverseCurve(t => t * t, -0.5f), 4);
        Assert.Equal(1f, ZoomMath.InverseCurve(t => t * t, 2f), 4);
    }

    [Fact]
    public void CurveInputFov_MapsBackToTarget()
    {
        System.Func<float, float> curve = t => t * t;
        float target = ZoomMath.PercentToFov(25, Wide, Narrow);
        float input = ZoomMath.CurveInputFov(target, Wide, Narrow, curve);
        // Replays method_4: float_2 = |range| * curve(InverseLerp(Max, Min, input)) + Max.
        float ratio = (input - Narrow) / (Wide - Narrow);
        float mapped = (Wide - Narrow) * curve(ratio) + Narrow;
        Assert.Equal(target, mapped, 3);
    }

    [Fact]
    public void CurveInputFov_IdentityCurveIsTarget()
    {
        Assert.Equal(15f, ZoomMath.CurveInputFov(15f, Wide, Narrow, t => t), 3);
    }

    [Fact]
    public void CurveInputFov_NativeEdgesAreExact()
    {
        System.Func<float, float> curve = t => t * t;
        Assert.Equal(Wide, ZoomMath.CurveInputFov(Wide, Wide, Narrow, curve));
        Assert.Equal(Narrow, ZoomMath.CurveInputFov(Narrow, Wide, Narrow, curve));
    }

    [Fact]
    public void IsSupportedRange_OnlyWideToNarrow()
    {
        // Vanilla clamps with (MaxFov, MinFov), so only MinMaxFov.x > .y works; others stay vanilla.
        Assert.True(ZoomMath.IsSupportedRange(23.4f, 1.37f));
        Assert.False(ZoomMath.IsSupportedRange(5f, 20f));
        Assert.False(ZoomMath.IsSupportedRange(10f, 10f));
    }

    [Fact]
    public void PercentToFov_EdgesAreExact()
    {
        Assert.Equal(23.4f, ZoomMath.PercentToFov(0, 23.4f, 1.37f));
        Assert.Equal(1.37f, ZoomMath.PercentToFov(100, 23.4f, 1.37f));
        Assert.Equal(23.4f, ZoomMath.PercentToFov(-5, 23.4f, 1.37f));
        Assert.Equal(1.37f, ZoomMath.PercentToFov(250, 23.4f, 1.37f));
    }

    [Fact]
    public void IsCurveValid_AcceptsIdentityAndMonotonicCurves()
    {
        Assert.True(ZoomMath.IsCurveValid(t => t));
        Assert.True(ZoomMath.IsCurveValid(t => t * t));
        Assert.True(ZoomMath.IsCurveValid(t => t * t * (3f - 2f * t)));
    }

    [Fact]
    public void IsCurveValid_RejectsWrongEdgesOrNonMonotonic()
    {
        Assert.False(ZoomMath.IsCurveValid(t => t + 0.2f));
        Assert.False(ZoomMath.IsCurveValid(t => t * 0.5f));
        Assert.False(ZoomMath.IsCurveValid(t => 1f - t));
        Assert.False(ZoomMath.IsCurveValid(t => t < 0.5f ? t : (t < 0.7f ? 0.4f : t)));
        Assert.False(ZoomMath.IsCurveValid(t => float.NaN));
    }

    [Fact]
    public void IsCurveValid_RejectsDipBetweenCoarseSamples()
    {
        // Dips at 33/64, between the 1/16 grid points.
        Assert.False(ZoomMath.IsCurveValid(t => System.Math.Abs(t - 33f / 64f) < 0.004f ? t - 0.1f : t));
    }

    [Fact]
    public void IsCurveValid_RejectsCurveThatDoesNotRoundTrip()
    {
        // Monotonic non-decreasing with correct edges, but jumps from 0.25 to 0.5 at t = 0.5: no t maps to 0.4.
        Assert.False(ZoomMath.IsCurveValid(t => t < 0.5f ? t * 0.5f : t));
    }
}
