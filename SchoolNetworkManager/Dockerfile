# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SchoolNetworkManager.csproj ./
RUN dotnet restore SchoolNetworkManager.csproj

COPY . .
RUN dotnet publish SchoolNetworkManager.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Τρέχει ως root ώστε να μπορεί να γράφει στους φακέλους του ZimaOS (/DATA/AppData/...)
# και να κάνει ping στους υπολογιστές του δικτύου.
USER root

ENV ASPNETCORE_URLS=http://+:5055 \
    APP_DATA_DIR=/app/data \
    TZ=Europe/Athens

COPY --from=build /app/publish .

RUN mkdir -p /app/data /app/wwwroot/uploads

VOLUME ["/app/data", "/app/wwwroot/uploads"]

EXPOSE 5055

ENTRYPOINT ["dotnet", "SchoolNetworkManager.dll"]
