Import-Module "$PSScriptRoot/../Conversion/Convert-BenchmarkTimeSpan.psm1" -Force
Import-Module "$PSScriptRoot/../Conversion/Convert-BenchmarkMemory.psm1" -Force

function Get-OptimalTimeSpanUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [double[]]$Values,
        [Parameter(Mandatory=$true)] [System.String]$Unit)

    # pick the smallest unit that allows the smallest value
    # to be rendered with 1 to 3 digits before the decimal sign
    $smallestValue = ($Values | Measure-Object -Minimum).Minimum
    $units = @("ns", "us", "ms", "s", "m", "h", "d") # Important: sorted from smallest to largest unit!
    foreach ($currentUnit in $units) {
        $inCurrentUnit = Convert-BenchmarkTimeSpan -Value $smallestValue -FromUnit $Unit -ToUnit $currentUnit
        if ($inCurrentUnit -lt 1000) {
            return $currentUnit
        }
    }
    
    # all duration units have values greater than 1000
    # so we return the biggest unit
    return $units[-1]
}

function Get-OptimalMemoryUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [double[]]$Values,
        [Parameter(Mandatory=$true)] [System.String]$Unit)

    # pick the smallest unit that allows the smallest value
    # to be rendered with 1 to 3 digits before the decimal sign
    $smallestValue = ($Values | Measure-Object -Minimum).Minimum
    $units = @("B", "kB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB") # Important: sorted from smallest to largest unit!
    foreach ($currentUnit in $units) {
        $inCurrentUnit = Convert-BenchmarkMemory -Value $smallestValue -FromUnit $Unit -ToUnit $currentUnit
        if ($inCurrentUnit -lt 1000) {
            return $currentUnit
        }
    }

    # all memory units have values greater than 1000
    # so we return the biggest unit
    return $units[-1]
}

function Get-BenchmarkDisplayUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [double[]]$Values,
        [Parameter(Mandatory=$true)] [System.String]$Unit)
    
    $unitType = Get-BenchmarkValueType -Unit $Unit
    switch -CaseSensitive ($unitType) {
        "duration" { return Get-OptimalTimeSpanUnit -Values $Values -Unit $Unit }
        "memory" { return Get-OptimalMemoryUnit -Values $Values -Unit $Unit }
        "count" { return "count" }
        default  { 
            Write-Error "Unknow unit type '$unitType' encountered."
            exit 1
        }
    }
}