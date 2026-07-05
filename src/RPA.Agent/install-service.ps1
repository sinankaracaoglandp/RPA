<#
.SYNOPSIS
  RPA Agent'ı Windows Service olarak kurar (Spec Bölüm 9 — Unattended robot).
.DESCRIPTION
  Yayınlanmış RPA.Agent.exe'yi "RPA.Agent" adıyla bir Windows Service olarak kaydeder ve başlatır.
  Yükseltilmiş (Administrator) PowerShell gerektirir.
.EXAMPLE
  .\install-service.ps1 -BinPath "C:\RPA\Agent\RPA.Agent.exe"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BinPath,

    [string]$ServiceName = "RPA.Agent",
    [string]$DisplayName = "RPA Robot Agent",
    [string]$Description = "RPA platform robot ajanı: kuyruk yoklar, workflow çalıştırır, heartbeat gönderir.",
    [ValidateSet("Automatic", "Manual", "Disabled")]
    [string]$StartupType = "Automatic"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BinPath)) {
    throw "Yürütülebilir bulunamadı: $BinPath. Önce 'dotnet publish' çalıştırın."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Servis '$ServiceName' zaten var — durdurulup kaldırılıyor."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Servis kuruluyor: $ServiceName -> $BinPath"
New-Service -Name $ServiceName `
    -BinaryPathName "`"$BinPath`"" `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType $StartupType | Out-Null

# Hata durumunda otomatik yeniden başlatma (5 sn sonra, 3 deneme).
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

Start-Service -Name $ServiceName
Write-Host "Servis '$ServiceName' kuruldu ve başlatıldı." -ForegroundColor Green
