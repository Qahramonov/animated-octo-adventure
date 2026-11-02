# OROL Survival dedicated server - Linux container image.
# Build from the REPOSITORY ROOT (the server compiles shared code from UnityClient/Assets):
#   docker build -f Server/deploy/Dockerfile -t orol-server .
# Run:
#   docker run -d --name orol -p 7777:7777/udp -p 8080:8080/tcp -v orol-data:/data orol-server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY UnityClient/Assets/Plugins/LiteNetLib UnityClient/Assets/Plugins/LiteNetLib
COPY UnityClient/Assets/Survival/Core UnityClient/Assets/Survival/Core
COPY UnityClient/Assets/Survival/World UnityClient/Assets/Survival/World
COPY UnityClient/Assets/Survival/Gameplay UnityClient/Assets/Survival/Gameplay
COPY UnityClient/Assets/Survival/Networking UnityClient/Assets/Survival/Networking
COPY UnityClient/Assets/Survival/Server UnityClient/Assets/Survival/Server
COPY Server/Directory.Build.props Server/
COPY Server/src Server/src
COPY Server/deploy/server.json Server/deploy/server.json
RUN dotnet publish Server/src/Survival.Server.Host -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out .
RUN useradd --system --uid 10001 orol && mkdir -p /data && chown orol /data
USER orol
ENV OROL_DATA_DIRECTORY=/data
EXPOSE 7777/udp 8080/tcp
HEALTHCHECK --interval=30s --timeout=3s CMD [ "sh", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && grep -q ok <&3" ]
ENTRYPOINT ["dotnet", "orol-server.dll", "--no-console"]
