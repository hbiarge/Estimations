#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/Directory.Packages.props", "src/"]
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Application/Acheve.Application.Api/Acheve.Application.Api.csproj", "src/Application/Acheve.Application.Api/"]
COPY ["src/Common/Acheve.Common.Messages/Acheve.Common.Messages.csproj", "src/Common/Acheve.Common.Messages/"]
COPY ["src/Common/Acheve.Common.Shared/Acheve.Common.Shared.csproj", "src/Common/Acheve.Common.Shared/"]
RUN dotnet restore "src/Application/Acheve.Application.Api/Acheve.Application.Api.csproj"
COPY . .
WORKDIR "/src/src/Application/Acheve.Application.Api"
RUN dotnet build "Acheve.Application.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Acheve.Application.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Acheve.Application.Api.dll"]