FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY backend/Oa.Api/Oa.Api.csproj backend/Oa.Api/
RUN dotnet restore backend/Oa.Api/Oa.Api.csproj
COPY backend/Oa.Api/ backend/Oa.Api/
RUN dotnet publish backend/Oa.Api/Oa.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
RUN apk add --no-cache curl
WORKDIR /app
COPY --from=build /app/publish/ .
RUN mkdir -p /var/lib/cute-oa/files && chown -R app:app /app /var/lib/cute-oa
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
VOLUME ["/var/lib/cute-oa/files"]
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=4 CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Oa.Api.dll"]
