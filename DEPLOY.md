# ProcessZero.TimerService - Linux Deployment Guide

## Overview
The timer service is a standalone ASP.NET Core 8 API that handles session management and background credit consumption. It runs in its own Docker container, independent of the main API.

## Prerequisites on VPS
- Docker Engine 20.10+
- Docker Compose 2.0+
- MySQL 8.0+ (shared with main API)
- Port 8082 available (or change in Dockerfile)

## Deployment Options

### Option 1: Deploy via Docker Compose (Recommended)

The timer service is already included in the main `docker-compose.yml` at `api/ProcessZero.Web/docker-compose.yml`.

**From your VPS:**

```bash
cd /path/to/ProcessZeroWorkspace/api/ProcessZero.Web

# Create .processzero.env if it doesn't exist
nano .processzero.env
```

Add these environment variables:
```env
# Database
CONNECTION_STRING=Server=localhost;Port=3306;Database=processzero;User=processzero;Password=YOUR_DB_PASSWORD

# Timer Service Auth (MUST match in both places)
TIMER_API_KEY=YOUR_SECURE_API_KEY

# Main API config (existing vars...)
```

**Deploy just the timer service:**
```bash
docker-compose up --build -d timer-service
```

**Deploy everything:**
```bash
docker-compose up --build -d
```

---

### Option 2: Manual Docker Deployment

If you want to deploy the timer service independently:

```bash
cd /path/to/ProcessZeroWorkspace/services/timer-service

# Build the image
docker build -t processzero-timer:latest .

# Run the container
docker run -d \
  --name processzero-timer \
  --restart unless-stopped \
  -p 8082:8082 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:8082 \
  -e ConnectionStrings__DefaultConnection="Server=localhost;Port=3306;Database=processzero;User=processzero;Password=YOUR_DB_PASSWORD" \
  -e TimerApiKey="YOUR_SECURE_API_KEY" \
  processzero-timer:latest
```

---

### Option 3: Systemd Service (without Docker)

If you want to run it directly on the host:

```bash
cd /path/to/ProcessZeroWorkspace/services/timer-service

# Publish the app
dotnet publish ProcessZero.TimerService.csproj -c Release -o /opt/timer-service/publish

# Create systemd service
sudo nano /etc/systemd/system/timer-service.service
```

Service file contents:
```ini
[Unit]
Description=ProcessZero Timer Service
After=network.target mysql.service

[Service]
Type=notify
WorkingDirectory=/opt/timer-service/publish
ExecStart=/usr/bin/dotnet /opt/timer-service/publish/ProcessZero.TimerService.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:8082
Environment=ConnectionStrings__DefaultConnection=Server=localhost;Port=3306;Database=processzero;User=processzero;Password=YOUR_DB_PASSWORD
Environment=TimerApiKey=YOUR_SECURE_API_KEY
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable timer-service
sudo systemctl start timer-service

# Check status
sudo systemctl status timer-service

# View logs
sudo journalctl -u timer-service -f
```

---

## Verification

**Health check:**
```bash
curl http://localhost:8082/api/timer/health
```

Expected response:
```json
{
  "service": "ProcessZero Timer Service",
  "status": "running",
  "time": "2025-07-23T23:45:00Z"
}
```

**View Hangfire Dashboard:**
```bash
curl http://localhost:8082/hangfire
```

---

## Important Notes

1. **Same MySQL Database**: The timer service shares the same MySQL database as the main API. The tables it uses are:
   - `UserSessions`
   - `ConsumptionConfigs`
   - `UserWallets`

2. **API Key**: The `TIMER_API_KEY` must be the same in:
   - Timer service's `appsettings.json` (or env var `TimerApiKey`)
   - Main API's `appsettings.json` under `TimerService:ApiKey` (or env var `TimerService__ApiKey` in docker-compose)
   - `.processzero.env` file's `TIMER_API_KEY` variable

3. **Independent Operation**: 
   - You can restart the main API without affecting the timer service
   - You can restart the timer service without affecting the main API
   - Hangfire jobs continue running even during deployments

4. **Monitoring**: 
   - Check logs: `docker logs processzero-timer -f`
   - Hangfire dashboard: `http://your-server:8082/hangfire`
   - Health endpoint: `http://your-server:8082/api/timer/health`

---

## Troubleshooting

**Timer service won't start:**
```bash
# Check logs
docker logs processzero-timer

# Common issues:
# 1. Database connection failed - verify CONNECTION_STRING
# 2. Port 8082 already in use - change port mapping
# 3. Missing TimerApiKey - verify env variable
```

**Main API can't connect to timer service:**
```bash
# Verify the API key matches
grep TIMER_API_KEY .processzero.env

# Test connectivity from main API container
docker exec -it processzero-web curl http://timer-service:8082/api/timer/health

# Check network
docker network inspect ProcessZero.Web_default
```

---

## Updates & Redeployment

After code changes:
```bash
# Rebuild and restart timer service
docker-compose up --build -d timer-service

# Check it's running
docker-compose ps timer-service

# Watch logs
docker-compose logs -f timer-service