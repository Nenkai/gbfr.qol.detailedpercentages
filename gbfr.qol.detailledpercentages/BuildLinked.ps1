# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/gbfr.qol.detailledpercentages/*" -Force -Recurse
dotnet publish "./gbfr.qol.detailledpercentages.csproj" -c Release -o "$env:RELOADEDIIMODS/gbfr.qol.detailledpercentages" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location