FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/FimpleCaseStudy.Api/FimpleCaseStudy.Api.csproj
RUN dotnet publish src/FimpleCaseStudy.Api/FimpleCaseStudy.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FimpleCaseStudy.Api.dll"]
