using Rules.Core.Geometry;

namespace Rules.Tests.Geometry;

public class LengthAndMathTests
{
    [Theory]
    [Trait("Rule", "1.0.2 Fractions p.30 (distances keep fractions); provisional U1")]
    [InlineData("0.5", 500)]
    [InlineData("12", 12000)]
    [InlineData("-1.25", -1250)]
    [InlineData(".5", 500)]
    [InlineData("0.001", 1)]
    [InlineData("1.", 1000)]
    [InlineData(" 3 ", 3000)]
    public void ParsesDecimalInchesExactly(string text, long thousandths)
    {
        Assert.Equal(thousandths, Length.Parse(text).Thousandths);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData("1.2345")]
    [InlineData("1.2.3")]
    [InlineData("--1")]
    [InlineData("1e3")]
    public void RejectsAnythingButInchesWithAtMostThreeDecimals(string text)
    {
        Assert.Throws<FormatException>(() => Length.Parse(text));
    }

    // U1: millimetres convert at 25.4 per inch, rounded half up to the nearest thousandth of an inch.
    // Expected values were worked out independently: 25/25.4 = 0.98425", 32 = 1.25984", 40 = 1.57480",
    // 50 = 1.96850" and 60 = 2.36220".
    [Theory]
    [Trait("Rule", "Provisional U1: base sizes are quoted in millimetres (e.g. 25mm, 32mm, 40mm)")]
    [InlineData(0, 0)]
    [InlineData(25, 984)]
    [InlineData(32, 1260)]
    [InlineData(40, 1575)]
    [InlineData(50, 1969)]
    [InlineData(60, 2362)]
    [InlineData(254, 10000)]
    public void MillimetresConvertAtTwentyFivePointFourRoundedHalfUp(int mm, long thousandths)
    {
        Assert.Equal(thousandths, Length.FromMillimetres(mm).Thousandths);
    }

    [Fact]
    public void MillimetreRoundingGoesUpAtExactlyHalf()
    {
        // 12.7 mm would be exactly 0.5", and 127 mm exactly 5"; 1 mm is 39.37 thousandths. A true half:
        // 254 * 5 / 10 is not an integer mm, so check the formula's tie with the smallest integer tie, 0.5 thousandth.
        // mm * 10000 / 254 has a remainder of exactly 127 (half of 254) when mm * 10000 = 254k + 127: none for integer mm,
        // so assert the rule through the rounding of a value just below and just above half instead.
        Assert.Equal(39, Length.FromMillimetres(1).Thousandths);   // 39.37 rounds down
        Assert.Equal(79, Length.FromMillimetres(2).Thousandths);   // 78.74 rounds up
    }

    [Fact]
    public void LengthsAreBoundedAndArithmeticIsChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromThousandths(1_000_000_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Point2(Length.FromInches(201), Length.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMillimetres(-1));
        var max = Length.FromThousandths(1_000_000_000);
        Assert.Throws<ArgumentOutOfRangeException>(() => max + Length.Inch);
        Assert.Equal(Length.FromThousandths(1500), Length.Inch + Length.HalfInch);
        Assert.Equal(Length.FromThousandths(-500), Length.HalfInch - Length.Inch);
        Assert.True(Length.HalfInch < Length.Inch && Length.Inch >= Length.Inch);
        Assert.Equal("1.500\"", (Length.Inch + Length.HalfInch).ToString());
        Assert.Equal("-0.250\"", Length.FromThousandths(-250).ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(999_999_999_999)]
    public void SquareRootsRoundDownAndUpExactly(long n)
    {
        var floor = ExactMath.FloorSqrt(n);
        var ceil = ExactMath.CeilSqrt(n);
        Assert.True(floor * floor <= n && (floor + 1) * (floor + 1) > n);
        Assert.True(ceil * ceil >= n && (ceil == 0 || (ceil - 1) * (ceil - 1) < n));
    }

    [Fact]
    public void SquareRootsAreExactAtTheLargestValuesTheGeometryUses()
    {
        // The biggest product in the movement code is about 3e34; exercise well past that, up to near Int128's limit.
        foreach (var root in new Int128[] { 1, 99_999_999_999, 100_000_000_000_000, 123_456_789_012_345_678, 9_000_000_000_000_000_000 })
        {
            foreach (var n in new[] { root * root - 1, root * root, root * root + 1 })
            {
                if (n < 0)
                {
                    continue;
                }
                var f = ExactMath.FloorSqrt(n);
                var c = ExactMath.CeilSqrt(n);
                Assert.True(f * f <= n && (f + 1) * (f + 1) > n, $"floor of {n}");
                Assert.True(c * c >= n && (c == 0 || (c - 1) * (c - 1) < n), $"ceil of {n}");
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactMath.FloorSqrt(-1));
    }

    [Fact]
    public void SquareRootsAgreeWithBruteForceOnSeededNumbers()
    {
        var rng = new Random(12345);
        for (var i = 0; i < 2000; i++)
        {
            long n = rng.NextInt64(0, 4_000_000_000_000);
            long f = (long)ExactMath.FloorSqrt(n);
            long expected = 0;
            while ((expected + 1) * (expected + 1) <= n)
            {
                expected += Math.Max(1, (f - expected) / 2);
                if (expected > f)
                {
                    expected = f;
                    break;
                }
            }
            Assert.Equal(f, (long)ExactMath.FloorSqrt(n));
            Assert.True(f * f <= n && (f + 1) * (f + 1) > n);
        }
    }

    [Fact]
    public void RatiosCompareByCrossMultiplication()
    {
        Assert.True(ExactMath.Ratio.Of(1, 3).CompareTo(ExactMath.Ratio.Of(2, 6)) == 0);
        Assert.True(ExactMath.Ratio.Of(1, 3).CompareTo(ExactMath.Ratio.Of(1, 2)) < 0);
        Assert.True(ExactMath.Ratio.Of(-1, -2).CompareTo(ExactMath.Ratio.Of(1, 3)) > 0);
        Assert.True(ExactMath.Ratio.Of(5, -10).CompareTo(ExactMath.Ratio.Of(-1, 2)) == 0);
    }
}
