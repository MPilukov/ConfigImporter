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
                var currectVesion = GetVersion();
                var lastVersion = await GetLastVersion();

                if (lastVersion == null)
                {
                    return;
                }

                if (lastVersion == null
                    || string.IsNullOrWhiteSpace(currectVesion) 
                    || !currectVesion.Equals(lastVersion.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    trace($"Необходимо обновиться на более свежую версию : {lastVersion.Name}");
                    await Update(lastVersion);
                    trace($"Успешно обновились на более свежую версию : {lastVersion.Name}");
                }

            }
            catch (Exception exc)
            {
                trace($"CheckUpdateException : {exc}");
            }
        }

        private async Task Update(TagData data)
        {
            var dll = "https://github.com/MPilukov/ConfigImporter/raw/master/ConfigImporterExecuter/build/ConfigImporterExecuter.dll";
            var pdb = "https://github.com/MPilukov/ConfigImporter/raw/master/ConfigImporterExecuter/build/ConfigImporterExecuter.pdb";

            using (var webClient = new WebClient())
            {
                webClient.Headers.Add("User-Agent", "user");

                var documentDllBody = await webClient.DownloadDataTaskAsync(dll);
                var documentPdbBody = await webClient.DownloadDataTaskAsync(pdb);

                var fileDll = "ConfigImporterExecuter.dll";
                var filePdb = "ConfigImporterExecuter.pdb";

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

        private void SaveVersion(string version)
        {
            File.WriteAllText(VersionFileName, version);
        }
        public string GetVersion()
        {
            if (File.Exists(VersionFileName))
            {
                return File.ReadAllText(VersionFileName);
            }

            return null;
        }

        private async Task<TagData> GetLastVersion()
        {            
            var url = "https://api.github.com/repos/MPilukov/ConfigImporter/tags";
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
