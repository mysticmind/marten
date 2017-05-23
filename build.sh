#!/bin/bash
set -ev

#npm install
dotnet --info

dotnet restore ./src/Marten.sln
dotnet build ./src/Marten.sln --configuration Release
#npm run test
#dotnet test .\src\Marten.Testing\Marten.Testing.csproj --configuration Release 