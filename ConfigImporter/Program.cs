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
            return new Action<string>(s =>
            {
                try
                {
                    var logFileName = $"logs/log-{DateTime.Today.ToShortDateString()}.txt";
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

        /// <summary>
        /// Получить значение ключа из appConfig и выбросить исключение, если его не найдено и задано имя ключа
        /// </summary>
        /// <param name="key">ключ</param>
        /// <param name="keyNameForError">имя ключа для проброса ошибки</param>
        /// <returns></returns>
        private static string GetConfig(string key, string keyNameForError = null)
        {
            var value = ConfigurationManager.AppSettings[key];

            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(keyNameForError))
            {
                throw new Exception($"Не указано : {keyNameForError}");
            }

            return value;
        }

        /// <summary>
        /// Получить имя среды от пользователя (из списка допустимых сред)
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Получить настройки подключения к консулу
        /// </summary>
        /// <param name="currentStage"></param>
        /// <returns></returns>
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

                var fileForImportName = GetConfig("fileConfig.base.fileName", "имя файла для импорта");
                var fileForImportExt = GetConfig("fileConfig.base.ext", "расширение файла для импорта");

                var currentStage = GetCurrentStage();
                var sdConfig = GetSdConfigs(currentStage);

                var valuesForImport = GetValuesForImport(fileForImportName, fileForImportExt, currentStage);

                try
                {
                    ImportToSd(sdConfig, valuesForImport);
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

        /// <summary>
        /// Получить набор параметров для импорта в консул
        /// </summary>
        /// <param name="fileForImportName"></param>
        /// <param name="fileForImportExt"></param>
        /// <param name="currentStage"></param>
        /// <returns></returns>
        private static Dictionary<string, string> GetValuesForImport(string fileForImportName, string fileForImportExt, string currentStage)
        {
            var baseFilePathForImport = $"{fileForImportName}.{fileForImportExt}";
            var stageFilePathForImport = $"{fileForImportName}.{currentStage}.{fileForImportExt}";

            if (!File.Exists(baseFilePathForImport))
            {
                throw new Exception($"Не найден файл {baseFilePathForImport}.");
            }

            var baseValuesForImport = GetValuesFromFile(baseFilePathForImport);
            var stageValuesForImport = GetValuesFromFile(stageFilePathForImport);

            var values = new Dictionary<string, string>();

            // заполняем набор значениями по-умолчанию (для всех сред)
            foreach (var data in baseValuesForImport)
            {
                if (values.ContainsKey(data.Key))
                {
                    continue;
                }

                values.Add(data.Key, data.Value);
            }

            // переопределяем значения по-умолчанию на конкретные для этой среды
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

            return values;
        }

        /// <summary>
        /// Получить словарь значений из файла
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Отправить в консул набор параметров
        /// </summary>
        /// <param name="sdConfig"></param>
        /// <param name="values"></param>
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