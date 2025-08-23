# pc-ha-control

Utility to expose Windows PC sound device volumes to Home Assistant via MQTT.

## Setup

1. Publish a single-file executable for Windows (requires .NET SDK 8.0 or later):

   ```sh
   dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```

   The resulting `PCVolumeMqtt.exe` will be in
   `src/PCVolumeMqtt/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/`.

2. Copy the executable to the target PC and run it. On first launch it displays
   dialogs to collect your MQTT host, port, username, password, and a machine
   name. These values are stored in `config.json` alongside the executable for
   future runs. The app registers itself to start on login and runs in the
   system tray; right‑click the tray icon to open settings or exit. The
   settings dialog can be used later to change MQTT details or rename the
   machine.

Home Assistant will automatically discover each active sound device through MQTT
discovery and expose a numeric entity to view or change that device's volume.

