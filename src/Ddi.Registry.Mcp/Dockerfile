FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj", "src/Ddi.Registry.Mcp/"]
COPY ["src/Ddi.Registry.Data/Ddi.Registry.Data.csproj", "src/Ddi.Registry.Data/"]
RUN dotnet restore "./src/Ddi.Registry.Mcp/Ddi.Registry.Mcp.csproj"
COPY . .
WORKDIR "/src/src/Ddi.Registry.Mcp"
RUN dotnet build "./Ddi.Registry.Mcp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ddi.Registry.Mcp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER $APP_UID
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ddi.Registry.Mcp.dll"]