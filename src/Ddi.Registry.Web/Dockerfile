# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Ddi.Registry.Web/Ddi.Registry.Web.csproj", "src/Ddi.Registry.Web/"]
COPY ["src/Ddi.Registry.Data/Ddi.Registry.Data.csproj", "src/Ddi.Registry.Data/"]
RUN dotnet restore "./src/Ddi.Registry.Web/Ddi.Registry.Web.csproj"
COPY . .
WORKDIR "/src/src/Ddi.Registry.Web"
RUN dotnet build "./Ddi.Registry.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ddi.Registry.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

# install libgssapi_krb5.so.2 dependency
RUN apt-get update && \
    apt-get install -y --no-install-recommends libgssapi-krb5-2 && \
    apt-get purge -y --auto-remove && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/* /usr/share/doc/* /usr/share/man/*

USER $APP_UID

WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ddi.Registry.Web.dll"]