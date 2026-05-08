FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Packages.props NuGet.config PlaywrightMCPSharp.sln ./
COPY src/PlaywrightMCPSharp.Server/PlaywrightMCPSharp.Server.csproj src/PlaywrightMCPSharp.Server/
RUN dotnet restore src/PlaywrightMCPSharp.Server/PlaywrightMCPSharp.Server.csproj

COPY . .
RUN dotnet publish src/PlaywrightMCPSharp.Server/PlaywrightMCPSharp.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8931
ENV PlaywrightMCPSharp__Server__Host=0.0.0.0
ENV PlaywrightMCPSharp__Server__Port=8931
ENV PlaywrightMCPSharp__Server__Transport=Http
ENV PlaywrightMCPSharp__Browser__Headless=true

COPY --from=build /app/publish .

EXPOSE 8931

ENTRYPOINT ["dotnet", "PlaywrightMCPSharp.Server.dll"]
