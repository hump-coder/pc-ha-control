using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PCVolumeMqtt;

public class AppConfig
{
    public string MachineName { get; set; } = string.Empty;
    public MqttConfig Mqtt { get; set; } = new();

    public class MqttConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public string Username { get; set; } = string.Empty;

        [JsonIgnore]
        public string Password { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsPasswordEncrypted { get; private set; } = true;

        [JsonPropertyName("password")]
        public string? EncryptedPassword
        {
            get => string.IsNullOrEmpty(Password)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Password), null, DataProtectionScope.CurrentUser));
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Password = string.Empty;
                    IsPasswordEncrypted = true;
                    return;
                }

                try
                {
                    var bytes = Convert.FromBase64String(value);
                    var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    Password = Encoding.UTF8.GetString(decrypted);
                    IsPasswordEncrypted = true;
                }
                catch
                {
                    Password = value;
                    IsPasswordEncrypted = false;
                }
            }
        }
    }
}
