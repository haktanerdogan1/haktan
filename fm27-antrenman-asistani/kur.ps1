# Antrenman Asistanı'nı derler ve Football Manager 27'nin BepInEx eklenti klasörüne kopyalar.
# Kullanım (PowerShell, FM kapalıyken):  .\kur.ps1
# Oyun farklı bir klasördeyse:            .\kur.ps1 -OyunKlasoru "D:\SteamLibrary\steamapps\common\Football Manager 27"
# Oyun içi panel derlenmezse:              .\kur.ps1 -YalnizcaTarayiciPaneli
param(
    [string]$OyunKlasoru = "C:\Program Files (x86)\Steam\steamapps\common\Football Manager 27",
    [switch]$YalnizcaTarayiciPaneli
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path "$OyunKlasoru\BepInEx\interop\Il2Cppmscorlib.dll")) {
    Write-Error "BepInEx bulunamadı: $OyunKlasoru. Önce MCPFM kurulumundaki BepInEx adımlarını yapın ve oyunu bir kez açın."
}

if (Get-Process -Name "fm" -ErrorAction SilentlyContinue) {
    Write-Error "Football Manager açık. Eklenti dosyaları oyun açıkken değiştirilemez; oyunu kapatıp tekrar deneyin."
}

$panel = if ($YalnizcaTarayiciPaneli) { "false" } else { "true" }
dotnet build "$PSScriptRoot\src\Asistan.Plugin\Asistan.Plugin.csproj" -c Release -p:FM27Dir="$OyunKlasoru" -p:OyunIciPanel=$panel

$hedef = "$OyunKlasoru\BepInEx\plugins\AntrenmanAsistani"
if (Test-Path "$hedef\AntrenmanAsistani.dll") {
    Write-Host "Kuruldu: $hedef" -ForegroundColor Green
    Write-Host "Oyunu açın; MCPFM API'si (5120) çalışırken F8 paneli açar. Tarayıcı paneli: http://localhost:5130/"
} else {
    Write-Error "Derleme bitti ama dosyalar $hedef klasörüne kopyalanamadı."
}
