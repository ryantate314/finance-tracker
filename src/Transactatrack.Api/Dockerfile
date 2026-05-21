# syntax=docker/dockerfile:1.7
# Build context is the repo root so we can copy sibling .csproj files.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Transactatrack.Domain/Transactatrack.Domain.csproj         src/Transactatrack.Domain/
COPY src/Transactatrack.Application/Transactatrack.Application.csproj src/Transactatrack.Application/
COPY src/Transactatrack.Infrastructure/Transactatrack.Infrastructure.csproj src/Transactatrack.Infrastructure/
COPY src/Transactatrack.Api/Transactatrack.Api.csproj               src/Transactatrack.Api/
RUN dotnet restore src/Transactatrack.Api/Transactatrack.Api.csproj

COPY src/Transactatrack.Domain/         src/Transactatrack.Domain/
COPY src/Transactatrack.Application/    src/Transactatrack.Application/
COPY src/Transactatrack.Infrastructure/ src/Transactatrack.Infrastructure/
COPY src/Transactatrack.Api/            src/Transactatrack.Api/

RUN dotnet publish src/Transactatrack.Api/Transactatrack.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "Transactatrack.Api.dll"]
