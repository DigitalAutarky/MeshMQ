function Convert-BenchmarkTimeSpan {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)] [double]$Value,
        [Parameter(Mandatory = $true, Position = 1)] [string]$FromUnit,
        [Parameter(Mandatory = $true, Position = 2)] [string]$ToUnit
    )

    # Conversion scale relative to 1 second
    $unitMap = @{
        'ns'   = 1e-9
        'us'   = 1e-6
        'µs'   = 1e-6
        'ms'   = 1e-3
        's'    = 1.0
        'm'    = 60.0
        'h'    = 3600.0
        'd'    = 86400.0
    }

    $from = $FromUnit.ToLower()
    $to   = $ToUnit.ToLower()

    if (-not $unitMap.ContainsKey($from) -or -not $unitMap.ContainsKey($to)) {
        Write-Error "Unsupported unit suffix provided. Supported units: $(($unitMap.Keys | Sort-Object -Unique) -join ', ')"
        exit 1
    }

    # Convert source unit to base unit (seconds), then to target unit
    $inSeconds = $Value * $unitMap[$from]
    return $inSeconds / $unitMap[$to]
}