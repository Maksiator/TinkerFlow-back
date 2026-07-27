FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TinkerFlow.API/TinkerFlow.API.csproj", "TinkerFlow.API/"]
COPY ["TinkerFlow.Infrastructure/TinkerFlow.Infrastructure.csproj", "TinkerFlow.Infrastructure/"]
COPY ["TinkerFlow.Domain/TinkerFlow.Domain.csproj", "TinkerFlow.Domain/"]
RUN dotnet restore "TinkerFlow.API/TinkerFlow.API.csproj"
COPY . .
WORKDIR "/src/TinkerFlow.API"
RUN dotnet build "./TinkerFlow.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TinkerFlow.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TinkerFlow.API.dll"]
