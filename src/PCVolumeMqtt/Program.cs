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

    Console.Write("Enter MQTT host: ");
    config.Mqtt.Host = Console.ReadLine() ?? "localhost";

    Console.Write("Enter MQTT port (default 1883): ");
    var portInput = Console.ReadLine();
    if (int.TryParse(portInput, out var port))
    {
        config.Mqtt.Port = port;
    }

    Console.Write("Enter MQTT username: ");
    config.Mqtt.Username = Console.ReadLine() ?? string.Empty;

    Console.Write("Enter MQTT password: ");
    config.Mqtt.Password = ReadPassword();

    Console.Write("Enter machine name: ");
    config.MachineName = Console.ReadLine() ?? string.Empty;

    SaveConfig();
    Console.WriteLine($"Config saved to {configPath}.");
}
else
{
    config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath)) ?? new AppConfig();

    if (string.IsNullOrWhiteSpace(config.MachineName) || string.IsNullOrWhiteSpace(config.Mqtt.Host))
    {
        if (string.IsNullOrWhiteSpace(config.Mqtt.Host))
        {
            Console.Write("Enter MQTT host: ");
            config.Mqtt.Host = Console.ReadLine() ?? "localhost";

            Console.Write("Enter MQTT port (default 1883): ");
            var portInput = Console.ReadLine();
            if (int.TryParse(portInput, out var port))
            {
                config.Mqtt.Port = port;
            }

            Console.Write("Enter MQTT username: ");
            config.Mqtt.Username = Console.ReadLine() ?? string.Empty;

            Console.Write("Enter MQTT password: ");
            config.Mqtt.Password = ReadPassword();
        }

        if (string.IsNullOrWhiteSpace(config.MachineName))
        {
            Console.Write("Enter machine name: ");
            config.MachineName = Console.ReadLine() ?? string.Empty;
        }

        SaveConfig();
    }
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

void SaveConfig()
{
    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(configPath, json);
}

string ReadPassword()
{
    var password = new StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Length--;
            Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
            Console.Write("*");
        }
    }
    Console.WriteLine();
    return password.ToString();
}
