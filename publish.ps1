param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration
)

$BuildRoot = $PSScriptRoot

$RIDs = "win-x64", "linux-x64", "osx-x64"

foreach ($RID in $RIDs) {
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/engine --runtime $RID --no-self-contained $BuildRoot\DataGateway.Service\Azure.DataGateway.Service.csproj
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/cli --runtime $RID --no-self-contained $BuildRoot\Hawaii-Cli\src\Hawaii.Cli.csproj
}
