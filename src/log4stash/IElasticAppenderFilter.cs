using System.Collections.Generic;
using BMX.Infra.log4stash.ElasticClient;

namespace BMX.Infra.log4stash
{
    public interface IElasticAppenderFilter 
    {
        void PrepareConfiguration(IElasticsearchClient client);
        void PrepareEvent(Dictionary<string, object> logEvent);
    }
}