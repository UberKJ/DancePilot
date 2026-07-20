param(
    [string]$Subject = "CN=DancePilot Local Dev Code Signing",
    [switch]$Force
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

$existing = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey -and (Test-CodeSigningCertificate $_) -and $_.NotAfter -gt (Get-Date).AddMonths(1) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($existing -and -not $Force) {
    $cert = $existing
} else {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5)
}

$certPath = Join-Path $env:TEMP "DancePilotLocalDevCodeSigning.cer"
[System.IO.File]::WriteAllBytes(
    $certPath,
    $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null

Write-Host "DancePilot dev signing certificate is ready."
Write-Host "Subject: $($cert.Subject)"
Write-Host "Thumbprint: $($cert.Thumbprint)"
