using System;
using System.IO;
using Krane.AdsCameraDistance;
using Xunit;

public class OffsetMathTests
{
    [Fact]
    public void Adjust_ScrollUpGoesCloser()
    {
        Assert.Equal(-0.005f, OffsetMath.Adjust(0f, 1, 0.005f, -0.15f, 0.10f));
        Assert.Equal(0.005f, OffsetMath.Adjust(0f, -1, 0.005f, -0.15f, 0.10f));
    }

    [Fact]
    public void Adjust_ClampsAtBounds()
    {
        Assert.Equal(-0.15f, OffsetMath.Adjust(-0.148f, 5, 0.005f, -0.15f, 0.10f));
        Assert.Equal(0.10f, OffsetMath.Adjust(0.098f, -5, 0.005f, -0.15f, 0.10f));
    }

    [Fact]
    public void Adjust_DoesNotDrift()
    {
        float v = 0f;
        for (int i = 0; i < 7; i++)
            v = OffsetMath.Adjust(v, 1, 0.005f, -0.15f, 0.10f);
        Assert.Equal(-0.035f, v);
        for (int i = 0; i < 7; i++)
            v = OffsetMath.Adjust(v, -1, 0.005f, -0.15f, 0.10f);
        Assert.True(OffsetMath.IsVanilla(v));
    }

    [Fact]
    public void Normalize_SwapsAndContainsZero()
    {
        float min = 0.1f, max = -0.2f;
        OffsetMath.Normalize(ref min, ref max);
        Assert.Equal(-0.2f, min);
        Assert.Equal(0.1f, max);
        float a = 0.05f, b = 0.2f;
        OffsetMath.Normalize(ref a, ref b);
        Assert.Equal(0f, a);
    }

    [Fact]
    public void Clamp_NanBecomesZero()
    {
        Assert.Equal(0f, OffsetMath.Clamp(float.NaN, -1f, 1f));
        Assert.Equal(0f, OffsetMath.Clamp(float.PositiveInfinity, -1f, 1f));
    }

    [Fact]
    public void Labels()
    {
        Assert.Equal("Eye dist -0.035", OffsetMath.FormatLabel(-0.035f));
        Assert.Equal("Eye dist 0.020", OffsetMath.FormatLabel(0.02f));
        Assert.Equal("Eye dist vanilla", OffsetMath.FormatLabel(0f));
    }

    [Fact]
    public void Keys()
    {
        Assert.Equal("w|s|1", OffsetMath.BuildKey("w", "s", 1));
        Assert.Equal("w|iron|0", OffsetMath.BuildIronKey("w", 0));
    }
}

public class OffsetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "adscd-" + Guid.NewGuid().ToString("N"));

    public OffsetStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    private string P => Path.Combine(_dir, "o.json");

    [Fact]
    public void RoundTrip()
    {
        var a = new OffsetStore();
        a.Set("w|s|0", -0.035f);
        a.Set("w|iron|1", 0.02f);
        a.Set("we\"ird\\key|x|0", -0.1f);
        Assert.True(a.Dirty);
        a.Save(P);
        Assert.False(a.Dirty);

        var b = new OffsetStore();
        Assert.Null(b.Load(P));
        Assert.Equal(3, b.Count);
        Assert.Equal(-0.035f, b.Get("w|s|0"));
        Assert.Equal(0.02f, b.Get("w|iron|1"));
        Assert.Equal(-0.1f, b.Get("we\"ird\\key|x|0"));
        Assert.False(b.Dirty);
    }

    [Fact]
    public void ZeroRemovesEntry()
    {
        var a = new OffsetStore();
        a.Set("k", -0.01f);
        a.Set("k", 0f);
        Assert.Equal(0, a.Count);
    }

    [Fact]
    public void MissingFile_IsEmptyNoError()
    {
        var a = new OffsetStore();
        Assert.Null(a.Load(P));
        Assert.Equal(0, a.Count);
    }

    [Fact]
    public void Load_KeepsOutOfRangeValues()
    {
        File.WriteAllText(P, "{\"a\": -5, \"b\": 9, \"c\": -0.05}");
        var a = new OffsetStore();
        Assert.Null(a.Load(P));
        Assert.Equal(-5f, a.Get("a"));
        Assert.Equal(9f, a.Get("b"));
        Assert.Equal(-0.05f, a.Get("c"));
    }

    [Fact]
    public void Load_FallsBackToTmp()
    {
        File.WriteAllText(P + ".tmp", "{\"a\": -0.02}");
        var a = new OffsetStore();
        Assert.Null(a.Load(P));
        Assert.Equal(-0.02f, a.Get("a"));
    }

    [Fact]
    public void Save_ReplacesExisting()
    {
        var a = new OffsetStore();
        a.Set("a", -0.01f);
        a.Save(P);
        a.Set("a", -0.02f);
        a.Save(P);
        Assert.False(File.Exists(P + ".tmp"));
        var b = new OffsetStore();
        Assert.Null(b.Load(P));
        Assert.Equal(-0.02f, b.Get("a"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\": ")]
    [InlineData("{\"a\": \"x\"}")]
    [InlineData("[1,2]")]
    [InlineData("{\"a\": 1} trailing")]
    public void Corrupt_MovedToBak_StartsEmpty(string content)
    {
        File.WriteAllText(P, content);
        var a = new OffsetStore();
        string problem = a.Load(P);
        Assert.NotNull(problem);
        Assert.Equal(0, a.Count);
        Assert.False(File.Exists(P));
        Assert.Equal(content, File.ReadAllText(P + ".bak"));
    }

    [Fact]
    public void Corrupt_OverwritesOlderBak()
    {
        File.WriteAllText(P + ".bak", "old");
        File.WriteAllText(P, "garbage");
        var a = new OffsetStore();
        Assert.NotNull(a.Load(P));
        Assert.Equal("garbage", File.ReadAllText(P + ".bak"));
    }

    [Fact]
    public void Parse_AcceptsEmptyObjectAndWhitespace()
    {
        Assert.Empty(OffsetStore.Parse(" { } ", out _));
        Assert.Single(OffsetStore.Parse("{\n\"k\" :\t-1.5e-2\n}", out _));
    }

    [Fact]
    public void Version_ChangesOnEdit()
    {
        var a = new OffsetStore();
        int v = a.Version;
        a.Set("k", -0.01f);
        Assert.NotEqual(v, a.Version);
    }
}
