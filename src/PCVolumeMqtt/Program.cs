using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using MQTTnet;
using MQTTnet.Client;
using Microsoft.Win32;
using PCVolumeMqtt;
using System.Linq;

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

        var slug = Slugify(config.MachineName);
        var baseTopic = $"pc/{slug}/volume";

        using var volume = new VolumeService();

        var device = volume.GetDevice();
        var stateTopic = $"{baseTopic}/state";
        var commandTopic = $"{baseTopic}/set";

        async Task PublishState(float v)
        {
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(stateTopic)
                .WithPayload(((int)v).ToString())
                .WithRetainFlag(true)
                .Build();
            await mqttClient.PublishAsync(msg);
        }

        var objectId = $"{slug}_volume";
        var discoveryTopic = $"homeassistant/number/{objectId}/config";
        var discoveryPayload = JsonSerializer.Serialize(new
        {
            name = $"{device.Name} Volume",
            command_topic = commandTopic,
            state_topic = stateTopic,
            min = 0,
            max = 100,
            unique_id = objectId,
            device = new { identifiers = new[] { slug }, name = config.MachineName }
        });
        var discoveryMessage = new MqttApplicationMessageBuilder()
            .WithTopic(discoveryTopic)
            .WithPayload(discoveryPayload)
            .WithRetainFlag(true)
            .Build();
        await mqttClient.PublishAsync(discoveryMessage);

        await mqttClient.SubscribeAsync(commandTopic);

        await PublishState(volume.GetVolume());

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

        volume.VolumeChanged += async (_, args) => await PublishState(args.Volume);

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

    private static string Slugify(string input)
    {
        return string.Concat(input.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_'));
    }

    private static void EnsureStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        key?.SetValue("PCVolumeMqtt", Application.ExecutablePath);
    }

}

