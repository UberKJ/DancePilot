param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [string]$Subject = "CN=DancePilot Local Dev Code Signing"
)

function Test-CodeSigningCertificate {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    foreach ($usage in $Certificate.EnhancedKeyUsageList) {
        if ($usage.ObjectId -eq "1.3.6.1.5.5.7.3.3" -or $usage.FriendlyName -eq "Code Signing") {
            return $true
        }
    }

    return $false
}

Import-Module Microsoft.PowerShell.Security -ErrorAction SilentlyContinue
if (-not (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) {
    New-PSDrive -Name Cert -PSProvider Certificate -Root \ | Out-Null
}

if (-not (Test-Path -LiteralPath $FilePath)) {
    Write-Host "DancePilot dev signing skipped; file was not found: $FilePath"
    exit 0
}

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey -and (Test-CodeSigningCertificate $_) -and $_.NotAfter -gt (Get-Date).AddDays(1) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "DancePilot dev signing skipped; run scripts\New-DancePilotDevCertificate.ps1 once to enable local Debug signing."
    exit 0
}

$signature = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $cert -HashAlgorithm SHA256
if ($signature.Status -ne "Valid") {
    Write-Warning "DancePilot dev signing did not produce a valid signature: $($signature.StatusMessage)"
    exit 1
}

Write-Host "DancePilot dev signed $FilePath with $($cert.Thumbprint)."
