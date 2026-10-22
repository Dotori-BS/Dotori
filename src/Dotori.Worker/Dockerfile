FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY proto/          proto/
COPY src/Dotori.Core/ src/Dotori.Core/
COPY src/Dotori.Grpc/ src/Dotori.Grpc/
COPY src/Dotori.Worker/ src/Dotori.Worker/

RUN dotnet publish src/Dotori.Worker/Dotori.Worker.csproj \
    -c Release -o /app

# Use Ubuntu base so we can install clang
FROM ubuntu:24.04
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        clang lld libc++-dev libc++abi-dev \
        dotnet-runtime-10.0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5100
EXPOSE 5100
ENTRYPOINT ["dotori-worker"]
