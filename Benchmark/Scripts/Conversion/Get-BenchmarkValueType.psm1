function Get-BenchmarkValueType {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [System.String]$Unit)

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