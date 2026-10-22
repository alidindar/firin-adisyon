# Kasa PC'ye USB ile taşınacak kurulum paketini hazırlar: paket\FirinAdisyon-Kurulum
# Önce scripts\publish.ps1 çalıştırılmış olmalı. Paket: Kur.cmd, Kaldir.cmd, KURULUM.md, scripts\, publish\FirinAdisyon\
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'publish\FirinAdisyon'
if (-not (Test-Path (Join-Path $src 'Firin.Api.exe'))) { throw 'Önce scripts\publish.ps1 çalıştırın.' }
if (Test-Path (Join-Path $src 'data')) { throw "Yayın klasöründe 'data' klasörü var ($src\data). scripts\publish.ps1'i yeniden çalıştırın." }

$out = Join-Path $root 'paket\FirinAdisyon-Kurulum'
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $out 'scripts'), (Join-Path $out 'publish') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'install-service.ps1'), (Join-Path $PSScriptRoot 'uninstall-service.ps1') (Join-Path $out 'scripts')
Copy-Item (Join-Path $PSScriptRoot 'Kur.cmd'), (Join-Path $PSScriptRoot 'Kaldir.cmd') $out
Copy-Item (Join-Path $root 'docs\KURULUM.md') $out
Copy-Item $src (Join-Path $out 'publish') -Recurse
Write-Host "Paket hazır: $out"
Write-Host 'Bu klasörü olduğu gibi USB belleğe kopyalayın.'
