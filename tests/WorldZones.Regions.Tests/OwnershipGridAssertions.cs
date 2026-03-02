using Xunit;

namespace WorldZones.Regions.Tests
{
    internal static class OwnershipGridAssertions
    {
        public static void AssertEqual(int[,] expected, int[,] actual)
        {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));

            int rows = expected.GetLength(0);
            int columns = expected.GetLength(1);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Assert.Equal(expected[y, x], actual[y, x]);
                }
            }
        }
    }
}
