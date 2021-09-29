using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConfigImporter
{
    public class VersionHelper
    {
        public async Task CheckUpdate(Action<string> trace)
        {
            try
            {
                var currentVersion = GetVersion();
                var lastVersion = await GetLastVersion();

                if (lastVersion == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(currentVersion) ||
                    !currentVersion.Equals(lastVersion.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    trace($"You need to upgrade to a more recent version : {lastVersion.Name}");
                    await Update(lastVersion);
                    trace($"Successfully upgraded to a more recent version : {lastVersion.Name}");
                }

            }
            catch (Exception exc)
            {
                trace($"CheckUpdateException : {exc}");
            }
        }

        private static async Task Update(TagData data)
        {
            const string dll = "https://github.com/MPilukov/ConfigImporter/raw/master/ConfigImporterExecutor/build/ConfigImporterExecutor.dll";
            const string pdb = "https://github.com/MPilukov/ConfigImporter/raw/master/ConfigImporterExecutor/build/ConfigImporterExecutor.pdb";

            using (var webClient = new WebClient())
            {
                webClient.Headers.Add("User-Agent", "user");

                var documentDllBody = await webClient.DownloadDataTaskAsync(dll);
                var documentPdbBody = await webClient.DownloadDataTaskAsync(pdb);

                const string fileDll = "ConfigImporterExecutor.dll";
                const string filePdb = "ConfigImporterExecutor.pdb";

                var currentDir = Directory.GetCurrentDirectory();

                using (var fs = File.Create($"{currentDir}/{fileDll}"))
                {
                    fs.Write(documentDllBody, 0, documentDllBody.Length);
                }

                using (var fs = File.Create($"{currentDir}/{filePdb}"))
                {
                    fs.Write(documentPdbBody, 0, documentPdbBody.Length);
                }
            }

            SaveVersion(data.Name);
        }

        private const string VersionFileName = "currentVersion.txt";

        private static void SaveVersion(string version)
        {
            File.WriteAllText(VersionFileName, version);
        }

        public static string GetVersion()
        {
            if (File.Exists(VersionFileName))
            {
                return File.ReadAllText(VersionFileName);
            }

            return null;
        }

        private static async Task<TagData> GetLastVersion()
        {            
            const string url = "https://api.github.com/repos/MPilukov/ConfigImporter/tags";
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "user");

                var responseBody = await client.GetAsync(url);
                var json = await responseBody.Content.ReadAsStringAsync();
                var values = JsonConvert.DeserializeObject<TagData[]>(json);

                var last = (values ?? new TagData[0]).OrderByDescending(x => x.Name).FirstOrDefault();

                return last;
            };
        }            
    }
}
