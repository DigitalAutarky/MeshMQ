using System.Text.RegularExpressions;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Benchmark;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Validators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public  class StrictRuntimeValidator : IValidator
{
    public bool TreatsWarningsAsErrors => true;

    public async IAsyncEnumerable<ValidationError> ValidateAsync(ValidationParameters validationParameters)
    {
        //Fetch installed runtimes and sdks
        var installedRuntimes = GetDotNetListOutput("--list-runtimes");
        var installedSdks = GetDotNetListOutput("--list-sdks");

        //Check requirements for each benchmark
        foreach (var benchmark in validationParameters.Benchmarks)
        {
            // Retrieve version from either CsProjGenerator or CoreRuntime
            var requestedVersion = GetRequestedRuntimeVersion(benchmark.Job);
            
            // Ignore if default host runtime or legacy framework without silent roll forward used
            if (string.IsNullOrEmpty(requestedVersion))
            {
                yield return new ValidationError(
                    isCritical: true,
                    message: $"[Strict Validation] Benchmark '{benchmark.DisplayInfo}' is missing a explicitly configured runtime framework version. Aborting to prevent silent fallback."
                );
                
                continue;
            }

            // Check if we have the required runtime and sdk
            var (searchTerm, isExactSearch) = SemanticVersion.GetSearchTerm(requestedVersion);
            var targetVersion = isExactSearch ? requestedVersion : searchTerm;

            if (HasRuntimeAndSdk(installedRuntimes, installedSdks, targetVersion))
                continue; // We're good
            
            // If we get to here we are missing a runtime or sdk and need to generate and error
            var (majorVersion, _, _, _, _) = SemanticVersion.ParseVersion(requestedVersion);
            var availableRuntimes = ListAvailableRuntimes(installedRuntimes, majorVersion!);
            var availableSdks = ListAvailableSdks(installedSdks, majorVersion!);
           
            var errorCondition = !isExactSearch
                ? $"matching wildcard prefix '{searchTerm}*'"
                : $"exactly '{requestedVersion}'";

            yield return new ValidationError(
                isCritical: true,
                message: $"[Strict Validation] Benchmark '{benchmark.DisplayInfo}' requested runtime framework version {errorCondition}, but it is missing.\n" +
                         $"   -> Installed Runtimes ({majorVersion}.*): [{availableRuntimes}]\n" +
                         $"   -> Installed SDKs ({majorVersion}.*): [{availableSdks}]\n" +
                         $"   -> Aborting run to prevent silent fallback to an unintended version."
            );
        }
    }

    private static string[] GetDotNetListOutput(string argument)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = argument,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        var output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
        var result = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        process?.WaitForExit();
        return result;
    }
    
    private static string? GetRequestedRuntimeVersion(Job job)
    {
        // 1. Explicit strict version string (e.g., "11.0.0-preview.3")
        if (job.Infrastructure.Toolchain?.Generator is CsProjGenerator csProj && 
            !string.IsNullOrWhiteSpace(csProj.RuntimeFrameworkVersion))
        {
            return csProj.RuntimeFrameworkVersion;
        }

        // 2. Only validate .NET Core / .NET 5+ runtimes. 
        // We ignore full .NET Framework (net472) because it doesn't show up in `dotnet --list-runtimes`.
        // and doesent silently roll forward
        if (job.Environment.Runtime is not CoreRuntime coreRuntime)
            return null; 

        // 3. MsBuildMoniker is ALWAYS formatted natively (e.g., "net11.0" or "netcoreapp3.1")
        var moniker = coreRuntime.MsBuildMoniker;
        if (string.IsNullOrEmpty(moniker))
            return null;

        // 4. Strip the text prefixes to isolate the semantic numbers
        var cleanVersion = moniker
            .Replace("netcoreapp", "", StringComparison.OrdinalIgnoreCase)
            .Replace("net", "", StringComparison.OrdinalIgnoreCase);

        // 5. Turn "11.0" into a wildcard search "11.0.*"
        return cleanVersion + ".*"; 
    }

    private static bool HasRuntimeAndSdk(string[] runtimes, string[] sdks, string version)
    {
        // Runtime check: matches exact version (e.g. "11.0.0") or prefix (e.g. "11.0." or "11.0.0-preview.")
        var hasMatchingRuntime = runtimes.Any(line => 
            line.StartsWith($"Microsoft.NETCore.App {version}"));

        if (!hasMatchingRuntime)
            return false;

        // Parse components to handle SDK feature band variations
        var (major, minor, _, tag, _) = SemanticVersion.ParseVersion(version);

        // 1. Tagged releases or tag wildcards (e.g. "preview.3.24172.9" or "preview.")
        // SDKs contain the preview tag in their version string (e.g. 11.0.100-preview.3.24172.9)
        if (!string.IsNullOrEmpty(tag))
        {
            return sdks.Any(line => line.Contains(tag));
        }

        // 2. Untagged releases or non-tag wildcards (e.g. "11.0.0", "11.0.", or "11.")
        // SDKs use feature bands (e.g., Runtime "11.0.0" -> SDK "11.0.100")
        var sdkPrefix = !string.IsNullOrEmpty(minor)
            ? $"{major}.{minor}."
            : $"{major}.";

        return sdks.Any(line => line.StartsWith(sdkPrefix));
    }
    
    private static string ListAvailableRuntimes(string[] installedRuntimes, string majorVersion)
    {
        var availableRuntimes = installedRuntimes
            .Where(line => line.StartsWith($"Microsoft.NETCore.App {majorVersion}."))
            .Select(line => line.Split(' ')[1])
            .ToList();
        
        return availableRuntimes.Any()
            ? string.Join(", ", availableRuntimes)
            : "None";
    }
    
    private static string ListAvailableSdks(string[] installedSdks, string majorVersion)
    {
        var availableSdks = installedSdks
            .Where(line => line.StartsWith($"{majorVersion}."))
            .Select(line => line.Split(' ')[0])
            .ToList();
        
        return  availableSdks.Any()
            ? string.Join(", ", availableSdks)
            : "None";
    }
}