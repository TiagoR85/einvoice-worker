# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props .
COPY src/Einvoice.Worker/Einvoice.Worker.csproj src/Einvoice.Worker/
RUN dotnet restore src/Einvoice.Worker/Einvoice.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Einvoice.Worker/Einvoice.Worker.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Worker serves no HTTP endpoint; liveness is process supervision + structured logs.
COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "Einvoice.Worker.dll"]
