FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/YtProducer.Api/YtProducer.Api.csproj", "src/YtProducer.Api/"]
COPY ["src/YtProducer.Infrastructure/YtProducer.Infrastructure.csproj", "src/YtProducer.Infrastructure/"]
COPY ["src/YtProducer.Domain/YtProducer.Domain.csproj", "src/YtProducer.Domain/"]
COPY ["src/YtProducer.Contracts/YtProducer.Contracts.csproj", "src/YtProducer.Contracts/"]
RUN dotnet restore "src/YtProducer.Api/YtProducer.Api.csproj"

COPY . .
WORKDIR "/src/src/YtProducer.Api"
RUN dotnet publish "YtProducer.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "YtProducer.Api.dll"]
