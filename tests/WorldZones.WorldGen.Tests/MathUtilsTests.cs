using Xunit;

namespace WorldZones.WorldGen.Tests
{
    public class MathUtilsTests
    {
    [Fact]
    public void Length_Origin_ReturnsZero()
    {
        float result = MathUtils.Length(0f, 0f);
        
        Assert.Equal(0f, result, precision: 5);
    }
    
    [Fact]
    public void Length_UnitVectors_ReturnsCorrectDistance()
    {
        Assert.Equal(1f, MathUtils.Length(1f, 0f), precision: 5);
        Assert.Equal(1f, MathUtils.Length(0f, 1f), precision: 5);
        Assert.Equal(1.41421f, MathUtils.Length(1f, 1f), precision: 5);
    }
    
    [Fact]
    public void Length_NegativeCoordinates_ReturnsPositiveDistance()
    {
        Assert.Equal(5f, MathUtils.Length(-3f, -4f), precision: 5);
        Assert.Equal(5f, MathUtils.Length(3f, 4f), precision: 5);
    }
    
    [Fact]
    public void Lerp_ZeroT_ReturnsStartValue()
    {
        float result = MathUtils.Lerp(10f, 20f, 0f);
        
        Assert.Equal(10f, result);
    }
    
    [Fact]
    public void Lerp_OneT_ReturnsEndValue()
    {
        float result = MathUtils.Lerp(10f, 20f, 1f);
        
        Assert.Equal(20f, result);
    }
    
    [Fact]
    public void Lerp_HalfT_ReturnsMidpoint()
    {
        float result = MathUtils.Lerp(10f, 20f, 0.5f);
        
        Assert.Equal(15f, result);
    }
    
    [Fact]
    public void Lerp_ExtrapolatesBeyondRange()
    {
        float result = MathUtils.Lerp(10f, 20f, 2f);
        
        Assert.Equal(30f, result);
    }
    
    [Fact]
    public void LerpStep_ValueBelowMin_ReturnsZero()
    {
        float result = MathUtils.LerpStep(10f, 20f, 5f);
        
        Assert.Equal(0f, result);
    }
    
    [Fact]
    public void LerpStep_ValueAboveMax_ReturnsOne()
    {
        float result = MathUtils.LerpStep(10f, 20f, 25f);
        
        Assert.Equal(1f, result);
    }
    
    [Fact]
    public void LerpStep_ValueAtMidpoint_ReturnsHalf()
    {
        float result = MathUtils.LerpStep(10f, 20f, 15f);
        
        Assert.Equal(0.5f, result, precision: 5);
    }
    
    [Fact]
    public void LerpStep_MinEqualsMax_ReturnsZero()
    {
        float result = MathUtils.LerpStep(10f, 10f, 10f);
        
        Assert.Equal(0f, result);
    }
    
    [Fact]
    public void SmoothStep_ValueBelowMin_ReturnsZero()
    {
        float result = MathUtils.SmoothStep(10f, 20f, 5f);
        
        Assert.Equal(0f, result);
    }
    
    [Fact]
    public void SmoothStep_ValueAboveMax_ReturnsOne()
    {
        float result = MathUtils.SmoothStep(10f, 20f, 25f);
        
        Assert.Equal(1f, result);
    }
    
    [Fact]
    public void SmoothStep_ValueAtMidpoint_ReturnsSmoothHalf()
    {
        float result = MathUtils.SmoothStep(10f, 20f, 15f);
        
        // At t=0.5, smoothstep formula: 3(0.5)² - 2(0.5)³ = 0.75 - 0.25 = 0.5
        Assert.Equal(0.5f, result, precision: 5);
    }
    
    [Fact]
    public void SmoothStep_QuarterPoint_HasSmoothCurve()
    {
        float result = MathUtils.SmoothStep(0f, 1f, 0.25f);
        
        // At t=0.25, smoothstep: 3(0.25)² - 2(0.25)³ = 0.1875 - 0.03125 = 0.15625
        Assert.Equal(0.15625f, result, precision: 5);
    }
    
    [Fact]
    public void Clamp01_NegativeValue_ReturnsZero()
    {
        Assert.Equal(0f, MathUtils.Clamp01(-5f));
        Assert.Equal(0f, MathUtils.Clamp01(-0.1f));
    }
    
    [Fact]
    public void Clamp01_ValueAboveOne_ReturnsOne()
    {
        Assert.Equal(1f, MathUtils.Clamp01(5f));
        Assert.Equal(1f, MathUtils.Clamp01(1.1f));
    }
    
    [Fact]
    public void Clamp01_ValueInRange_ReturnsValue()
    {
        Assert.Equal(0.5f, MathUtils.Clamp01(0.5f));
        Assert.Equal(0f, MathUtils.Clamp01(0f));
        Assert.Equal(1f, MathUtils.Clamp01(1f));
    }
    
    [Fact]
    public void Abs_PositiveValue_ReturnsSameValue()
    {
        Assert.Equal(5f, MathUtils.Abs(5f));
    }
    
    [Fact]
    public void Abs_NegativeValue_ReturnsPositiveValue()
    {
        Assert.Equal(5f, MathUtils.Abs(-5f));
    }
    
    [Fact]
    public void Abs_Zero_ReturnsZero()
    {
        Assert.Equal(0f, MathUtils.Abs(0f));
    }
}
}

