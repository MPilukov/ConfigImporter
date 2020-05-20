using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace ConfigImporter
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var x = new VersionHelper();
                await x.CheckUpdate();

                var getConfig = new Func<string, string>(s => ConfigurationManager.AppSettings[s]);

                var currentPath = Directory.GetCurrentDirectory();

                var assembly = System.Reflection.Assembly.LoadFile($"{currentPath}/ConfigImporterExecuter.dll");
                var type = assembly.GetType("ConfigImporterExecuter.ConfigImporterExecuter");
                var methodInfo = type.GetMethod("Run");
                methodInfo.Invoke(null, new object[] { getConfig });

                var instance = Activator.CreateInstance(type);

                //ConfigImporterExecuter.ConfigImporterExecuter.Run(getConfig);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Ошибка при запуске импорта : {e}");
            }

            Console.ReadLine();
        }
    }
}