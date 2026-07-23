# Push timer-service to its own Git repository

## Initialize and push to remote

```bash
cd /path/to/ProcessZeroWorkspace/services/timer-service

# Initialize git repo
git init
git add .
git commit -m "Initial commit: ProcessZero.TimerService standalone microservice"

# Add remote (replace with your actual repo URL)
git remote add origin git@github.com:your-org/processzero-timer-service.git
# or: git remote add origin https://github.com/your-org/processzero-timer-service.git

# Push to remote
git branch -M main
git push -u origin main
```

## Suggested repo structure
If you want to keep the timer service in the same GitHub org/repo group as your main API, you could also use a **submodule** or **monorepo** approach. But for independent deployment and CI/CD, a separate repo is cleanest.

## CI/CD suggestion
Set up a simple GitHub Actions workflow to build and push the Docker image on push to `main`:

```yaml
# .github/workflows/docker.yml
name: Build and Push Timer Service Docker Image

on:
  push:
    branches: [ main ]
    paths:
      - 'services/timer-service/**'

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
        with:
          path: services/timer-service

      - name: Login to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: services/timer-service
          push: true
          tags: your-org/processzero-timer:latest
```

Then on your VPS:
```bash
docker compose pull timer-service
docker compose up -d timer-service