Import-Module "$PSScriptRoot/Get-BenchmarkValueType.psm1" -Force
Import-Module "$PSScriptRoot/Convert-BenchmarkTimeSpan.psm1" -Force
Import-Module "$PSScriptRoot/Convert-BenchmarkMemory.psm1" -Force

function Convert-BenchmarkValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)] [double]$Value,
        [Parameter(Mandatory = $true, Position = 1)] [string]$FromUnit,
        [Parameter(Mandatory = $true, Position = 2)] [string]$ToUnit
    )

    # Assert that we only convert between units of the same type
    $fromUnitType = Get-BenchmarkValueType -Unit $FromUnit
    $toUnitType = Get-BenchmarkValueType -Unit $ToUnit
    if ($fromUnitType -ne $toUnitType) {
        Write-Error "Can not convert between different unit types: '$fromUnitType' => '$toUnitType'"
        exit 1
    }
    
    # Perform conversion, both units are the same kind
    # so we can use either in the case switch
    switch -CaseSensitive ($fromUnitType) {
        "duration" { return Convert-BenchmarkTimeSpan -Value $Value -FromUnit $FromUnit -ToUnit $ToUnit }
        "memory" { return Convert-BenchmarkMemory -Value $Value -FromUnit $FromUnit -ToUnit $ToUnit }
        "count" { return $Value }
        default  {
            Write-Error "Conversion failed due to unknow unit type '$fromUnitType'."
            exit 1
        }
    }
}