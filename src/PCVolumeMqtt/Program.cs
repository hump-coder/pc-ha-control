using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using MQTTnet;
using MQTTnet.Client;
using Microsoft.Win32;
using PCVolumeMqtt;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        AppConfig config;
        if (!File.Exists(configPath))
        {
            config = new AppConfig();
            PromptForConfig(config);
            SaveConfig(config, configPath);
            MessageBox.Show($"Config saved to {configPath}.");
        }
        else
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath)) ?? new AppConfig();

            if (string.IsNullOrWhiteSpace(config.MachineName) || string.IsNullOrWhiteSpace(config.Mqtt.Host))
            {
                if (string.IsNullOrWhiteSpace(config.Mqtt.Host))
                {
                    PromptForConfig(config);
                }

                if (string.IsNullOrWhiteSpace(config.MachineName))
                {
                    config.MachineName = Prompt("Machine name:", config.MachineName);
                }

                SaveConfig(config, configPath);
            }
        }

        EnsureStartup();

        var mqttFactory = new MqttFactory();
        var mqttClient = mqttFactory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(config.Mqtt.Host, config.Mqtt.Port)
            .WithCredentials(config.Mqtt.Username, config.Mqtt.Password)
            .Build();

        await mqttClient.ConnectAsync(options);

        var baseTopic = $"pc/{config.MachineName}/volume";

        using var volume = new VolumeService();

        var commandTopics = new Dictionary<string, string>(); // topic -> deviceId
        var stateTopics = new Dictionary<string, string>();   // deviceId -> topic

        async Task PublishState(string deviceId, float v)
        {
            if (!stateTopics.TryGetValue(deviceId, out var topic))
            {
                return;
            }

            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(((int)v).ToString())
                .WithRetainFlag(true)
                .Build();
            await mqttClient.PublishAsync(msg);
        }

        foreach (var device in volume.GetDevices())
        {
            var slug = Slugify(device.Name);
            var deviceBase = $"{baseTopic}/{slug}";
            var stateTopic = $"{deviceBase}/state";
            var commandTopic = $"{deviceBase}/set";

            stateTopics[device.Id] = stateTopic;
            commandTopics[commandTopic] = device.Id;

            var discoveryTopic = $"homeassistant/number/{config.MachineName}_{slug}/config";
            var discoveryPayload = JsonSerializer.Serialize(new
            {
                name = $"{device.Name} Volume",
                command_topic = commandTopic,
                state_topic = stateTopic,
                min = 0,
                max = 100,
                unique_id = $"{config.MachineName}_{slug}_volume",
                device = new { identifiers = new[] { config.MachineName }, name = config.MachineName }
            });
            var discoveryMessage = new MqttApplicationMessageBuilder()
                .WithTopic(discoveryTopic)
                .WithPayload(discoveryPayload)
                .WithRetainFlag(true)
                .Build();
            await mqttClient.PublishAsync(discoveryMessage);

            await mqttClient.SubscribeAsync(commandTopic);

            await PublishState(device.Id, volume.GetVolume(device.Id));
        }

        mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            if (commandTopics.TryGetValue(e.ApplicationMessage.Topic, out var id))
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                if (float.TryParse(payload, out var vol))
                {
                    volume.SetVolume(id, vol);
                }
            }

            return Task.CompletedTask;
        };

        volume.VolumeChanged += async (_, args) => await PublishState(args.DeviceId, args.Volume);

        using var icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "PC Volume MQTT",
            ContextMenuStrip = new ContextMenuStrip()
        };
        icon.ContextMenuStrip.Items.Add("Settings...", null, (_, _) =>
        {
            PromptForConfig(config);
            SaveConfig(config, configPath);
            Application.Restart();
            Application.Exit();
        });
        icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.Exit());

        Application.ApplicationExit += (_, _) =>
        {
            icon.Visible = false;
            mqttClient.DisconnectAsync().GetAwaiter().GetResult();
            mqttClient.Dispose();
        };

        Application.Run();
    }

    private static void PromptForConfig(AppConfig config)
    {
        config.Mqtt.Host = Prompt("MQTT host:", config.Mqtt.Host);
        config.Mqtt.Port = int.TryParse(Prompt("MQTT port:", config.Mqtt.Port.ToString()), out var port)
            ? port
            : config.Mqtt.Port;
        config.Mqtt.Username = Prompt("MQTT username:", config.Mqtt.Username);
        config.Mqtt.Password = Prompt("MQTT password:", config.Mqtt.Password, true);
        config.MachineName = Prompt("Machine name:", config.MachineName);
    }

    private static void SaveConfig(AppConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string Prompt(string text, string defaultValue = "", bool isPassword = false)
    {
        using var form = new Form
        {
            Width = 400,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "PC Volume MQTT",
            StartPosition = FormStartPosition.CenterScreen
        };

        using var label = new Label { Left = 10, Top = 10, Text = text, AutoSize = true };
        using var textBox = new TextBox { Left = 10, Top = 30, Width = 360, Text = defaultValue, UseSystemPasswordChar = isPassword };
        using var buttonOk = new Button { Text = "OK", Left = 300, Width = 70, Top = 70, DialogResult = DialogResult.OK };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(buttonOk);
        form.AcceptButton = buttonOk;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text : defaultValue;
    }

    private static void EnsureStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        key?.SetValue("PCVolumeMqtt", Application.ExecutablePath);
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c == '-' || c == '_')
            {
                sb.Append('_');
            }
        }

        return sb.ToString();
    }
}

