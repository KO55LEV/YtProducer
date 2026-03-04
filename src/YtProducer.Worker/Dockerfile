FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/YtProducer.Worker/YtProducer.Worker.csproj", "src/YtProducer.Worker/"]
COPY ["src/YtProducer.Infrastructure/YtProducer.Infrastructure.csproj", "src/YtProducer.Infrastructure/"]
COPY ["src/YtProducer.Domain/YtProducer.Domain.csproj", "src/YtProducer.Domain/"]
RUN dotnet restore "src/YtProducer.Worker/YtProducer.Worker.csproj"

COPY . .
WORKDIR "/src/src/YtProducer.Worker"
RUN dotnet publish "YtProducer.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "YtProducer.Worker.dll"]
