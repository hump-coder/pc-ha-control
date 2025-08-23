using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using PCVolumeMqtt;

var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
AppConfig config;
if (!File.Exists(configPath))
{
    config = new AppConfig();
    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(configPath, json);
    Console.WriteLine($"Config file created at {configPath}. Please fill it and run again.");
    return;
}
config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath)) ?? new AppConfig();

if (string.IsNullOrWhiteSpace(config.MachineName))
{
    Console.Write("Enter machine name: ");
    config.MachineName = Console.ReadLine() ?? string.Empty;
    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(configPath, json);
}

var mqttFactory = new MqttFactory();
var mqttClient = mqttFactory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer(config.Mqtt.Host, config.Mqtt.Port)
    .WithCredentials(config.Mqtt.Username, config.Mqtt.Password)
    .Build();

await mqttClient.ConnectAsync(options);

var baseTopic = $"pc/{config.MachineName}/volume";
var stateTopic = $"{baseTopic}/state";
var commandTopic = $"{baseTopic}/set";

var discoveryTopic = $"homeassistant/number/{config.MachineName}_volume/config";
var discoveryPayload = JsonSerializer.Serialize(new
{
    name = $"{config.MachineName} Volume",
    command_topic = commandTopic,
    state_topic = stateTopic,
    min = 0,
    max = 100,
    unique_id = $"{config.MachineName}_volume",
    device = new { identifiers = new[] { config.MachineName }, name = config.MachineName }
});
var discoveryMessage = new MqttApplicationMessageBuilder()
    .WithTopic(discoveryTopic)
    .WithPayload(discoveryPayload)
    .WithRetainFlag(true)
    .Build();
await mqttClient.PublishAsync(discoveryMessage);

using var volume = new VolumeService();

async Task PublishState(float v)
{
    var msg = new MqttApplicationMessageBuilder()
        .WithTopic(stateTopic)
        .WithPayload(((int)v).ToString())
        .WithRetainFlag(true)
        .Build();
    await mqttClient.PublishAsync(msg);
}

mqttClient.ApplicationMessageReceivedAsync += e =>
{
    if (e.ApplicationMessage.Topic == commandTopic)
    {
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
        if (float.TryParse(payload, out var vol))
        {
            volume.SetVolume(vol);
        }
    }

    return Task.CompletedTask;
};

await mqttClient.SubscribeAsync(commandTopic);

volume.VolumeChanged += async (_, v) => await PublishState(v);
await PublishState(volume.GetVolume());

Console.WriteLine("PC volume control via MQTT running. Press Ctrl+C to exit.");
await Task.Delay(Timeout.Infinite);
