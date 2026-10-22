#Requires -RunAsAdministrator
# Yayını C:\FirinAdisyon'a kopyalar, servisi kurar ya da günceller. data klasörüne dokunmaz.
# Servis LocalSystem hesabıyla çalışır (New-Service varsayılanı).
param([string]$Target = 'C:\FirinAdisyon')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'publish\FirinAdisyon'
if (-not (Test-Path (Join-Path $source 'Firin.Api.exe'))) { throw 'Önce scripts\publish.ps1 çalıştırın.' }

$svc = Get-Service FirinAdisyon -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    Stop-Service FirinAdisyon
    $svc.WaitForStatus('Stopped', '00:00:30')
}
Get-Process Firin.Api -ErrorAction SilentlyContinue | Wait-Process -Timeout 30

# Güncellemeden önce veritabanının kopyasını al (servis durdu, dosya tutarlı).
# Ad, uygulamanın yedek adıyla aynıdır; Ayarlar'daki yedek listesinde görünür ve 30 gün sonra silinir.
$db = Join-Path $Target 'data\firin.db'
if (Test-Path $db) {
    $backups = Join-Path $Target 'data\backups'
    New-Item -ItemType Directory -Force $backups | Out-Null
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
    $copy = Join-Path $backups "firin-$stamp.db"
    Copy-Item $db $copy
    # Servis düzgün kapanmadıysa -wal içinde işlenmemiş kayıtlar olabilir; yanına aynı adla koy.
    if (Test-Path "$db-wal") { Copy-Item "$db-wal" "$copy-wal" }
    Write-Host "Güncelleme öncesi yedek: $copy"
}

New-Item -ItemType Directory -Force $Target | Out-Null
# /MIR, hedefte yayında olmayan her şeyi siler (data\ hariç: veritabanı, fotoğraflar, yedekler).
# Bu yüzden C:\FirinAdisyon içine kendi dosyalarınızı (örn. appsettings.Production.json) koymayın.
# /R:2 /W:2: kilitli dosya yüzünden betik sonsuza dek beklemesin.
robocopy $source $Target /MIR /XD (Join-Path $Target 'data') /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Dosyalar kopyalanamadı (robocopy kodu $LASTEXITCODE)" }

if (-not $svc) {
    New-Service -Name FirinAdisyon -BinaryPathName "`"$Target\Firin.Api.exe`"" `
        -DisplayName 'Fırın Adisyon' -StartupType Automatic `
        -Description 'Fırın masa adisyon sunucusu (port 5080)' | Out-Null
}
# Çökerse 5 sn sonra yeniden başlat. Güncellemelerde de yeniden uygulanır.
sc.exe failure FirinAdisyon reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
# Hata koduyla çıkışta (örn. başlangıçta migration hatası) da yeniden başlatma çalışsın.
sc.exe failureflag FirinAdisyon 1 | Out-Null

if (-not (Get-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -Direction Inbound -Protocol TCP -LocalPort 5080 `
        -Action Allow -Profile Private, Domain | Out-Null
}

$public = Get-NetConnectionProfile | Where-Object NetworkCategory -eq 'Public'
if ($public) {
    Write-Warning ("'{0}' ağı 'Ortak' olarak ayarlı; tabletler bağlanamaz. Ayarlar > Ağ ve İnternet > Wi-Fi/Ethernet > Ağ profili: Özel yapın." -f ($public.Name -join ', '))
}

# Kasa bilgisayarı fişe takılıyken uykuya geçmesin; uyursa tabletler bağlanamaz.
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0

Start-Service FirinAdisyon
Write-Host "Kuruldu ve başlatıldı. Bu bilgisayarda: http://localhost:5080"
