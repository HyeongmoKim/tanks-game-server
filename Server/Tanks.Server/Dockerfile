FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Tanks.Server.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish Tanks.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

COPY --from=build /app/publish .
EXPOSE 7777

USER $APP_UID
ENTRYPOINT ["dotnet", "Tanks.Server.dll"]