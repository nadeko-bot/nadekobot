dotnet pack -o bin/Release/packed
dotnet nuget push bin/Release/packed/ --api-key $env:nadeko_myget_api_key --source https://www.myget.org/F/nadeko/api/v2/package