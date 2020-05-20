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
        private static Action<string> Logger = GetLogger();

        private static Action<string> GetLogger()
        {
            var logFileName = $"logs/log-{DateTime.Today.ToShortDateString()}.txt";
            return new Action<string>(s => File.AppendAllText(logFileName, ""));
        }

        private static string GetFileName()
        {
            var fileName = ConfigurationManager.AppSettings["fileConfig.base.fileName"];

            if (string.IsNullOrEmpty(fileName))
            {
                throw new Exception("Не указано имя файла для импорта");
            }

            return fileName;
        }

        private static string GetFileExt()
        {
            var fileExt = ConfigurationManager.AppSettings["fileConfig.base.ext"];

            if (string.IsNullOrEmpty(fileExt))
            {
                throw new Exception("Не указано расширение файла для импорта");
            }

            return fileExt;
        }

        private static string GetCurrentStage()
        {
            var allowableStagesValue = ConfigurationManager.AppSettings["allowableStages"];
            var allowableStages = (allowableStagesValue ?? "").Split(',');
            
            if (string.IsNullOrEmpty(allowableStagesValue) || !allowableStages.Any())
            {
                throw new Exception("Не указаны допустимые миры для импорта");
            }

            var currentStage = "";
            while (string.IsNullOrEmpty(currentStage))
            {
                Console.Write($"Введите текущий мир для импорта ({allowableStagesValue}) : ");
                var stage = Console.ReadLine();
                if (!allowableStages.Any(x => x.Equals(stage)))
                {
                    Console.WriteLine($"Не найден мир '{stage}' в списке доступимых для импорта : {allowableStagesValue}.");
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
            var sdUrl = ConfigurationManager.AppSettings[$"sd.address.{currentStage}"];
            var sdPrefix = ConfigurationManager.AppSettings[$"sd.prefix{currentStage}"];
            var sdToken = ConfigurationManager.AppSettings[$"sd.token.{currentStage}"];
            
            if (string.IsNullOrEmpty(sdUrl))
            {
                throw new Exception($"Не указан адрес консула для мира : {currentStage}");
            }
            if (string.IsNullOrEmpty(sdPrefix))
            {
                throw new Exception($"Не указан префикс для консула для мира : {currentStage}");
            }

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
                var logger = GetLogger();
                Console.WriteLine($"Запускаем импорт в консул.");

                var fileName = GetFileName();
                var fileExt = GetFileExt();

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
                    Console.WriteLine($"Успешно импортировали конфиги в консул ({sdConfig.Url}{sdConfig.Prefix}).");
                }
                catch (Exception e)
                {
                    throw new Exception($"Произошла ошибка при импортировании конфигов в консул ({sdConfig.Url}{sdConfig.Prefix}) : {e.Message}.", e);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Произошла ошибка при импортировании конфигов в консул : {e.Message}.", e);
            }

            Console.WriteLine($"Нажмите любую кнопку для завершения.");
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