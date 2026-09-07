using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;

namespace Benchmark;

public class AllNet11PreviewsConfig: ManualConfig
{
    public AllNet11PreviewsConfig()
    {
        var preview4 = Job.Default
            .WithId(".NET 11 Preview 4")
            .WithRuntime(CoreRuntime.Core11_0)
            .WithToolchain(CsProjCoreToolchain.From(new NetCoreAppSettings(
                targetFrameworkMoniker: "net11.0",
                runtimeFrameworkVersion: "11.0.0-preview.4.26230.115", 
                name: ".NET 11 P4")))
            .AsBaseline();
        
        var preview7 = Job.Default
            .WithId(".NET 11 Preview 7")
            .WithRuntime(CoreRuntime.Core11_0)
            .WithToolchain(CsProjCoreToolchain.From(new NetCoreAppSettings(
                targetFrameworkMoniker: "net11.0",
                runtimeFrameworkVersion: "11.0.0-preview.7.26381.103",
                name: ".NET 11 P7")));

        AddJob(preview4, preview7);
    }
}