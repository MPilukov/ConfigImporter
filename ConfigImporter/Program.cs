using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace ConfigImporter
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var x = new VersionHelper();
                await x.CheckUpdate(s=> Console.WriteLine(s + Environment.NewLine));
                var version = VersionHelper.GetVersion();

                StartExecutor(version);
            }
            catch (Exception exc)
            {
                Console.WriteLine($"Error in import : {exc}");
            }
        }

        private static void StartExecutor(string version)
        {
            var getConfig = new Func<string, string>(s => ConfigurationManager.AppSettings[s]);

            var currentPath = Directory.GetCurrentDirectory();

            var assembly = System.Reflection.Assembly.LoadFile($"{currentPath}/ConfigImporterExecutor.dll");
            var type = assembly.GetType("ConfigImporterExecutor.ConfigImporterExecutor");
            var methodInfo = type.GetMethod("Run");
            methodInfo?.Invoke(null, new object[] { getConfig });
        }
    }
}