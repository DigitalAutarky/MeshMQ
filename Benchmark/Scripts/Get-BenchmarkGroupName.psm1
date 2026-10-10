function Get-BenchmarkGroupName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Group
    )

    $items = $Group.Group

    # 1. Extract unique properties across the group
    $types     = @($items | Select-Object -ExpandProperty Type -Unique | Where-Object { $_ })
    $methods   = @($items | Select-Object -ExpandProperty MethodTitle -Unique | Where-Object { $_ })
    $baselines = $items | Where-Object { $_.IsBaseline -eq $true -or $_.Baseline -eq $true }

    # Extract unique parameters to differentiate identical types/methods
    $parameters = @($items | Select-Object -ExpandProperty Parameters -Unique | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne "None" })
    $paramSuffix = if ($parameters.Count -eq 1) { " [$([uri]::UnescapeDataString($parameters[0].Replace('+', ' ')))]" } else { "" }

    # Flatten categories (supports string arrays or nulls)
    $categories = $items | ForEach-Object { $_.Categories } | Where-Object { $_ } | Select-Object -Unique

    # Helper scriptblock to append the parameter suffix before returning
    $FormatName = { param([string]$Name) return -join($Name, $paramSuffix) }

    # 2. Category Grouping
    if ($categories.Count -eq 1) {
        return &$FormatName "Category: $($categories[0])"
    }
    if ($categories.Count -gt 1 -and $types.Count -gt 1) {
        return &$FormatName "Categories: $(($categories -join ', '))"
    }

    # 3. Single C# Class (Type) Grouping
    if ($types.Count -eq 1) {
        $typeName = $types[0]

        # Single Method with multiple parameter combinations
        if ($methods.Count -eq 1) {
            return &$FormatName "$typeName › $($methods[0])"
        }

        # Class contains a designated Baseline Method
        if ($baselines.Count -gt 0) {
            $baseName = if ($baselines[0].MethodTitle) { $baselines[0].MethodTitle } else { "Baseline" }
            return &$FormatName "$typeName (Baseline: $baseName)"
        }

        return &$FormatName $typeName
    }

    # 4. Cross-Class Shared Method Grouping
    if ($methods.Count -eq 1) {
        return &$FormatName "Method: $($methods[0]) ($($types.Count) Classes)"
    }

    # 5. Cross-Class Baseline Comparison (comparing multiple types against one baseline)
    if ($types.Count -gt 1 -and $baselines.Count -gt 0) {
        $baseType   = $baselines[0].Type
        $baseMethod = $baselines[0].MethodTitle
        return &$FormatName "Comparison vs $baseType.$baseMethod ($($types.Count) Classes)"
    }

    # 6. Catch-All Mixed Group
    if ($types.Count -gt 1) {
        if ($types.Count -le 2) {
            return &$FormatName "Mixed: $(($types -join ' & '))"
        }
        $sample = ($types | Select-Object -First 2) -join ', '
        $remaining = $types.Count - 2
        return &$FormatName "Mixed Group ($sample +$remaining more)"
    }

    # Fallback to PowerShell's default Group-Object key
    $fallback = if ($Group.Name) { $Group.Name } else { "Benchmark Group" }
    return &$FormatName $fallback
}