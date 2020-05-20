using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConfigImporter
{
    public class VersionHelper
    {
        public async Task CheckUpdate()
        {
            try
            {
                var currectVesion = GetVersion();
                var lastVersion = await GetLastVersion();

                if (lastVersion == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(currectVesion) 
                    || !currectVesion.Equals(lastVersion.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    await Update(lastVersion);
                }

            }
            catch (Exception exc)
            {

            }
        }

        private async Task Update(TagData data)
        {
            await Update(data);

            SaveVersion(data.Name);
        }

        string _versionFileName = "currentVersion.txt";

        private void SaveVersion(string version)
        {
            File.WriteAllText(_versionFileName, version);
        }

        private string GetVersion()
        {
            if (File.Exists(_versionFileName))
            {
                return File.ReadAllText(_versionFileName);
            }

            return null;
        }

        private async Task<TagData> GetLastVersion()
        {
            var url = "https://api.github.com/repos/MPilukov/ConfigImporter/tags";

            using (var client = new HttpClient())
            {
                var responseBody = await client.GetAsync(url);
                var json = await responseBody.Content.ReadAsStringAsync();
                var values = JsonConvert.DeserializeObject<TagData[]>(json);

                var last = (values ?? new TagData[0]).OrderByDescending(x => x.Name).FirstOrDefault();

                return last;
            };
        }            
    }
}
