FROM node:22 AS ui-build
WORKDIR /src/ui

COPY ui/MaNoir.HomeAutomation.AdminUi.Front/package*.json ./
RUN --mount=type=secret,id=npm_token \
    sh -c 'printf "registry=https://registry.npmjs.org/\\n@manoir-app:registry=https://npm.pkg.github.com/\\n//npm.pkg.github.com/:_authToken=%s\\n" "$(cat /run/secrets/npm_token)" > .npmrc && npm ci --include=optional && rm -f .npmrc'

COPY ui/MaNoir.HomeAutomation.AdminUi.Front/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore apps/MaNoir.HomeAutomation.AdminUi/MaNoir.HomeAutomation.AdminUi.csproj --configfile NuGet.config
RUN dotnet publish apps/MaNoir.HomeAutomation.AdminUi/MaNoir.HomeAutomation.AdminUi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./
COPY --from=ui-build /src/ui/dist ./wwwroot/

ENTRYPOINT ["dotnet", "MaNoir.HomeAutomation.AdminUi.dll"]