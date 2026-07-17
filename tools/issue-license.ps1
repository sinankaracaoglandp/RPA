<#
.SYNOPSIS
  Test/gelistirme icin satici lisans uretimini tek komuta indirir (RPA.LicenseGenerator sarmalayicisi).

.DESCRIPTION
  Uretim CLI'sini (tools/RPA.LicenseGenerator) cagirir; degistirmez ve YERINE GECMEZ.
  Sadece tekrar eden test dongusundeki surtunmeyi alir:
    - Kurulum talebini Downloads'tan otomatik bulur (en yenisi),
    - Bu repo icin makul varsayilanlari doldurur,
    - Paroalayi ortam degiskeninden okur; yoksa GIZLI sorar (ekrana yazilmaz, gecmise dusmez).

  GUVENLIK: parola hicbir zaman arguman olarak gecirilmez (process listesi/PowerShell gecmisi
  sizintisi). Ortam degiskeni yalnizca bu surecin cocuguna verilir.

  URETIM ICIN DEGILDIR: gercek lisans kesiminde CLI dogrudan, bilincli argumanlarla calistirilir.

.EXAMPLE
  ./tools/issue-license.ps1
  # En yeni kurulum talebini bulur, varsayilanlarla acme.lic uretir.

.EXAMPLE
  ./tools/issue-license.ps1 -MaxAgents 1 -Expires 2026-08-01 -Revision 2
  # Koltuk siniri / sona erme / yeniden yayin senaryolarini denemek icin.

.EXAMPLE
  ./tools/issue-license.ps1 -Expires 2020-01-01 -LicenseId LIC-EXPIRED
  # SURESI DOLMUS lisans: import reddeder (LICENSE_EXPIRED) - red yolunu gormek icin.
#>
[CmdletBinding()]
param(
    # Kurulum talebi JSON'u. Verilmezse Downloads'taki en yeni installation-request-*.json.
    [string]$Request,

    [string]$Output = "$env:USERPROFILE\rpa-vendor-keys\acme.lic",
    [string]$Key = "$env:USERPROFILE\rpa-vendor-keys\vendor-private-key.pem",

    # Parolanin okunacagi ortam degiskeninin ADI (degeri degil).
    [string]$PasswordEnvVar = "RPA_LICENSE_KEY_PASSWORD",

    [string]$LicenseId = "LIC-ACME-001",
    [string]$CustomerId = "ACME",
    [string]$CustomerName = "ACME Sanayi A.S.",
    [string]$Edition = "enterprise",
    [int]$MaxAgents = 3,

    # Yeniden yayinda ARTIRILMALIDIR: import esit/eski revizyonu LICENSE_REVISION_INVALID ile reddeder.
    [int]$Revision = 1,

    [string]$Expires = (Get-Date).AddYears(1).ToString('yyyy-MM-dd'),
    [string]$Features = "agent,sap"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

# --- Kurulum talebi -------------------------------------------------------------------
if (-not $Request) {
    $latest = Get-ChildItem "$env:USERPROFILE\Downloads\installation-request-*.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) {
        throw "Kurulum talebi bulunamadi. Studio > Lisanslama > 'kurulum talebini indir' ile alin, " +
              "ya da -Request <yol> verin."
    }
    $Request = $latest.FullName
    Write-Host "Kurulum talebi (en yeni): $Request" -ForegroundColor DarkGray
}

if (-not (Test-Path $Request)) { throw "Kurulum talebi bulunamadi: $Request" }
if (-not (Test-Path $Key)) {
    throw "Satici ozel anahtari bulunamadi: $Key`n" +
          "Uretmek icin (Git Bash):`n" +
          "  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -aes-256-cbc -out vendor-private-key.pem`n" +
          "  openssl pkey -in vendor-private-key.pem -pubout -out vendor-public-key.pem`n" +
          "Acik anahtari WebAPI'de Licensing:VendorPublicKeyPem olarak yapilandirin."
}

# Talebin kendi icindeki acik anahtarla tutarli oldugunu ONCEDEN soyle: generator zaten
# reddeder, ama hatayi burada anlasilir bicimde vermek dongu suresini kisaltir.
$requestJson = Get-Content $Request -Raw | ConvertFrom-Json
$publicKeyBytes = [Convert]::FromBase64String($requestJson.installationPublicKey)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $computed = ($sha256.ComputeHash($publicKeyBytes) | ForEach-Object { $_.ToString('X2') }) -join ''
} finally { $sha256.Dispose() }

if ($computed -ne $requestJson.installationPublicKeyFingerprint) {
    throw "Kurulum talebi tutarsiz: parmak izi icerdigi acik anahtarla eslesmiyor (talep kurcalanmis olabilir)."
}

# --- Parola ---------------------------------------------------------------------------
if (-not [Environment]::GetEnvironmentVariable($PasswordEnvVar)) {
    $secure = Read-Host "Satici anahtar parolasi ($PasswordEnvVar tanimli degil)" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    # Yalnizca bu surecte; ekrana yazilmaz, PowerShell gecmisine dusmez.
    Set-Item -Path "Env:$PasswordEnvVar" -Value $plain
}

# --- Uretim CLI'si --------------------------------------------------------------------
$outputDirectory = Split-Path $Output -Parent
if ($outputDirectory -and -not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Write-Host "Lisans uretiliyor: $CustomerName / $Edition / $MaxAgents koltuk / revision $Revision / son $Expires" -ForegroundColor Cyan

& dotnet run --project "$repoRoot\tools\RPA.LicenseGenerator" -- generate `
    --request $Request `
    --output $Output `
    --key $Key `
    --key-password-env $PasswordEnvVar `
    --license-id $LicenseId `
    --customer-id $CustomerId `
    --customer-name $CustomerName `
    --edition $Edition `
    --max-agents $MaxAgents `
    --revision $Revision `
    --expires $Expires `
    --features $Features

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Sirada: Studio > Lisanslama > 'ice aktar' ile $Output dosyasini yukleyin." -ForegroundColor Green
