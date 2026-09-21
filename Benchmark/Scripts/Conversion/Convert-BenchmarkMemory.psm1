function Convert-BenchmarkMemory {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)] [double]$Value,
        [Parameter(Mandatory = $true, Position = 1)] [string]$FromUnit,
        [Parameter(Mandatory = $true, Position = 2)] [string]$ToUnit
    )

    # Conversion scale relative to 1 Byte
    $unitMap = @{
        'b'    = 1.0
        'kb'   = 1e3
        'mb'   = 1e6
        'gb'   = 1e9
        'tb'   = 1e12
        'pb'   = 1e15
    }

    $from = $FromUnit.ToLower()
    $to   = $ToUnit.ToLower()

    if (-not $unitMap.ContainsKey($from) -or -not $unitMap.ContainsKey($to)) {
        Write-Error "Unsupported memory unit suffix provided. Supported units: $(($unitMap.Keys | Sort-Object -Unique) -join ', ')"
        exit 1
    }

    # Convert source unit to Bytes, then to target unit
    $inBytes = $Value * $unitMap[$from]
    return $inBytes / $unitMap[$to]
}