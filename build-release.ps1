# Stop running instances to release file locks
Get-Process -Name "AiaDinamic" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Build single-file Self-Contained Release executable
Write-Host "Publishing Aia Dinamic (Self-Contained Release win-x64)..." -ForegroundColor Cyan
& "$env:LocalAppData\dotnet\dotnet.exe" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[SUCCESS] AiaDinamic.exe is ready in the 'publish' folder!" -ForegroundColor Green

    # Build Commercial Windows Installer via Inno Setup
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    if (Test-Path $iscc) {
        Write-Host "`nBuilding Commercial Windows Installer (Inno Setup)..." -ForegroundColor Cyan
        & $iscc installer.iss
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n[SUCCESS] AiaDinamic-Setup-v1.0.0.exe is ready in 'publish\installer'!" -ForegroundColor Green
        }
    }
} else {
    Write-Host "`n[ERROR] Build failed." -ForegroundColor Red
}
