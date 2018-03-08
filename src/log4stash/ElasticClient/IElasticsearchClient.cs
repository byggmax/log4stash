using System;
using System.Collections.Generic;
using BMX.Infra.log4stash.Authentication;
using BMX.Infra.log4stash.Configuration;

namespace BMX.Infra.log4stash.ElasticClient
{
    public interface IElasticsearchClient : IDisposable
    {
        ServerDataCollection Servers { get; }
        bool Ssl { get; }
        bool AllowSelfSignedServerCert { get; }
        AuthenticationMethodChooser AuthenticationMethod { get; set; }
        void PutTemplateRaw(string templateName, string rawBody);
        void IndexBulk(IEnumerable<InnerBulkOperation> bulk);
        void IndexBulkAsync(IEnumerable<InnerBulkOperation> bulk);
    }
}