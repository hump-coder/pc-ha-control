using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using MQTTnet;
using MQTTnet.Client;
using PCVolumeMqtt;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "trace.log");
        Trace.Listeners.Add(new TextWriterTraceListener(logPath));
        Trace.AutoFlush = true;
        Trace.WriteLine("Application starting");

        ApplicationConfiguration.Initialize();

        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var preConfigPath = Path.Combine(AppContext.BaseDirectory, "pre-config.txt");
        Trace.WriteLine($"Config path: {configPath}");
        var configExists = File.Exists(configPath);
        Trace.WriteLine($"Config exists: {configExists}");
        var config = configExists
            ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath)) ?? new AppConfig()
            : new AppConfig();

        if (!configExists && File.Exists(preConfigPath))
        {
            Trace.WriteLine("Loading pre-config");
            var pre = LoadPreConfig(preConfigPath);
            if (pre.TryGetValue("mqtt_host", out var host))
                config.Mqtt.Host = host;
            if (pre.TryGetValue("mqtt_username", out var user))
                config.Mqtt.Username = user;
            if (pre.TryGetValue("mqtt_password", out var pass))
                config.Mqtt.Password = pass;
            if (pre.TryGetValue("machine_name", out var name))
                config.MachineName = name;
        }

        if (configExists && !config.Mqtt.IsPasswordEncrypted && !string.IsNullOrEmpty(config.Mqtt.Password))
        {
            Trace.WriteLine("Encrypting MQTT password and saving config");
            SaveConfig(config, configPath);
        }

        var missing = string.IsNullOrWhiteSpace(config.Mqtt.Host)
                      || string.IsNullOrWhiteSpace(config.Mqtt.Username)
                      || string.IsNullOrWhiteSpace(config.Mqtt.Password)
                      || string.IsNullOrWhiteSpace(config.MachineName);

        if (missing)
        {
            Trace.WriteLine("Config missing values; prompting user");
            PromptForConfig(config);
            SaveConfig(config, configPath);
            if (!configExists)
            {
                Trace.WriteLine("Config saved after prompting");
                MessageBox.Show($"Config saved to {configPath}.");
            }
        }

        if (args.Contains("--uninstall"))
        {
            Installer.UninstallApplication();
            return;
        }

        Installer.RegisterApplication();
        Trace.WriteLine("Starting MQTT connection");

        var mqttFactory = new MqttFactory();
        var mqttClient = mqttFactory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(config.Mqtt.Host, config.Mqtt.Port)
            .WithCredentials(config.Mqtt.Username, config.Mqtt.Password)
            .Build();

        Trace.WriteLine($"Connecting to MQTT at {config.Mqtt.Host}:{config.Mqtt.Port}");
        await mqttClient.ConnectAsync(options);
        Trace.WriteLine("MQTT connected");

        var slug = Slugify(config.MachineName);
        var baseTopic = $"pc/{slug}/volume";

        using var volume = new VolumeService();

        var stateTopic = $"{baseTopic}/state";
        var commandTopic = $"{baseTopic}/set";

        async Task PublishState(float v)
        {
            Trace.WriteLine($"Publishing state {v}");
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
            name = "volume",
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
        Trace.WriteLine($"Subscribed to {commandTopic}");

        await PublishState(volume.GetVolume());

        mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == commandTopic)
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                Trace.WriteLine($"Received command {payload}");
                if (float.TryParse(payload, out var vol))
                {
                    volume.SetVolume(vol);
                    Trace.WriteLine($"Set volume to {vol}");
                }
                else
                {
                    Trace.WriteLine($"Invalid volume payload '{payload}'");
                }
            }

            return Task.CompletedTask;
        };

        volume.VolumeChanged += async (_, args) =>
        {
            Trace.WriteLine($"Volume changed to {args.Volume}");
            await PublishState(args.Volume);
        };

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
            Trace.WriteLine("Application exiting");
            icon.Visible = false;
            mqttClient.DisconnectAsync().GetAwaiter().GetResult();
            mqttClient.Dispose();
            Trace.WriteLine("MQTT disconnected");
        };

        Trace.WriteLine("Application running");
        Application.Run();
    }

    private static void PromptForConfig(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Mqtt.Host))
        {
            config.Mqtt.Host = Prompt("MQTT host:", config.Mqtt.Host);
        }

        if (string.IsNullOrWhiteSpace(config.Mqtt.Username))
        {
            config.Mqtt.Username = Prompt("MQTT username:", config.Mqtt.Username);
        }

        if (string.IsNullOrWhiteSpace(config.Mqtt.Password))
        {
            config.Mqtt.Password = Prompt("MQTT password:", config.Mqtt.Password, true);
        }

        if (string.IsNullOrWhiteSpace(config.MachineName))
        {
            config.MachineName = Prompt("Machine name:", config.MachineName);
        }
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

    private static Dictionary<string, string> LoadPreConfig(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComments(raw).Trim();
            if (string.IsNullOrEmpty(line))
                continue;
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2);
            }
            dict[key] = value;
        }

        return dict;
    }

    private static string StripComments(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == '#' && !inQuotes)
            {
                return line[..i];
            }
        }
        return line;
    }

}

