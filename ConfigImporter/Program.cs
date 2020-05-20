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
        private static readonly Action<string, LogLevel> Logger = GetLogger();
        private static ConsoleColor GetConsoleColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error:
                    return ConsoleColor.Red;
                case LogLevel.Info:
                    return ConsoleColor.White;
                case LogLevel.Success:
                    return ConsoleColor.Green;
                case LogLevel.Warning:
                    return ConsoleColor.Blue;
                default:
                    return ConsoleColor.White;
            }
        }

        private static void LogToConsole(string message, LogLevel level)
        {
            var color = GetConsoleColor(level);
            if (Console.ForegroundColor != color)
            {
                Console.ForegroundColor = color;
            }

            Console.WriteLine(message);
        }

        private static Action<string, LogLevel> GetLogger()
        {
            return new Action<string, LogLevel>((s, l) =>
            {
                try
                {
                    LogToConsole(s, l);

                    var dirName = "logs";
                    var logFileName = $"{dirName}/log-{DateTime.Today.ToShortDateString()}.txt";
                    var now = DateTime.Now.ToString("HH:mm:ss");

                    if (!Directory.Exists(dirName))
                    {
                        Directory.CreateDirectory(dirName);
                    }

                    File.AppendAllText(logFileName, $"{now} : {s}{Environment.NewLine}");
                }
                catch (Exception exc)
                {
                    LogToConsole(s, l);
                    LogToConsole($"При записи лога произошла ошибка : {exc}", LogLevel.Error);
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
                throw new Exception($"Не указано в конфигах : {keyNameForError} ({key})");
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
                Logger($"Введите текущий мир для импорта ({allowableStagesValue}) : ", LogLevel.Info);

                var stage = Console.ReadLine();
                if (!allowableStages.Any(x => x.Equals(stage)))
                {
                    Logger($"Не найден мир '{stage}' в списке доступимых для импорта : {allowableStagesValue}", LogLevel.Error);
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
            var sdPrefix = GetConfig($"sd.prefix.{currentStage}", $"префикс в консуле для мира : {currentStage}");
            var sdToken = GetConfig($"sd.token.{currentStage}");

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
                Logger($"Запускаем импорт в консул", LogLevel.Info);

                var fileForImportName = GetConfig("fileConfig.base.fileName", "имя файла для импорта");
                var fileForImportExt = GetConfig("fileConfig.base.ext", "расширение файла для импорта");

                var showValues = GetConfig("showValuesToClient", "разрешение демонстрировать значения параметров для импорта")
                    .Equals("true", StringComparison.InvariantCultureIgnoreCase);

                var currentStage = GetCurrentStage();
                var sdConfig = GetSdConfigs(currentStage);

                var valuesForImport = GetValuesForImport(fileForImportName, fileForImportExt, currentStage);

                try
                {
                    ImportToSd(sdConfig, valuesForImport, showValues);
                    Logger($"Успешно импортировали конфиги в консул ({sdConfig.Url}{sdConfig.Prefix})", LogLevel.Success);
                }
                catch (Exception e)
                {
                    throw new Exception($"Произошла ошибка при импортировании конфигов в консул ({sdConfig.Url}{sdConfig.Prefix}) : {e.Message}", e);
                }
            }
            catch (Exception e)
            {
                Logger($"Произошла ошибка при импортировании конфигов в консул : {e}", LogLevel.Error);
            }

            Logger($"Нажмите любую кнопку для завершения", LogLevel.Info);
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
                throw new Exception($"Не найден файл {baseFilePathForImport}");
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
                throw new Exception($"Произошла ошибка при чтении конфигов из файла ({filePath})", e);
            }
        }

        /// <summary>
        /// Отправить в консул набор параметров
        /// </summary>
        /// <param name="sdConfig"></param>
        /// <param name="values"></param>
        private static void ImportToSd(SdConfig sdConfig, Dictionary<string, string> values, bool showValues)
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

                    if (showValues)
                    {
                        Logger($"'{pair.Key}' = '{pair.Value}'", LogLevel.Info);
                    }
                }
            }

            if (showValues)
            {
                Logger("Внимание! Обратите внимание, что вы выбрали конфигурацию, в которой все параметры, импортируемые в консул, сохраняются в логи", 
                    LogLevel.Warning);
                Logger("Если вы переживаете за сохранность этих параметров, то рекомендуется очистить папку логов после завершения работы", 
                    LogLevel.Warning);
            }
        }
    }
}