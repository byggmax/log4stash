using Newtonsoft.Json;

namespace BMX.Infra.log4stash.ElasticClient
{
    internal sealed class PartialElasticResponse
    {
        [JsonProperty("errors")]
        public bool Errors { get; set; }
    }
}