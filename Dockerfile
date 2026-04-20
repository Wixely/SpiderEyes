FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Packages.props NuGet.config SpiderEyes.sln ./
COPY src/SpiderEyes.Server/SpiderEyes.Server.csproj src/SpiderEyes.Server/
RUN dotnet restore src/SpiderEyes.Server/SpiderEyes.Server.csproj

COPY . .
RUN dotnet publish src/SpiderEyes.Server/SpiderEyes.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8931
ENV SpiderEyes__Server__Host=0.0.0.0
ENV SpiderEyes__Server__Port=8931
ENV SpiderEyes__Server__Transport=Http
ENV SpiderEyes__Browser__Headless=true

COPY --from=build /app/publish .

EXPOSE 8931

ENTRYPOINT ["dotnet", "SpiderEyes.Server.dll"]
