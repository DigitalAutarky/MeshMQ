function Get-BenchmarkGroupName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Group
    )

    $items = $Group.Group

    # 1. Extract unique properties across the group
    $types     = $items | Select-Object -ExpandProperty Type -Unique | Where-Object { $_ }
    $methods   = $items | Select-Object -ExpandProperty MethodTitle -Unique | Where-Object { $_ }
    $baselines = $items | Where-Object { $_.IsBaseline -eq $true -or $_.Baseline -eq $true }
    
    # Flatten categories (supports string arrays or nulls)
    $categories = $items | ForEach-Object { $_.Categories } | Where-Object { $_ } | Select-Object -Unique

    # 2. Category Grouping (e.g., [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)])
    if ($categories.Count -eq 1) {
        return "Category: $($categories[0])"
    }
    if ($categories.Count -gt 1 -and $types.Count -gt 1) {
        return "Categories: $(($categories -join ', '))"
    }

    # 3. Single C# Class (Type) Grouping
    if ($types.Count -eq 1) {
        $typeName = $types[0]

        # Single Method with multiple parameter combinations
        if ($methods.Count -eq 1) {
            return "$typeName › $($methods[0])"
        }

        # Class contains a designated Baseline Method
        if ($baselines.Count -gt 0) {
            $baseName = if ($baselines[0].MethodTitle) { $baselines[0].MethodTitle } else { "Baseline" }
            return "$typeName (Baseline: $baseName)"
        }

        return $typeName
    }

    # 4. Cross-Class Shared Method Grouping
    if ($methods.Count -eq 1) {
        return "Method: $($methods[0]) ($($types.Count) Classes)"
    }

    # 5. Cross-Class Baseline Comparison (comparing multiple types against one baseline)
    if ($types.Count -gt 1 -and $baselines.Count -gt 0) {
        $baseType   = $baselines[0].Type
        $baseMethod = $baselines[0].MethodTitle
        return "Comparison vs $baseType.$baseMethod ($($types.Count) Classes)"
    }

    # 6. Catch-All Mixed Group
    if ($types.Count -gt 1) {
        if ($types.Count -le 2) {
            return "Mixed: $(($types -join ' & '))"
        }
        $sample = ($types | Select-Object -First 2) -join ', '
        $remaining = $types.Count - 2
        return "Mixed Group ($sample +$remaining more)"
    }

    # Fallback to PowerShell's default Group-Object key
    return if ($Group.Name) { $Group.Name } else { "Benchmark Group" }
}