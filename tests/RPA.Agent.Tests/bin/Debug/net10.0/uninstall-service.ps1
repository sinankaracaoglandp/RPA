<#
.SYNOPSIS
  RPA Agent Windows Service'ini durdurur ve kaldırır.
.EXAMPLE
  .\uninstall-service.ps1
#>
param(
    [string]$ServiceName = "RPA.Agent"
)

$ErrorActionPreference = "Stop"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Servis '$ServiceName' bulunamadı — yapılacak bir şey yok."
    return
}

Write-Host "Servis durduruluyor: $ServiceName"
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
sc.exe delete $ServiceName | Out-Null
Write-Host "Servis '$ServiceName' kaldırıldı." -ForegroundColor Green
