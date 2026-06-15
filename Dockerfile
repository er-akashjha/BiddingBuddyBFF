# syntax=docker/dockerfile:1
# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/BiddingBuddy.Bff.Api/BiddingBuddy.Bff.Api.csproj"
RUN dotnet publish "src/BiddingBuddy.Bff.Api/BiddingBuddy.Bff.Api.csproj" \
    -c Release -o /app /p:UseAppHost=false --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
EXPOSE 5124
ENV ASPNETCORE_URLS=http://+:5124 \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "BiddingBuddy.Bff.Api.dll"]
