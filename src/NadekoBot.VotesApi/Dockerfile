FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["NadekoBot.VotesApi.csproj", "NadekoBot.VotesApi/"]
RUN dotnet restore "NadekoBot.VotesApi/NadekoBot.VotesApi.csproj"
COPY . "NadekoBot.VotesApi/"
WORKDIR "/src/NadekoBot.VotesApi"
RUN dotnet build "NadekoBot.VotesApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NadekoBot.VotesApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NadekoBot.VotesApi.dll"]
