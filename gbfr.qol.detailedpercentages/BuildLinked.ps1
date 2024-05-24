# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/gbfr.qol.detailedpercentages/*" -Force -Recurse
dotnet publish "./gbfr.qol.detailedpercentages.csproj" -c Release -o "$env:RELOADEDIIMODS/gbfr.qol.detailedpercentages" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location