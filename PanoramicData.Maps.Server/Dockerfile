# Static-map + geocoding service. Renders natively with SkiaSharp (no headless browser), so the
# runtime image is a plain ASP.NET Core image - small and fast to start.
#
# Build + push (CI does this to Docker Hub as panoramicdata/maps):
#   docker build -t panoramicdata/maps:dev -f PanoramicData.Maps.Server/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore PanoramicData.Maps.Server/PanoramicData.Maps.Server.csproj
RUN dotnet publish PanoramicData.Maps.Server/PanoramicData.Maps.Server.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# SkiaSharp.NativeAssets.Linux links fontconfig; install it plus a font so text (the OpenStreetMap
# attribution now, place labels later) renders. fonts-dejavu-core is small and widely available.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PanoramicData.Maps.Server.dll"]
