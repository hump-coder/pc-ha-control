# pc-ha-control

Utility to expose Windows PC volume to Home Assistant via MQTT.

## Setup

1. Copy `config-example.json` to `config.json` and edit it with your MQTT
   broker details.
2. Build the project:

   ```sh
   dotnet build
   ```
3. Run the application. On first start it will ask for a machine name, which is
   used to create unique MQTT topics.

Home Assistant will automatically discover the device through MQTT discovery
and expose a numeric entity to view or change the PC volume.

