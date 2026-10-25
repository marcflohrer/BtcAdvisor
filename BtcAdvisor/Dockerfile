FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BtcAdvisor.csproj .
RUN dotnet restore --disable-parallel
COPY . .
RUN dotnet publish BtcAdvisor.csproj -c Release -o /src/publish --self-contained=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl is required by the container healthcheck command.
RUN apt-get update \
  && apt-get install -y --no-install-recommends curl \
  && rm -rf /var/lib/apt/lists/*

# Nicht-root-User
RUN useradd -m appuser && chown -R appuser /app
USER appuser

COPY --from=build /src/publish .
ENV ASPNETCORE_URLS=http://+:6777

HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl -f http://localhost:6777/ || exit 1

ENTRYPOINT ["dotnet", "BtcAdvisor.dll"]
