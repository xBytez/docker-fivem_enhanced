ARG FIVEM_NUM=119
ARG FIVEM_URL=https://downloads.cfx-services.net/prod/019ffb4d-b63e-7b39-bd95-31986c0f786f/cfx-server_linux_x64.tar.xz
ARG FIVEM_SHA256=81b2965bfd3a628294e516d5a4c90e962aba678c2a4ecba1e79dd8985fe123e9
ARG DATA_VER=e265cb251c88260533c847d4a1a2838c7d828a66
ARG DISABLE_CSHARP_CAS=false

FROM alpine:3.23 AS builder

ARG FIVEM_URL
ARG FIVEM_SHA256
ARG DATA_VER

WORKDIR /output

RUN wget -O /tmp/cfx-server.tar.xz "${FIVEM_URL}" \
 && echo "${FIVEM_SHA256}  /tmp/cfx-server.tar.xz" | sha256sum -c - \
 && tar xJf /tmp/cfx-server.tar.xz --strip-components=2 \
            --exclude './alpine/dev' --exclude './alpine/proc' \
            --exclude './alpine/run' --exclude './alpine/sys' \
            './alpine' \
 && rm /tmp/cfx-server.tar.xz \
 && mkdir -p /output/opt/cfx-server-data /output/usr/local/share \
 && wget -O- https://github.com/citizenfx/cfx-server-data/archive/${DATA_VER}.tar.gz \
        | tar xz --strip-components=1 -C opt/cfx-server-data

ADD server.cfg opt/cfx-server-data
ADD entrypoint usr/bin/entrypoint

RUN chmod +x /output/usr/bin/entrypoint

#================

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS csharp-cas-patcher

WORKDIR /src
COPY tools/CfxCasBypass/ CfxCasBypass/
COPY tools/CfxCoreMapCompat/ CfxCoreMapCompat/
RUN dotnet publish CfxCasBypass/CfxCasBypass.csproj \
        --configuration Release \
        --output /patcher \
        --no-self-contained \
 && dotnet publish CfxCoreMapCompat/CfxCoreMapCompat.csproj \
        --configuration Release \
        --output /map-patcher \
        --no-self-contained

#================

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS csharp-cas-bypassed

ARG DISABLE_CSHARP_CAS=false

COPY --from=builder /output /output
COPY --from=csharp-cas-patcher /patcher /patcher
COPY --from=csharp-cas-patcher /map-patcher /map-patcher

RUN if [ "${DISABLE_CSHARP_CAS}" = "true" ]; then \
        dotnet /patcher/CfxCasBypass.dll /output/opt/cfx-server/coreclr/CitizenFX.Host.dll; \
        dotnet /map-patcher/CfxCoreMapCompat.dll /output/opt/cfx-server/coreclr/fallback/CitizenFX.Core.Server.dll; \
    fi

#================

FROM scratch

ARG FIVEM_URL
ARG FIVEM_NUM
ARG DATA_VER
ARG DISABLE_CSHARP_CAS

LABEL org.opencontainers.image.title="FiveM Enhanced" \
      org.opencontainers.image.url="https://fivem.net" \
      org.opencontainers.image.description="A Docker image for the FiveM for GTAV Enhanced dedicated server." \
      org.opencontainers.image.version=${FIVEM_NUM} \
      io.cfx.server.build=${FIVEM_NUM} \
      io.cfx.server.artifact=${FIVEM_URL} \
      io.cfx.server.data-revision=${DATA_VER} \
      io.cfx.server.csharp-cas-disabled=${DISABLE_CSHARP_CAS}

COPY --from=csharp-cas-bypassed /output/ /
RUN apk add --no-cache tini nodejs

WORKDIR /config
VOLUME ["/config", "/txData"]
EXPOSE 30120/tcp 30120/udp 40120/tcp

# Default to an empty CMD, so it can be used to add separate arguments to the binary.
CMD [""]

ENTRYPOINT ["/sbin/tini", "--", "/usr/bin/entrypoint"]
