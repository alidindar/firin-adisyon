#Requires -RunAsAdministrator
# Yayını C:\FirinAdisyon'a kopyalar, servisi kurar ya da günceller. data klasörüne dokunmaz.
# Servis LocalSystem hesabıyla çalışır (New-Service varsayılanı).
param([string]$Target = 'C:\FirinAdisyon')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'publish\FirinAdisyon'
if (-not (Test-Path (Join-Path $source 'Firin.Api.exe'))) { throw 'Önce scripts\publish.ps1 çalıştırın.' }

# Servise dokunmadan ÖNCE kontrol edilir: reddedilen güncelleme sunucuyu durdurmasın.
# Yayın klasöründe data olmamalı (exe elle çalıştırılınca oluşur); kopyalanırsa gerçek verinin üstüne yazar.
if (Test-Path (Join-Path $source 'data')) {
    throw "Yayın klasöründe 'data' klasörü var ($source\data). Silin ya da scripts\publish.ps1'i yeniden çalıştırın; sonra tekrar deneyin."
}

$svcExisted = [bool](Get-Service FirinAdisyon -ErrorAction SilentlyContinue)
try {
    $svc = Get-Service FirinAdisyon -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Stop-Service FirinAdisyon
        try { $svc.WaitForStatus('Stopped', '00:00:30') }
        catch { throw "Servis 30 sn içinde kapanmadı; Görev Yöneticisi'nden kapatıp tekrar deneyin." }
    }
    try { Get-Process Firin.Api -ErrorAction SilentlyContinue | Wait-Process -Timeout 30 -ErrorAction Stop }
    catch { throw "Firin.Api.exe 30 sn içinde kapanmadı; Görev Yöneticisi'nden kapatıp tekrar deneyin." }

    # Güncellemeden önce veritabanının kopyasını al (servis durdu, dosya tutarlı).
    # Ad, uygulamanın yedek adıyla aynıdır; yalnızca Ayarlar'daki yedek klasörü varsayılan (data\backups) ise listede görünür ve 30 gün sonra silinir.
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
        if (Test-Path "$copy-wal") { Write-Host "  (işlenmemiş kayıtlar için: $copy-wal; geri dönerken ikisini birlikte kopyalayın)" }
        Write-Host "  (Ayarlar'daki yedek klasörü başkaysa bu kopya listede görünmez ve otomatik silinmez.)"
    }

    New-Item -ItemType Directory -Force $Target | Out-Null
    # /MIR, hedefte yayında olmayan her şeyi siler (data\ hariç: veritabanı, fotoğraflar, yedekler).
    # Bu yüzden C:\FirinAdisyon içine kendi dosyalarınızı (örn. appsettings.Production.json) koymayın.
    # /R:2 /W:2: kilitli dosya yüzünden betik sonsuza dek beklemesin.
    robocopy $source $Target /MIR /XD (Join-Path $source 'data') (Join-Path $Target 'data') /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Dosyalar kopyalanamadı (robocopy kodu $LASTEXITCODE)" }

    if (-not $svc) {
        New-Service -Name FirinAdisyon -BinaryPathName "`"$Target\Firin.Api.exe`"" `
            -DisplayName 'Fırın Adisyon' -StartupType Automatic `
            -Description 'Fırın masa adisyon sunucusu (port 5080)' | Out-Null
    }
    # Çökerse 5 sn sonra yeniden başlat. Güncellemelerde de yeniden uygulanır.
    sc.exe failure FirinAdisyon reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    if ($LASTEXITCODE) { throw "sc.exe failure başarısız (kod $LASTEXITCODE)" }
    # Hata koduyla çıkışta (örn. başlangıçta migration hatası) da yeniden başlatma çalışsın.
    sc.exe failureflag FirinAdisyon 1 | Out-Null
    if ($LASTEXITCODE) { throw "sc.exe failureflag başarısız (kod $LASTEXITCODE)" }

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
} catch {
    $err = $_
    # Servis durdurulduysa tabletler kopar; hata olsa da servisi geri açmayı dene.
    if ($svcExisted) {
        try {
            Start-Service FirinAdisyon
            Write-Warning "Kurulum yarıda kaldı. Servis yeniden başlatıldı; eski sürüm çalışıyor olmalı (dosyalar yarım kopyalanmış olabilir, sorunu giderip betiği tekrar çalıştırın)."
        } catch {
            Write-Warning "Kurulum yarıda kaldı ve servis YENİDEN BAŞLATILAMADI. Elle başlatın: Start-Service FirinAdisyon"
        }
    }
    throw $err
}


Start-Service FirinAdisyon
Write-Host "Kuruldu ve başlatıldı. Bu bilgisayarda: http://localhost:5080"
