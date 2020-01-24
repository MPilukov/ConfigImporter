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
        private static string _sdUrl;
        private static string _sdPrefix;
        private static string _sdToken;
        private static string _fileName;
        private static string _fileExt;
        private static string[] _allowableStages;

        private const string SdUrlKey = "sd.address";
        private const string SdPrefixKey = "sd.prefix";
        private const string SdTokenKey = "sd.token";
        private const string FileNameKey = "fileConfig.base.fileName";
        private const string FileExtKey = "fileConfig.base.ext";
        private const string AllowableStagesKey = "allowableStages";
        private static string _currentStage = "";

        private static void GetFileData()
        {
            _fileName = ConfigurationManager.AppSettings[FileNameKey];
            _fileExt = ConfigurationManager.AppSettings[FileExtKey];
            
            if (string.IsNullOrEmpty(_fileName))
            {
                throw new Exception("Не указано имя файла для импорта.");
            }
            if (string.IsNullOrEmpty(_fileExt))
            {
                throw new Exception("Не указано расширение файла для импорта.");
            }
        }

        private static void GetCurrentStage()
        {
            var allowableStagesValue = ConfigurationManager.AppSettings[AllowableStagesKey];
            _allowableStages = (allowableStagesValue ?? "").Split(',');
            
            if (string.IsNullOrEmpty(allowableStagesValue) || !_allowableStages.Any())
            {
                throw new Exception("Не указаны допустимые миры для импорта.");
            }
            
            while (string.IsNullOrEmpty(_currentStage))
            {
                Console.Write($"Введите текущий мир для импорта ({allowableStagesValue}) :");
                var stage = Console.ReadLine();
                if (!_allowableStages.Any(x => x.Equals(stage)))
                {
                    Console.WriteLine($"Не найден мир {stage} в списке доступимых для импорта : ({allowableStagesValue})");
                }
                else
                {
                    _currentStage = stage;
                }
            }
        }

        private static void GetSdConfigs()
        {
            _sdUrl = ConfigurationManager.AppSettings[$"{SdUrlKey}.{_currentStage}"];
            _sdPrefix = ConfigurationManager.AppSettings[$"{SdPrefixKey}.{_currentStage}"];
            _sdToken = ConfigurationManager.AppSettings[$"{SdTokenKey}.{_currentStage}"];
            
            if (string.IsNullOrEmpty(_sdUrl))
            {
                throw new Exception($"Не указан адрес консула для мира : {_currentStage}.");
            }
            if (string.IsNullOrEmpty(_sdPrefix))
            {
                throw new Exception($"Не указан префикс для консула для мира : {_currentStage}.");
            }
        }

        public static void Main(string[] args)
        {
            try
            {
                Console.WriteLine($"Запускаем импорт в консул.");

                GetFileData();
                GetCurrentStage();
                GetSdConfigs();

                var filePath = $"{_fileName}.{_fileExt}";
                if (!File.Exists(filePath))
                {
                    throw new Exception($"Не найден файл {filePath}.");
                }

                var valuesForImport = GetValuesFromFile(filePath);

                var fileStagePath = $"{_fileName}.{_currentStage}.{_fileExt}";
                var valuesStageForImport = File.Exists(fileStagePath)
                    ? GetValuesFromFile(fileStagePath)
                    : new Dictionary<string, string>();

                InitConfigs(valuesForImport, valuesStageForImport);
                Console.WriteLine($"Успешно импортировали конфиги в консул ({_sdUrl}{_sdPrefix}).");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw new Exception(
                    $"Произошла ошибка при импортировании конфигов в консул ({_sdUrl}{_sdPrefix}) : {e.Message}.", e);
            }

            Console.WriteLine($"Нажмите любую кнопку для завершения.");
            Console.ReadKey();
        }

        private static Dictionary<string, string> GetValuesFromFile(string filePath)
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

            return values;
        }

        private static void InitConfigs(Dictionary<string, string> valuesForImport, IReadOnlyDictionary<string, string> valuesStageForImport)
        {
            var values = new Dictionary<string, string>();
        
            foreach (var data in valuesForImport)
            {
                if (values.ContainsKey(data.Key))
                {
                    continue;
                }

                values.Add(data.Key,
                    valuesStageForImport.TryGetValue(data.Key, out var stageValue) ? stageValue : data.Value);
            }
                
            ImportToSd(values);
        }

        private static void ImportToSd(Dictionary<string, string> values)
        {
            void ConfigurationWithToken(ConsulClientConfiguration config)
            {
                config.Address = new Uri(_sdUrl);
                config.Token = _sdToken;
            }

            void ConfigurationWithoutToken(ConsulClientConfiguration config)
            {
                config.Address = new Uri(_sdUrl);
            }

            using (var client = new ConsulClient(string.IsNullOrWhiteSpace(_sdToken) ? (Action<ConsulClientConfiguration>) ConfigurationWithoutToken : ConfigurationWithToken))
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