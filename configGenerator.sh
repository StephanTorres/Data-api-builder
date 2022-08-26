#!/bin/bash
commandFiles=("MsSqlCommands.txt" "MySqlCommands.txt" "PostgreSqlCommands.txt" "CosmosCommands.txt")
absolutePath=$(pwd -P);
#Fetching the path to dab dll
pathToDLL=$(find ./src/out/cli -name dab.dll)
#Generating the config using dab commands
echo "Generating config file using dab commands";
for file in "${commandFiles[@]}"
do
    commandsFileNameWithPath="$absolutePath/$file";
    while read -r command; do
        cmd="dotnet ${pathToDLL} ${command}";
        eval $cmd;
    done <$commandsFileNameWithPath;
done