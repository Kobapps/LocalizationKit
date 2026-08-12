# Builds the source generator and drops LocalizationKit.SourceGenerator.dll into
# ../Runtime/Plugins/ so Unity picks it up alongside the Runtime asmdef.
#
# The generated DLL must carry the "RoslynAnalyzer" asset label or Unity treats it as a
# runtime assembly and the generator never runs — with no error to say so. The committed
# .meta file already declares the label; if you delete it, Unity regenerates it without
# the label and you must re-add it by selecting the DLL and setting the label by hand.

$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot

dotnet build LocalizationKit.SourceGenerator.csproj -c Release

Write-Host "Source generator built. Refresh Unity (Ctrl+R) to pick up the new analyzer DLL." -ForegroundColor Green
