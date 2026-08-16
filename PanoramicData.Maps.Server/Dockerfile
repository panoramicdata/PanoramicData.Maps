# Static-map + geocoding service. Renders with MapLibre GL JS in headless Chromium (Playwright),
# so the runtime image needs a browser + its OS dependencies. The official Playwright .NET image
# bundles both; keep its tag aligned with the Microsoft.Playwright package version in
# Directory.Packages.props (and a .NET 10-capable variant).
#
# Build + push (CI does this to Docker Hub as panoramicdata/maps):
#   docker build -t panoramicdata/maps:dev -f PanoramicData.Maps.Server/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore PanoramicData.Maps.Server/PanoramicData.Maps.Server.csproj
RUN dotnet publish PanoramicData.Maps.Server/PanoramicData.Maps.Server.csproj -c Release -o /app /p:UseAppHost=false

# Runtime: Playwright image ships Chromium + OS deps. NOTE: verify this tag provides the .NET 10
# ASP.NET runtime; if not, base on mcr.microsoft.com/dotnet/aspnet:10.0 and run
# `playwright install --with-deps chromium` at build time instead.
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-noble AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_gcServer=1
EXPOSE 8080
ENTRYPOINT ["dotnet", "PanoramicData.Maps.Server.dll"]
