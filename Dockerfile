# breakbar-logs: one image with the API, Elite Insights and the built frontend.
# Build from the repository root (submodule must be checked out):
#   git submodule update --init && docker build -t breakbar-logs .
# GitHub Actions builds and publishes it as ghcr.io/breakbarcc/breakbar-log-parser (see .github/workflows/ci.yml).

# ---- Frontend --------------------------------------------------------------------------------
FROM node:24-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# vite.config.ts writes to ../backend/src/Breakbar.Api/wwwroot
RUN npm run build

# ---- Backend ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY external/ external/
COPY backend/ backend/
RUN dotnet publish backend/src/Breakbar.Api -c Release -o /app
COPY --from=frontend /src/backend/src/Breakbar.Api/wwwroot /app/wwwroot

# ---- Runtime ---------------------------------------------------------------------------------
# Debian-based ASP.NET image: includes ICU, which Elite Insights needs (docs/PLAN.md 6b).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__DataDirectory=/app/data \
    ForwardedHeaders__TrustAllProxies=true \
    DOTNET_gcServer=0
COPY --from=backend /app ./
# Runs as the image's non-root user "app" (UID 1654); the data bind mount must be writable for that UID.
RUN mkdir -p /app/data && chown app:app /app/data
USER app
EXPOSE 8080
VOLUME ["/app/data"]
# No curl/wget in the image: plain HTTP request over bash's /dev/tcp.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /api/health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Breakbar.Api.dll"]
