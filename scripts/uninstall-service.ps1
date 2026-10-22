#Requires -RunAsAdministrator
# Servisi ve güvenlik duvarı kuralını kaldırır. C:\FirinAdisyon\data (veriler) silinmez.
$ErrorActionPreference = 'Stop'
$svc = Get-Service FirinAdisyon -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service FirinAdisyon
        $svc.WaitForStatus('Stopped', '00:00:30')
    }
    Get-Process Firin.Api -ErrorAction SilentlyContinue | Wait-Process -Timeout 30
    sc.exe delete FirinAdisyon | Out-Null
}
Get-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Servis kaldırıldı. Veriler C:\FirinAdisyon\data altında duruyor.'
