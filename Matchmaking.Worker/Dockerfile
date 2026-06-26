FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Matchmaking.Worker/Matchmaking.Worker.csproj", "Matchmaking.Worker/"]
COPY ["Matchmaking.Shared/Matchmaking.Shared.csproj", "Matchmaking.Shared/"]
RUN dotnet restore "Matchmaking.Worker/Matchmaking.Worker.csproj"
COPY . .
WORKDIR /src/Matchmaking.Worker
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Matchmaking.Worker.dll"]