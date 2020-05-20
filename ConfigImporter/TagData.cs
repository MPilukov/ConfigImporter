using System.Runtime.Serialization;

namespace ConfigImporter
{
    [DataContract]
    public class TagData
    {
        [DataMember(Name ="name")]
        public string Name { get; set; }

        [DataMember(Name = "zipball_url")]
        public string ZipUrl { get; set; }
    }
}
