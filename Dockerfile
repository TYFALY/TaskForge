# =============================================================================
# TaskForge - Multi-Stage Production Dockerfile
# =============================================================================
# Stage 1: Frontend Build
# =============================================================================
FROM node:20-alpine AS frontend-build

WORKDIR /src/frontend

# Copy and install frontend dependencies
COPY taskforge-ui/package*.json ./
RUN npm ci

# Copy frontend source and build
COPY taskforge-ui/ ./
RUN npm run build

# =============================================================================
# Stage 2: Backend SDK & Publish
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS backend-build

WORKDIR /src

# Copy all project files for proper reference resolution
COPY src/TaskForge.Core/ ./TaskForge.Core/
COPY src/TaskForge.Api/ ./TaskForge.Api/

# Copy frontend build artifacts into the API project for static file serving
COPY --from=frontend-build /src/frontend/dist ./TaskForge.Api/wwwroot

# Set working directory to the API project
WORKDIR /src/TaskForge.Api

# Restore dependencies and publish in Release mode
RUN dotnet restore
RUN dotnet publish -c Release -o /app/out --no-restore

# =============================================================================
# Stage 3: Production Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime

# Install ca-certificates for HTTPS connectivity
RUN apk add --no-cache ca-certificates icu-libs

# Create non-root user for security
RUN addgroup -g 1000 appgroup && adduser -u 1000 -G appgroup -s /bin/sh -D appuser

WORKDIR /app

# Copy published output from build stage
COPY --from=backend-build /app/out .

# Set ownership to non-root user
RUN chown -R appuser:appgroup /app

# Switch to non-root user
USER appuser

# Expose the application port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

# Entry point to run the application
ENTRYPOINT ["dotnet", "TaskForge.Api.dll"]
