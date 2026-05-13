# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY NuGet.config global.json Directory.Build.props Directory.Packages.props PlaywrightMCPSharp.sln ./
COPY src/PlaywrightMCPSharp.Server/PlaywrightMCPSharp.Server.csproj src/PlaywrightMCPSharp.Server/
RUN dotnet restore src/PlaywrightMCPSharp.Server/PlaywrightMCPSharp.Server.csproj

COPY . .
RUN dotnet publish src/PlaywrightMCPSharp.Server/PlaywrightMCPSharp.Server.csproj \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Host=0.0.0.0 \
    PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Port=5704 \
    PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Transport=Http \
    PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__Headless=true

RUN mkdir -p /app/logs && chown -R $APP_UID:0 /app
COPY --from=build --chown=$APP_UID:0 /app/publish ./

USER $APP_UID
EXPOSE 5704
VOLUME ["/app/logs"]

ENTRYPOINT ["dotnet", "PlaywrightMCPSharp.Server.dll"]
