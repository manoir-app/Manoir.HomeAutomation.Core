FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore apps/MaNoir.Agents.Sarah/MaNoir.Agents.Sarah.csproj --configfile NuGet.config
RUN dotnet publish apps/MaNoir.Agents.Sarah/MaNoir.Agents.Sarah.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "MaNoir.Agents.Sarah.dll"]