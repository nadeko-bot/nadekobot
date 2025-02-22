# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy the .csproj files for each project
COPY src/Nadeko.Medusa/*.csproj src/Nadeko.Medusa/
COPY src/NadekoBot/*.csproj src/NadekoBot/
COPY src/NadekoBot.Coordinator/*.csproj src/NadekoBot.Coordinator/
COPY src/NadekoBot.Generators/*.csproj src/NadekoBot.Generators/
COPY src/NadekoBot.Voice/*.csproj src/NadekoBot.Voice/
COPY src/NadekoBot.GrpcApiBase/*.csproj src/NadekoBot.GrpcApiBase/

# Restore the dependencies for the NadekoBot project
RUN dotnet restore src/NadekoBot/ -r linux-musl-x64

# Copy the rest of the source code
COPY . .

WORKDIR /source/src/NadekoBot

# Build for linux-musl-x64 runtime as the image is based on alpine
RUN dotnet publish -c Release -o /app --self-contained -r linux-musl-x64 --no-restore \
    && mv /app/data /app/data_init \
    && chmod +x /app/NadekoBot

# Final stage
FROM alpine:3.20
WORKDIR /app

# Music dependencies
ADD --chmod=755 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux /usr/local/bin/yt-dlp
RUN apk add --no-cache ffmpeg libsodium

# Required dependencies
# icu-libs is required for globalization
RUN apk update; \
    apk add --no-cache libstdc++ libgcc icu-libs libc6-compat \
    && rm -rf /var/cache/apk/*;

COPY --from=build /app ./
COPY docker-entrypoint.sh /usr/local/sbin/

RUN rm /app/data_init/lib/libsodium.so \
    && ln -s /usr/lib/libsodium.so.26 /app/data_init/lib/libsodium.so

VOLUME [ "/app/data" ]

ENTRYPOINT [ "/usr/local/sbin/docker-entrypoint.sh" ]
CMD [ "./NadekoBot" ]
