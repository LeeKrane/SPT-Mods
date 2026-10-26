using Krane.ScopeZoomPresets;
using Xunit;

public class ZoomMathTests
{
    // Razor HD Gen 2: MinMaxFov x=26.5 (1x), y=4.4167 (6x).
    private const float Wide = 26.5f;
    private const float Narrow = 4.4167f;

    // 1x-6x Razor: wide FOV = 1x, narrow FOV = 6x.
    [Fact]
    public void ZoomToFov_EdgesAreExact()
    {
        Assert.Equal(Wide, ZoomMath.ZoomToFov(1f, 1f, 6f, Wide, Narrow));
        Assert.Equal(Narrow, ZoomMath.ZoomToFov(6f, 1f, 6f, Wide, Narrow));
    }

    [Fact]
    public void ZoomToFov_ClampsOutsideRange()
    {
        Assert.Equal(Wide, ZoomMath.ZoomToFov(0.2f, 1f, 6f, Wide, Narrow));
        Assert.Equal(Narrow, ZoomMath.ZoomToFov(9f, 1f, 6f, Wide, Narrow));
    }

    [Fact]
    public void ZoomToFov_InBetweenIsStrictlyBetweenAndDecreasing()
    {
        float a = ZoomMath.ZoomToFov(2f, 1f, 6f, Wide, Narrow);
        float b = ZoomMath.ZoomToFov(4f, 1f, 6f, Wide, Narrow);
        Assert.InRange(a, Narrow + 0.001f, Wide - 0.001f);
        Assert.InRange(b, Narrow + 0.001f, Wide - 0.001f);
        Assert.True(b < a);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    [InlineData(3.3f)]
    [InlineData(5.9f)]
    public void FovToZoom_InvertsZoomToFov(float zoom)
    {
        float fov = ZoomMath.ZoomToFov(zoom, 1f, 6f, Wide, Narrow);
        Assert.Equal(zoom, ZoomMath.FovToZoom(fov, 1f, 6f, Wide, Narrow), 3);
    }

    [Fact]
    public void FovToZoom_EdgesAreExactLabels()
    {
        Assert.Equal(2.5f, ZoomMath.FovToZoom(Wide, 2.5f, 8f, Wide, Narrow));
        Assert.Equal(8f, ZoomMath.FovToZoom(Narrow, 2.5f, 8f, Wide, Narrow));
        Assert.Equal(2.5f, ZoomMath.FovToZoom(Wide + 3f, 2.5f, 8f, Wide, Narrow));
        Assert.Equal(8f, ZoomMath.FovToZoom(Narrow - 1f, 2.5f, 8f, Wide, Narrow));
    }

    [Fact]
    public void ZoomToFov_DegenerateRangeReturnsWide()
    {
        Assert.Equal(Wide, ZoomMath.ZoomToFov(3f, 4f, 4f, Wide, Narrow));
        Assert.Equal(4f, ZoomMath.FovToZoom(10f, 4f, 4f, Wide, Narrow));
    }

    [Theory]
    [InlineData(1.04f, 1.0f)]
    [InlineData(1.06f, 1.1f)]
    [InlineData(4.24f, 4.2f)]
    public void SnapZoom_RoundsToTenths(float input, float expected)
    {
        Assert.Equal(expected, ZoomMath.SnapZoom(input), 3);
    }

    [Fact]
    public void ToTenths_Rounds()
    {
        Assert.Equal(42, ZoomMath.ToTenths(4.24f));
        Assert.Equal(60, ZoomMath.ToTenths(6f));
    }

    [Fact]
    public void FormatZoom_OneDecimalAndX()
    {
        Assert.Equal("4.2x", ZoomMath.FormatZoom(42));
        Assert.Equal("1.0x", ZoomMath.FormatZoom(10));
        Assert.Equal("35.0x", ZoomMath.FormatZoom(350));
        Assert.Equal("0.5x", ZoomMath.FormatZoom(5));
        Assert.Equal("1234.5x", ZoomMath.FormatZoom(12345));
        Assert.Same(ZoomMath.FormatZoom(42), ZoomMath.FormatZoom(42));
    }

    [Fact]
    public void Order_SwapsZoomPoints()
    {
        float lo = 4.5f, hi = 2f;
        ZoomMath.Order(ref lo, ref hi);
        Assert.Equal(2f, lo);
        Assert.Equal(4.5f, hi);
    }

    [Fact]
    public void ToggleTarget_GoesToFartherPoint()
    {
        float lo = ZoomMath.ZoomToFov(1.5f, 1f, 6f, Wide, Narrow);
        float hi = ZoomMath.ZoomToFov(4.2f, 1f, 6f, Wide, Narrow);
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
        float target = ZoomMath.ZoomToFov(2f, 1f, 6f, Wide, Narrow);
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
