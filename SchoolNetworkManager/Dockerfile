FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV APP_DATA_DIR=/app/data

COPY . .

EXPOSE 8080

ENTRYPOINT ["dotnet", "SchoolNetworkManager.dll"]