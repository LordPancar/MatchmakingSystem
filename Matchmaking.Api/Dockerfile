FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Matchmaking.Api/Matchmaking.Api.csproj", "Matchmaking.Api/"]
COPY ["Matchmaking.Shared/Matchmaking.Shared.csproj", "Matchmaking.Shared/"]
RUN dotnet restore "Matchmaking.Api/Matchmaking.Api.csproj"
COPY . .
WORKDIR /src/Matchmaking.Api
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Matchmaking.Api.dll"]