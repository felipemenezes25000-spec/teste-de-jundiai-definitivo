FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Jundiai.Api/Jundiai.Api.csproj src/Jundiai.Api/
RUN dotnet restore src/Jundiai.Api/Jundiai.Api.csproj
COPY . .
RUN dotnet publish src/Jundiai.Api/Jundiai.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","Jundiai.Api.dll"]
