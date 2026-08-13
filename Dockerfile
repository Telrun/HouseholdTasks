# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore "HouseholdTasks.Server/HouseholdTasks.Server.csproj"

RUN dotnet publish "HouseholdTasks.Server/HouseholdTasks.Server.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore


# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "HouseholdTasks.Server.dll"]