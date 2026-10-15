# MatchShift - single container: ASP.NET Core API serving the built React UI.
# All football data is synthetic and generated in-process; no data volumes are required.

FROM node:22-alpine AS web
WORKDIR /web
COPY src/MatchShift.Web/package*.json ./
RUN npm ci
COPY src/MatchShift.Web/ ./
RUN npx tsc -b && npx vite build --outDir /web/dist

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props MatchShift.sln ./
COPY src/ src/
COPY tests/ tests/
COPY tools/ tools/
RUN dotnet publish src/MatchShift.Api/MatchShift.Api.csproj -c Release -o /app/publish /p:UseAppHost=false
COPY --from=web /web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "MatchShift.Api.dll"]
