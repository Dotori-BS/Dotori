FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY proto/           proto/
COPY src/Dotori.Core/ src/Dotori.Core/
COPY src/Dotori.Grpc/ src/Dotori.Grpc/
COPY src/Dotori.BuildServer/ src/Dotori.BuildServer/

RUN dotnet publish src/Dotori.BuildServer/Dotori.BuildServer.csproj \
    -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5100
ENV DOTORI_WORKERS=""

EXPOSE 5100
ENTRYPOINT ["dotori-server"]
