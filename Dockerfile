FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Mocksmith.slnx ./
COPY .config/dotnet-tools.json .config/
COPY src/Mocksmith/Mocksmith.csproj src/Mocksmith/
COPY src/Mocksmith.Core/Mocksmith.Core.csproj src/Mocksmith.Core/
COPY tests/Mocksmith.Tests/Mocksmith.Tests.csproj tests/Mocksmith.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Mocksmith/Mocksmith.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# curl is only used by the container healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data && chown app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    MOCKSMITH_DATA_DIR=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mocksmith.dll"]
