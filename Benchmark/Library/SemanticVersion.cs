using System.Text.RegularExpressions;

namespace Benchmark;

public partial class SemanticVersion
{
    private const char Wildcard = '*';

    [GeneratedRegex(@"^(?<major>[\d*]+)(?:\.(?<minor>[\d*]+))?\.?(?:\.(?<patch>[\d*]+))?(?:-(?<tag>[a-zA-Z0-9.*-]+))?(?:\+(?<build>[a-zA-Z0-9.*-]+))?$")]
    private static partial Regex SemanticVersionRegex { get; }

    public static (string? major, string? minor, string? patch, string? tag, string? build)
        ParseVersion(string versionString)
    {
        var match = SemanticVersionRegex.Match(versionString);
        if(!match.Success)
            throw new ArgumentException($"Invalid version string: {versionString}");

        return (
            match.Groups["major"].Success ? match.Groups["major"].Value : null,
            match.Groups["minor"].Success ? match.Groups["minor"].Value : null,
            match.Groups["patch"].Success ? match.Groups["patch"].Value : null,
            match.Groups["tag"].Success   ? match.Groups["tag"].Value : null,
            match.Groups["build"].Success ? match.Groups["build"].Value : null);
    }

    public static (string searchTerm, bool isExactSearch) GetSearchTerm(string versionString)
    {
        var isExactSearch = IsExactSearch(versionString);
        var (major, minor, patch, tag, build) = ParseVersion(versionString);
        var searchTerm = GetSearchTerm(major!, minor, patch, tag, build);
        return (searchTerm, isExactSearch);
    }

    private static string GetSearchTerm(string major, string? minor, string? patch, string? tag, string? build)
    {
        var searchTerm = major;
        
        if (string.IsNullOrWhiteSpace(minor))
        {
            searchTerm = AddTagAndBuildIfPresent(searchTerm, tag, build);
            searchTerm = StripWildcardIfPresent(searchTerm);
            return searchTerm;
        }
        
        searchTerm += $".{minor}";
        
        if(string.IsNullOrWhiteSpace(patch))
        {
            searchTerm = AddTagAndBuildIfPresent(searchTerm, tag, build);
            searchTerm = StripWildcardIfPresent(searchTerm);
            return searchTerm;
        }
        
        searchTerm += $".{patch}";
        
        searchTerm = AddTagAndBuildIfPresent(searchTerm, tag, build);
        searchTerm = StripWildcardIfPresent(searchTerm);
        return searchTerm;
    }

    private static string AddTagAndBuildIfPresent(string searchTerm, string? tag, string? build)
    {
        if(!string.IsNullOrWhiteSpace(tag))
            searchTerm += $"-{tag}";
        
        if(!string.IsNullOrWhiteSpace(build))
            searchTerm += $"+{build}";
        
        return searchTerm;
    }

    private static string StripWildcardIfPresent(string value)
    {
        var position = value.IndexOf(Wildcard);
        return position >= 0
            ? value.Substring(0, position)
            : value;
    }

    private static bool IsExactSearch(string versionString)
        => !versionString.Contains(Wildcard);
}