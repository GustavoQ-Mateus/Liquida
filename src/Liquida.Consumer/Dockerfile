FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Liquida.Consumer/Liquida.Consumer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Liquida.Consumer.dll"]
