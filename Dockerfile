# syntax=docker/dockerfile:1.7

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy package management first so restore is cached when only source changes.
COPY Directory.Build.props Directory.Packages.props global.json PaymentsLedger.sln ./
COPY src/PaymentsLedger.Domain/PaymentsLedger.Domain.csproj src/PaymentsLedger.Domain/
COPY src/PaymentsLedger.Application/PaymentsLedger.Application.csproj src/PaymentsLedger.Application/
COPY src/PaymentsLedger.Infrastructure/PaymentsLedger.Infrastructure.csproj src/PaymentsLedger.Infrastructure/
COPY src/PaymentsLedger.Api/PaymentsLedger.Api.csproj src/PaymentsLedger.Api/

RUN dotnet restore src/PaymentsLedger.Api/PaymentsLedger.Api.csproj

# Then copy the rest and publish.
COPY src/ src/
RUN dotnet publish src/PaymentsLedger.Api/PaymentsLedger.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Non-root user for least privilege.
RUN addgroup -S app && adduser -S -G app -h /app app
USER app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build --chown=app:app /app/publish .

ENTRYPOINT ["dotnet", "PaymentsLedger.Api.dll"]
