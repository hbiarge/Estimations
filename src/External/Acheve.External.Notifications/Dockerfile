#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/Directory.Packages.props", "src/"]
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/External/Acheve.External.Notifications/Acheve.External.Notifications.csproj", "src/External/Acheve.External.Notifications/"]
COPY ["src/Common/Acheve.Common.Shared/Acheve.Common.Shared.csproj", "src/Common/Acheve.Common.Shared/"]
RUN dotnet restore "src/External/Acheve.External.Notifications/Acheve.External.Notifications.csproj"
COPY . .
WORKDIR "/src/src/External/Acheve.External.Notifications"
RUN dotnet build "Acheve.External.Notifications.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Acheve.External.Notifications.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Acheve.External.Notifications.dll"]