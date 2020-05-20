using System;
using System.Configuration;
using System.Threading.Tasks;

namespace ConfigImporter
{
    public class Program
    {
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

        public static async Task Main(string[] args)
        {
            try
            {
                var x = new VersionHelper();
                await x.CheckUpdate();

                var getConfig = new Func<string, string>(s => ConfigurationManager.AppSettings[s]);
                ConfigImporterExecuter.ConfigImporterExecuter.Run(getConfig);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Ошибка при запуске импорта : {e}");
            }

            Console.ReadLine();
        }
    }
}