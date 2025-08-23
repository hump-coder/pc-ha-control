# pc-ha-control

Utility to expose Windows PC volume to Home Assistant via MQTT.

## Setup

1. Publish a single-file executable for Windows (requires .NET SDK 5.0 or later):

   ```sh
   dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```

   The resulting `PCVolumeMqtt.exe` will be in
   `src/PCVolumeMqtt/bin/Release/net5.0-windows10.0.19041.0/win-x64/publish/`.

2. Copy the executable to the target PC and run it. On first launch it will ask
   for your MQTT host, port, username, password, and a machine name. These
   values are stored in `config.json` alongside the executable for future runs.

Home Assistant will automatically discover the device through MQTT discovery
and expose a numeric entity to view or change the PC volume.

