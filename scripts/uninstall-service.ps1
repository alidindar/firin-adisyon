#Requires -RunAsAdministrator
# Servisi ve güvenlik duvarı kuralını kaldırır. C:\FirinAdisyon\data (veriler) silinmez.
$ErrorActionPreference = 'Stop'
$svc = Get-Service FirinAdisyon -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service FirinAdisyon
        try { $svc.WaitForStatus('Stopped', '00:00:30') }
        catch { throw "Servis 30 sn içinde kapanmadı; Görev Yöneticisi'nden kapatıp tekrar deneyin." }
    }
    try { Get-Process Firin.Api -ErrorAction SilentlyContinue | Wait-Process -Timeout 30 -ErrorAction Stop }
    catch { throw "Firin.Api.exe 30 sn içinde kapanmadı; Görev Yöneticisi'nden kapatıp tekrar deneyin." }
    sc.exe delete FirinAdisyon | Out-Null
    if ($LASTEXITCODE) { throw "sc.exe delete başarısız (kod $LASTEXITCODE)" }
}
Get-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Servis kaldırıldı. Veriler C:\FirinAdisyon\data altında duruyor.'
