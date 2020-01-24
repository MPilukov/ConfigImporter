using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;
using Consul;
using Newtonsoft.Json;

namespace ConfigImporter
{
    internal class Program
    {
        private static string _sdUrl;
        private static string _sdPrefix;
        private static string _sdToken;

        private const string SdUrlKey = "sd.address";
        private const string SdPrefixKey = "sd.prefix";
        private const string SdTokenKey = "sd.token";
        private const string FilePathKey = "fileConfig";

        public static void Main(string[] args)
        {
            Console.WriteLine($"Запускаем импорт в консул.");
            
            _sdUrl = ConfigurationManager.AppSettings[SdUrlKey];
            _sdPrefix = ConfigurationManager.AppSettings[SdPrefixKey];
            _sdToken = ConfigurationManager.AppSettings[SdTokenKey];
            var filePath = ConfigurationManager.AppSettings[FilePathKey];

            if (string.IsNullOrEmpty(_sdUrl))
            {
                throw new Exception("Не указан адрес консула в AppConfig.");
            }
            if (string.IsNullOrEmpty(_sdPrefix))
            {
                throw new Exception("Не указан префикс для консула в AppConfig.");
            }

            if (!File.Exists(filePath))
            {
                throw new Exception($"Не найден файл {filePath}.");
            }

            try
            {
                InitConfigs(filePath);
                Console.WriteLine($"Успешно импортировали конфиги в консул ({_sdUrl}).");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw new Exception($"Произошла ошибка при импортировании конфигов в консул ({_sdUrl}) : {e.Message}.", e);
            }

            Console.WriteLine($"Нажмите любую кнопку для завершения.");
            Console.ReadKey();
        }

        private static void InitConfigs(string filePath)
        {
            Dictionary<string, string> values;

            try
            {
                var fileText = File.ReadAllText(filePath);
                values = JsonConvert.DeserializeObject<Dictionary<string, string>>(fileText);
            }
            catch (Exception e)
            {
                throw new Exception($"Произошла ошибка при чтении конфигов из файла ({filePath}).", e);
            }
                
            ImportToSd(values);
        }

        private static void ImportToSd(Dictionary<string, string> values)
        {
            Action<ConsulClientConfiguration> configurationWithToken = config =>
            {
                config.Address = new Uri(_sdUrl);
                config.Token = _sdToken;
            };
            Action<ConsulClientConfiguration> configurationWithoutToken = config =>
            {
                config.Address = new Uri(_sdUrl);
            };
            
            using (var client = new ConsulClient(string.IsNullOrWhiteSpace(_sdToken) ? configurationWithoutToken : configurationWithToken))
            {
                foreach (var pair in values)
                {
                    var kv = client.KV;
                    var p = new KVPair(_sdPrefix + "/" + pair.Key) {Value = Encoding.UTF8.GetBytes(pair.Value)};
                    var ct = new CancellationToken();
                    kv.Put(p, ct).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
        }
    }
}