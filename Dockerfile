# syntax=docker/dockerfile:1

# ----------------------------------------------------------------------------
# ProcessZero.TimerService - Multi-stage Dockerfile
# Standalone microservice for the countdown timer system.
# Runs independently of the main API so timers don't reset during deployments.
# ----------------------------------------------------------------------------

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore
COPY ProcessZero.TimerService.csproj ./
RUN dotnet restore ProcessZero.TimerService.csproj

# Copy the rest of the source and publish
COPY . .
RUN dotnet publish ProcessZero.TimerService.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8082 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

EXPOSE 8082

ENTRYPOINT ["dotnet", "ProcessZero.TimerService.dll"]