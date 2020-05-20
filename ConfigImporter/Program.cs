using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Consul;
using Newtonsoft.Json;

namespace ConfigImporter
{
    internal class Program
    {
        private static readonly Action<string> Logger = GetLogger();

        private static Action<string> GetLogger()
        {
            var logFileName = $"logs/log-{DateTime.Today.ToShortDateString()}.txt";
            return new Action<string>(s =>
            {
                try
                {
                    Console.WriteLine(s);
                    File.AppendAllText(logFileName, s);
                }
                catch (Exception exc)
                {
                    Console.WriteLine(s);
                    Console.WriteLine($"При записи лога произошла ошибка : {exc}");
                }
            });
        }

        private static string GetConfig(string key, string keyNameForError = null)
        {
            var value = ConfigurationManager.AppSettings[key];

            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(keyNameForError))
            {
                throw new Exception($"Не указано : {keyNameForError}");
            }

            return value;
        }

        private static string GetCurrentStage()
        {
            var allowableStagesValue = GetConfig("allowableStages", "допустимые миры для импорта");
            var allowableStages = allowableStagesValue.Split(',');
            
            if (!allowableStages.Any())
            {
                throw new Exception("Не указаны допустимые миры для импорта");
            }

            var currentStage = "";
            while (string.IsNullOrEmpty(currentStage))
            {
                Logger($"Введите текущий мир для импорта ({allowableStagesValue}) : ");

                var stage = Console.ReadLine();
                if (!allowableStages.Any(x => x.Equals(stage)))
                {
                    Logger($"Не найден мир '{stage}' в списке доступимых для импорта : {allowableStagesValue}.");
                }
                else
                {
                    currentStage = stage;
                }
            }

            return currentStage;
        }

        private static SdConfig GetSdConfigs(string currentStage)
        {
            var sdUrl = GetConfig($"sd.address.{currentStage}", $"адрес консула для мира : {currentStage}");
            var sdPrefix = GetConfig($"sd.prefix{currentStage}", $"префикс для консула для мира : {currentStage}");
            var sdToken = GetConfig("sd.token.{currentStage}");

            return new SdConfig
            {
                Url = sdUrl,
                Prefix = sdPrefix,
                Token = sdToken,
            };
        }

        public static void Main(string[] args)
        {
            try
            {
                Logger($"Запускаем импорт в консул.");

                var fileName = GetConfig("fileConfig.base.fileName", "имя файла для импорта");
                var fileExt = GetConfig("fileConfig.base.ext", "расширение файла для импорта");

                var currentStage = GetCurrentStage();
                var sdConfig = GetSdConfigs(currentStage);

                var filePath = $"{fileName}.{fileExt}";
                var fileStagePath = $"{fileName}.{currentStage}.{fileExt}";

                if (!File.Exists(filePath))
                {
                    throw new Exception($"Не найден файл {filePath}.");
                }

                var baseValuesForImport = GetValuesFromFile(filePath);
                var stageValuesForImport = GetValuesFromFile(fileStagePath);

                try
                {
                    InitConfigs(sdConfig, baseValuesForImport, stageValuesForImport);
                    Logger($"Успешно импортировали конфиги в консул ({sdConfig.Url}{sdConfig.Prefix}).");
                }
                catch (Exception e)
                {
                    throw new Exception($"Произошла ошибка при импортировании конфигов в консул ({sdConfig.Url}{sdConfig.Prefix}) : {e.Message}.", e);
                }
            }
            catch (Exception e)
            {
                Logger($"Произошла ошибка при импортировании конфигов в консул : {e}.");
            }

            Logger($"Нажмите любую кнопку для завершения.");
            Console.ReadLine();
        }

        private static Dictionary<string, string> GetValuesFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new Dictionary<string, string>();
                }

                var fileText = File.ReadAllText(filePath);
                var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(fileText);
                return values;
            }
            catch (Exception e)
            {
                throw new Exception($"Произошла ошибка при чтении конфигов из файла ({filePath}).", e);
            }
        }

        private static void InitConfigs(SdConfig sdConfig, 
            Dictionary<string, string> baseValuesForImport, IReadOnlyDictionary<string, string> stageValuesForImport)
        {
            var values = new Dictionary<string, string>();
        
            foreach (var data in baseValuesForImport)
            {
                if (values.ContainsKey(data.Key))
                {
                    continue;
                }

                values.Add(data.Key, data.Value);
            }

            foreach (var data in stageValuesForImport)
            {
                if (values.ContainsKey(data.Key))
                {
                    values[data.Key] = data.Value;
                }
                else
                {
                    values.Add(data.Key, data.Value);
                }
            }
                
            ImportToSd(sdConfig, values);
        }

        private static void ImportToSd(SdConfig sdConfig, Dictionary<string, string> values)
        {
            void ConfigurationWithToken(ConsulClientConfiguration config)
            {
                config.Address = new Uri(sdConfig.Url);
                config.Token = sdConfig.Token;
            }

            void ConfigurationWithoutToken(ConsulClientConfiguration config)
            {
                config.Address = new Uri(sdConfig.Url);
            }

            using (var client = new ConsulClient(string.IsNullOrWhiteSpace(sdConfig.Token) 
                ? (Action<ConsulClientConfiguration>) ConfigurationWithoutToken 
                : ConfigurationWithToken))
            {
                foreach (var pair in values)
                {
                    var kv = client.KV;
                    var p = new KVPair(sdConfig.Prefix + "/" + pair.Key) {Value = Encoding.UTF8.GetBytes(pair.Value)};
                    var ct = new CancellationToken();
                    kv.Put(p, ct).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
        }
    }
}