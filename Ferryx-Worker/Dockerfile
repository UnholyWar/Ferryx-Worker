# Base runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app

# Uygulama portu (yüksek port)
EXPOSE 16080

# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Ferryx-Worker/Ferryx-Worker.csproj", "Ferryx-Worker/"]
RUN dotnet restore "./Ferryx-Worker/Ferryx-Worker.csproj"
COPY . .
WORKDIR "/src/Ferryx-Worker"
RUN dotnet build "./Ferryx-Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ferryx-Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Kestrel portunu sabitle (istersen deðiþtirebilirsin)
ENV ASPNETCORE_URLS=http://0.0.0.0:16080

# Zorunlu env kontrol + start
ENTRYPOINT ["/bin/sh","-c", "\
  test -n \"$FERRYX_HUB_TOKEN\" || (echo 'FERRYX_HUB_TOKEN is required' && exit 1); \
  test -n \"$FERRYX_GROUP\" || (echo 'FERRYX_GROUP is required' && exit 1); \
  test -n \"$FERRYX_HUB_URL\" || (echo 'FERRYX_HUB_URL is required' && exit 1); \
  exec dotnet Ferryx-Worker.dll \
"]