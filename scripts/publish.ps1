# Web'i derler, testleri çalıştırır, kasa PC için self-contained yayın üretir.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Push-Location (Join-Path $root 'web')
try {
    npm ci
    if ($LASTEXITCODE) { throw 'npm ci başarısız' }
    npm test
    if ($LASTEXITCODE) { throw 'web testleri başarısız' }
    npm run build
    if ($LASTEXITCODE) { throw 'web derlemesi başarısız' }
} finally { Pop-Location }

dotnet test (Join-Path $root 'server\Firin.Tests')
if ($LASTEXITCODE) { throw 'sunucu testleri başarısız' }

$out = Join-Path $root 'publish\FirinAdisyon'
# Eski çıktı (örn. exe elle çalıştırılınca oluşan data\) yayına karışmasın.
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
if (Test-Path $out) { throw "Yayın klasörü silinemedi; açık Firin.Api.exe varsa kapatıp tekrar deneyin." }
dotnet publish (Join-Path $root 'server\Firin.Api') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $out
if ($LASTEXITCODE) { throw 'yayın başarısız' }

if (-not (Test-Path (Join-Path $out 'wwwroot\index.html'))) { throw 'wwwroot yayına girmedi' }
Write-Host "Yayın hazır: $out"
