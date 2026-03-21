FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

COPY src/Nadeko.Medusa/*.csproj src/Nadeko.Medusa/
COPY src/NadekoBot/*.csproj src/NadekoBot/
COPY src/NadekoBot.Coordinator/*.csproj src/NadekoBot.Coordinator/
COPY src/NadekoBot.Generators/*.csproj src/NadekoBot.Generators/
COPY src/NadekoBot.Voice/*.csproj src/NadekoBot.Voice/
COPY src/NadekoBot.StringsMerger/*.csproj src/NadekoBot.StringsMerger/

RUN DOTNET_RID="linux-$([ "$TARGETARCH" = "arm64" ] && echo "arm64" || echo "x64")" \
    && echo "$DOTNET_RID" > /tmp/rid
RUN dotnet restore src/NadekoBot/ -r $(cat /tmp/rid)

COPY . .
WORKDIR /source/src/NadekoBot

RUN dotnet publish -c Release -o /app --self-contained -r $(cat /tmp/rid) --no-restore \
    && mv /app/data /app/data_init \
    && chmod +x /app/NadekoBot

FROM debian:trixie-slim
ARG TARGETARCH
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates curl unzip \
        ffmpeg libsodium23 libopus0 libicu76 tzdata \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://deno.land/install.sh | DENO_INSTALL=/usr/local sh

RUN YT_DLP_BIN="yt-dlp_linux$([ "$TARGETARCH" = "arm64" ] && echo "_aarch64" || echo "")" \
    && curl -L -o /usr/local/bin/yt-dlp "https://github.com/yt-dlp/yt-dlp/releases/latest/download/${YT_DLP_BIN}" \
    && chmod 755 /usr/local/bin/yt-dlp

RUN DAVE_ARCH="$([ "$TARGETARCH" = "arm64" ] && echo "ARM64" || echo "X64")" \
    && curl -L -o /tmp/libdave.zip "https://github.com/discord/libdave/releases/download/v1.1.1%2Fcpp/libdave-Linux-${DAVE_ARCH}-boringssl.zip" \
    && mkdir -p /tmp/libdave \
    && unzip -o /tmp/libdave.zip -d /tmp/libdave \
    && rm /tmp/libdave.zip

COPY --from=build /app ./
COPY docker-entrypoint.sh /usr/local/sbin/

RUN ARCH_DIR="$([ "$TARGETARCH" = "arm64" ] && echo "aarch64-linux-gnu" || echo "x86_64-linux-gnu")" \
    && rm -f /app/data_init/lib/libsodium.so /app/data_init/lib/opus.so \
    && ln -sf /usr/lib/$ARCH_DIR/libsodium.so.23 /app/data_init/lib/libsodium.so \
    && ln -sf /usr/lib/$ARCH_DIR/libopus.so.0 /app/data_init/lib/opus.so \
    && find /tmp/libdave -name "libdave.so" -exec cp {} /app/data_init/lib/libdave.so \;

VOLUME [ "/app/data" ]

ENTRYPOINT [ "/usr/local/sbin/docker-entrypoint.sh" ]
CMD [ "./NadekoBot" ]