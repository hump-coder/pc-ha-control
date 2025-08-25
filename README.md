# pc-ha-control

Utility to expose Windows PC sound device volumes to Home Assistant via MQTT.

## Setup

1. Publish the application for Windows (requires .NET SDK 8.0 or later):

   ```sh
   dotnet publish -c Release -r win-x64
   ```

   The publish output will be in
   `src/PCVolumeMqtt/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/`
   with `PCVolumeMqtt.exe` at the root and a `content` subfolder containing
   all other files.

2. Copy the executable to the target PC and run it. On first launch it displays
   dialogs to collect your MQTT host, port, username, password, and a machine
   name. These values are stored in `config.json` alongside the executable for
   future runs. The password is encrypted using the Windows Data Protection API
   and can only be decrypted by the same user account. The app registers itself
   to start on login and creates an entry in Add/Remove Programs so it can be
   uninstalled later. It runs in the system tray; right‑click the tray icon to
   open settings or exit. The settings dialog can be used later to change MQTT
   details or rename the machine.

3. To uninstall, either use **Add/Remove Programs** or run the executable with
   the `--uninstall` flag.

Home Assistant will automatically discover each active sound device through MQTT
discovery and expose a numeric entity to view or change that device's volume.

