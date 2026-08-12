Import-Module "$PSScriptRoot/Conversion.psm1" -Force

function Format-Value {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)]
        [double]$Value,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Unit
    )
    
    $unitType = Get-Unit-Type -Unit $Unit
    switch -CaseSensitive ($unitType) {
        "duration"  { return "$("{0:N2}" -f $Value) $Unit" }
        "memory"    { return "$("{0:N2}" -f $Value) $Unit" }
        "count"     { return "$("{0:N0}" -f $Value)" }
        default  {
            Write-Error "Formatting failed due to unknow unit type '$unitType'."
            exit 1
        }
    }
}