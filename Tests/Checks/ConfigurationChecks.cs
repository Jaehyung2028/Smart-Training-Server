using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;

internal static class ConfigurationChecks
{
    public static void Run()
    {
        var defaults = new Dictionary<string, string?>
        {
            ["Auth:NameMinLength"] = "2",
            ["Auth:NameMaxLength"] = "20",
            ["Room:NameMinLength"] = "2",
            ["Room:NameMaxLength"] = "30",
            ["Room:MinUsers"] = "1",
            ["Room:MaxUsers"] = "8"
        };
        void Validate(string? key = null, string? value = null)
        {
            var values = new Dictionary<string, string?>(defaults);
            if (key != null)
            {
                values[key] = value;
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            using var provider = new ServiceCollection().AddTrainingOptions(configuration).BuildServiceProvider();
            provider.GetRequiredService<IStartupValidator>().Validate();
            provider.GetRequiredService<IOptions<AuthOptions>>().Value.ToString();
            provider.GetRequiredService<IOptions<RoomOptions>>().Value.ToString();
        }

        Validate();
        Console.WriteLine("PASS: default training settings validate without Unity executable");
        foreach (var address in new[]
        {
            "",
            "192.168.1.20",
            "training.example.com",
            "::1"
        }

        )
        {
            Validate("Room:AdvertisedAddress", address);
            Console.WriteLine("PASS: valid advertised address " + address);
        }

        foreach (var(key, value)in new[]
        {
            ("Auth:NameMinLength", "0"),
            ("Auth:NameMaxLength", "1"),
            ("Auth:AccessTokenLifetimeMinutes", "0"),
            ("Auth:SessionGraceMinutes", "61"),
            ("Room:MinUsers", "0"),
            ("Room:MaxUsers", "0"),
            ("Room:NameMaxLength", "1"),
            ("Room:ServerReadyTimeoutSeconds", "301"),
            ("Room:ServerExecutablePath", " "),
            ("Room:AdvertisedAddress", "https://localhost"),
            ("Room:AdvertisedAddress", "localhost:7777"),
            ("Room:AdvertisedAddress", " localhost "),
            ("Room:AdvertisedAddress", "0.0.0.0"),
            ("Room:AdvertisedAddress", "::"),
            ("Room:ServerApiBaseUrl", "ftp://localhost"),
            ("Room:ServerApiBaseUrl", "http://user:secret@localhost"),
            ("Room:ServerApiBaseUrl", "http://localhost?token=secret")
        }

        )
        {
            try
            {
                Validate(key, value);
            }
            catch (OptionsValidationException)
            {
                Console.WriteLine("PASS: invalid configuration rejected " + key + "=" + value);
                continue;
            }

            throw new Exception("Invalid configuration accepted: " + key);
        }
    }
}
