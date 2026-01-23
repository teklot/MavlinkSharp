using BenchmarkDotNet.Attributes;

namespace MavLinkSharp.Benchmark
{
    [MemoryDiagnoser]
    public class InitializationBenchmark
    {
        private const string DialectFileName = "common.xml";
        private string? _commonXmlPath;

        [GlobalSetup]
        public void Setup()
        {
            // Find the common.xml file path
            var currentDirectory = Directory.GetCurrentDirectory();
            
            _commonXmlPath = Path.Combine(currentDirectory, "Dialects", DialectFileName);

            // In a real benchmark, we'd ensure a clean state.
            // For now, assume BenchmarkDotNet's process isolation helps.
            // MavLink.Initialize is designed to be called once, subsequent calls will re-initialize
            // but the first parse is what we care about here.
        }

        [Benchmark]
        public void InitializeMavLink()
        {
            MavLink.Initialize(_commonXmlPath);
        }
    }
}
