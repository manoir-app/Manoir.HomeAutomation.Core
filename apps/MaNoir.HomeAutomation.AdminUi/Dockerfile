FROM node:22 AS ui-build
WORKDIR /src/ui

COPY ui/MaNoir.HomeAutomation.AdminUi.Front/package*.json ./
RUN npm install --include=optional --registry=https://registry.npmjs.org
RUN npm install --no-save @rolldown/binding-linux-x64-gnu@1.2.8 --registry=https://registry.npmjs.org

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