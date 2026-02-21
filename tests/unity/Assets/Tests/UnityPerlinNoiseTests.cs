using NUnit.Framework;
using UnityEngine;

namespace WorldZones.WorldGen.Tests
{
    [TestFixture]
    public class UnityPerlinNoiseTests
    {
        [Test]
        public void CanCallMathfPerlinNoise_ReturnsValidValue()
        {
            // Arrange
            float x = 1.5f;
            float y = 2.5f;

            // Act
            float result = Mathf.PerlinNoise(x, y);

            // Assert
            Assert.That(result, Is.GreaterThanOrEqualTo(0f), "Perlin noise should return >= 0");
            Assert.That(result, Is.LessThanOrEqualTo(1f), "Perlin noise should return <= 1");
        }

        [Test]
        public void MathfPerlinNoise_IsDeterministic()
        {
            // Arrange
            float x = 3.7f;
            float y = 8.2f;

            // Act
            float result1 = Mathf.PerlinNoise(x, y);
            float result2 = Mathf.PerlinNoise(x, y);

            // Assert
            Assert.That(result1, Is.EqualTo(result2), "Perlin noise should be deterministic");
        }

        [Test]
        public void MathfPerlinNoise_CoordinateWrapping()
        {
            // Test that wrapping at 256 boundary works
            float baseX = 10.5f;
            float baseY = 20.3f;
            
            float result1 = Mathf.PerlinNoise(baseX, baseY);
            float result2 = Mathf.PerlinNoise(baseX + 256f, baseY);
            float result3 = Mathf.PerlinNoise(baseX, baseY + 256f);
            
            // Unity Perlin repeats every 256 units
            Assert.That(result2, Is.EqualTo(result1).Within(0.0001f), 
                "Perlin should repeat at X+256");
            Assert.That(result3, Is.EqualTo(result1).Within(0.0001f), 
                "Perlin should repeat at Y+256");
        }

        [Test]
        public void MathfPerlinNoise_KnownValues()
        {
            // Test some known coordinates to establish baseline
            var testCases = new[]
            {
                (x: 0f, y: 0f),
                (x: 1f, y: 1f),
                (x: 10.5f, y: 20.3f),
                (x: 100.123f, y: 200.456f),
            };

            foreach (var (x, y) in testCases)
            {
                float result = Mathf.PerlinNoise(x, y);
                
                // Log for reference
                UnityEngine.Debug.Log($"Mathf.PerlinNoise({x}, {y}) = {result}");
                
                // Verify it returns something in valid range
                Assert.That(result, Is.InRange(0f, 1f), 
                    $"Perlin({x}, {y}) should return value in [0, 1]");
            }
        }
    }
}
