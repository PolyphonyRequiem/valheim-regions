using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class ShowGroundTruthAccuracy
    {
        readonly ITestOutputHelper output;
        
        public ShowGroundTruthAccuracy(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void ShowAccuracy()
        {
            var test = new GroundTruthComparisonTests(this.output);
            test.CompareAgainstValheimExport_HHcLC5acQt();
        }
    }
}
