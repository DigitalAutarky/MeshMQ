function Get-Unit-Type {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [System.String]$Unit)

    # TimeSpans
    $duration = @("ns", "us", "ms", "s", "m", "h", "d")
    if ($duration -contains $Unit) {
        return "duration"
    }

    # Memory Units
    $memory = @("B", "kB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB")
    if ($memory -contains $Unit) {
        return "memory"
    }

    # Counts
    $count = "count"
    if($count -eq $Unit) {
        return "count"
    }

    Write-Error "Encountered unknown unit '$Unit'. Please check your display configuration. Units are case sensitive."
    exit 1
}

function Convert-TimeSpanUnit {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)]
        [double]$Value,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$FromUnit,

        [Parameter(Mandatory = $true, Position = 2)]
        [string]$ToUnit
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

function Convert-MemoryUnit {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)]
        [double]$Value,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$FromUnit,

        [Parameter(Mandatory = $true, Position = 2)]
        [string]$ToUnit
    )

    # Conversion scale relative to 1 Byte
    $unitMap = @{
        'b'    = 1.0
        'byte' = 1.0

        # Binary Units (Power-of-2 / PowerShell Native: 1024)
        'kb'   = 1024.0
        'kib'  = 1024.0
        'mb'   = 1048576.0            # 1024^2
        'mib'  = 1048576.0
        'gb'   = 1073741824.0         # 1024^3
        'gib'  = 1073741824.0
        'tb'   = 1099511627776.0      # 1024^4
        'tib'  = 1099511627776.0
        'pb'   = 1125899906842624.0   # 1024^5
        'pib'  = 1125899906842624.0

        # Decimal Units (Power-of-10 Metric: 1000)
        'dkb'  = 1e3
        'dmb'  = 1e6
        'dgb'  = 1e9
        'dtb'  = 1e12
        'dpb'  = 1e15
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

function Convert {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [double]$Value,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$FromUnit,

        [Parameter(Mandatory = $true, Position = 2)]
        [string]$ToUnit
    )

    # Assert that we only convert between units of the same type
    $fromUnitType = Get-Unit-Type -Unit $FromUnit
    $toUnitType = Get-Unit-Type -Unit $ToUnit
    if ($fromUnitType -ne $toUnitType) {
        Write-Error "Can not convert between different unit types: '$fromUnitType' => '$toUnitType'"
        exit 1
    }
    
    # Perform conversion, both units are the same kind
    # so we can use either in the case switch
    switch -CaseSensitive ($fromUnitType) {
        "duration" { return Convert-TimeSpanUnit -Value $Value -FromUnit $FromUnit -ToUnit $ToUnit }
        "memory" { return Convert-MemoryUnit -Value $Value -FromUnit $FromUnit -ToUnit $ToUnit }
        "count" { return $Value }
        default  {
            Write-Error "Conversion failed due to unknow unit type '$fromUnitType'."
            exit 1
        }
    }
}

function Get-Optimal-TimeSpanUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [double[]]$Values,
    
        [Parameter(Mandatory=$true)]
        [System.String]$Unit)

    # pick the smallest unit that allows the smallest value
    # to be rendered with 1 to 3 digits before the decimal sign
    $smallestValue = ($Values | Measure-Object -Minimum).Minimum
    $units = @("ns", "us", "ms", "s", "m", "h", "d") # Important: sorted from smallest to largest unit!
    foreach ($currentUnit in $units) {
        $inCurrentUnit = Convert-TimeSpanUnit -Value $smallestValue -FromUnit $Unit -ToUnit $currentUnit
        if ($inCurrentUnit -lt 1000) {
            return $currentUnit
        }
    }
    
    # all duration units have values greater than 1000
    # so we return the biggest unit
    return $units[-1]
}

function Get-Optimal-MemoryUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [double[]]$Values,

        [Parameter(Mandatory=$true)]
        [System.String]$Unit)

    # pick the smallest unit that allows the smallest value
    # to be rendered with 1 to 3 digits before the decimal sign
    $smallestValue = ($Values | Measure-Object -Minimum).Minimum
    $units = @("B", "kB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB") # Important: sorted from smallest to largest unit!
    foreach ($currentUnit in $units) {
        $inCurrentUnit = Convert-MemoryUnit -Value $smallestValue -FromUnit $Unit -ToUnit $currentUnit
        if ($inCurrentUnit -lt 1000) {
            return $currentUnit
        }
    }

    # all memory units have values greater than 1000
    # so we return the biggest unit
    return $units[-1]
}

function Get-Optimal-DisplayUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [double[]]$Values,

        [Parameter(Mandatory=$true)]
        [System.String]$Unit)
    
    $unitType = Get-Unit-Type -Unit $Unit
    switch -CaseSensitive ($unitType) {
        "duration" { return Get-Optimal-TimeSpanUnit -Values $Values -Unit $Unit }
        "memory" { return Get-Optimal-MemoryUnit -Values $Values -Unit $Unit }
        "count" { return "count" }
        default  { 
            Write-Error "Unknow unit type '$unitType' encountered."
            exit 1
        }
    }
}