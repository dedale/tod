using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tod.Jenkins;
using Tod.Tests.Jenkins;

namespace Tod.Memory;

[MemoryDiagnoser]
public class DeSerializeBenchmark
{
    private string _payload;
    private JsonSerializerOptions _options;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var testBuild = RandomData.NextTestBuild(testJobName: "GitHub/TestFactories/CUSTOM-Core-dev-test-net6");
        _options = new JsonSerializerOptions
        {
            //PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        _options.Converters.Add(new JsonStringEnumConverter());
        _options.Converters.Add(new SingleStringValueConverterFactory());
        _payload = JsonSerializer.Serialize(testBuild, _options);
    }

    [Benchmark]
    public string Deserialize()
    {
        return JsonSerializer.Deserialize<TestBuild>(_payload, _options)!.JobName.Value;
    }
}

internal class Program
{
    static void Main() => BenchmarkRunner.Run<DeSerializeBenchmark>();
}
