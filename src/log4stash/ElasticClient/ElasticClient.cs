﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BMX.Infra.log4stash.Authentication;
using BMX.Infra.log4stash.Configuration;
using log4net.Util;
using Newtonsoft.Json;

namespace BMX.Infra.log4stash.ElasticClient
{
    public abstract class AbstractWebElasticClient : IElasticsearchClient
    {
        public ServerDataCollection Servers { get; private set; }
        public int Timeout { get; private set; }
        public bool Ssl { get; private set; }
        public bool AllowSelfSignedServerCert { get; private set; }
        public AuthenticationMethodChooser AuthenticationMethod { get; set; }
        public string Url { get { return GetServerUrl(); } }

        protected AbstractWebElasticClient(ServerDataCollection servers,
                                           int timeout,
                                           bool ssl,
                                           bool allowSelfSignedServerCert,
                                           AuthenticationMethodChooser authenticationMethod)
        {
            Servers = servers;
            Timeout = timeout;
            ServicePointManager.Expect100Continue = false;

            // SSL related properties
            Ssl = ssl;
            AllowSelfSignedServerCert = allowSelfSignedServerCert;
            AuthenticationMethod = authenticationMethod;
        }

        public abstract void PutTemplateRaw(string templateName, string rawBody);
        public abstract void IndexBulk(IEnumerable<InnerBulkOperation> bulk);
        public abstract IAsyncResult IndexBulkAsync(IEnumerable<InnerBulkOperation> bulk);
        public abstract void Dispose();

        protected string GetServerUrl()
        {
            var serverData = Servers.GetRandomServerData();
            var url = string.Format("{0}://{1}:{2}{3}/", Ssl ? "https" : "http", serverData.Address, serverData.Port, String.IsNullOrEmpty(serverData.Path) ? "" : serverData.Path);
            return url;
        }
    }

    public class WebElasticClient : AbstractWebElasticClient
    {
        private class RequestDetails
        {
            public RequestDetails(WebRequest webRequest, string content)
            {
                WebRequest = webRequest;
                Content = content;
            }

            public WebRequest WebRequest { get; private set; }
            public string Content { get; private set; }
        }

        public WebElasticClient(ServerDataCollection servers, int timeout)
            : this(servers, timeout, false, false, new AuthenticationMethodChooser())
        {
        }

        public WebElasticClient(ServerDataCollection servers,
                                int timeout,
                                bool ssl,
                                bool allowSelfSignedServerCert,
                                AuthenticationMethodChooser authenticationMethod)
            : base(servers, timeout, ssl, allowSelfSignedServerCert, authenticationMethod)
        {
            if (Ssl && AllowSelfSignedServerCert)
            {
                ServicePointManager.ServerCertificateValidationCallback += AcceptSelfSignedServerCertCallback;
            }
        }

        public override void PutTemplateRaw(string templateName, string rawBody)
        {
            var url = string.Concat(Url, "_template/", templateName);
            var webRequest = WebRequest.Create(url);
            webRequest.Timeout = Timeout;
            webRequest.ContentType = "application/json";
            webRequest.Method = "PUT";
            SetHeaders((HttpWebRequest)webRequest, url, rawBody);
            var request = new RequestDetails(webRequest, rawBody);
            request.WebRequest.BeginGetRequestStream(FinishGetRequest, request);
        }

        public override void IndexBulk(IEnumerable<InnerBulkOperation> bulk)
        {
            var request = PrepareRequest(bulk);
            if (SafeSendRequest(request, request.WebRequest.GetRequestStream))
            {
                SafeGetAndCheckResponse(request.WebRequest.GetResponse);
            }
        }

        public override IAsyncResult IndexBulkAsync(IEnumerable<InnerBulkOperation> bulk)
        {
            var request = PrepareRequest(bulk);
            return request.WebRequest.BeginGetRequestStream(FinishGetRequest, request);
        }

        private void FinishGetRequest(IAsyncResult result)
        {
            var request = (RequestDetails)result.AsyncState;
            if (SafeSendRequest(request, () => request.WebRequest.EndGetRequestStream(result)))
            {
                request.WebRequest.BeginGetResponse(FinishGetResponse, request.WebRequest);
            }
        }

        private void FinishGetResponse(IAsyncResult result)
        {
            var webRequest = (WebRequest)result.AsyncState;
            SafeGetAndCheckResponse(() => webRequest.EndGetResponse(result));
        }

        private RequestDetails PrepareRequest(IEnumerable<InnerBulkOperation> bulk)
        {
            var requestString = PrepareBulk(bulk);
            var url = string.Concat(Url, "_bulk");
            var webRequest = WebRequest.Create(url);
            webRequest.ContentType = "application/json";
            webRequest.Method = "POST";
            webRequest.Timeout = Timeout;
            SetHeaders((HttpWebRequest)webRequest, url, requestString);
            return new RequestDetails(webRequest, requestString);
        }

        private static string PrepareBulk(IEnumerable<InnerBulkOperation> bulk)
        {
            var sb = new StringBuilder();
            foreach (InnerBulkOperation operation in bulk)
            {
                AddOperationMetadata(operation, sb);
                AddOperationDocument(operation, sb);
            }
            return sb.ToString();
        }

        private static void AddOperationMetadata(InnerBulkOperation operation, StringBuilder sb)
        {
            var indexParams = new Dictionary<string, string>(operation.IndexOperationParams)
            {
                { "_index", operation.IndexName },
                { "_type", operation.IndexType },
            };
            var paramStrings = indexParams.Where(kv => kv.Value != null)
                .Select(kv => string.Format(@"""{0}"" : ""{1}""", kv.Key, kv.Value));
            var documentMetadata = string.Join(",", paramStrings.ToArray());
            sb.AppendFormat(@"{{ ""index"" : {{ {0} }} }}", documentMetadata);
            sb.Append("\n");
        }

        private static void AddOperationDocument(InnerBulkOperation operation, StringBuilder sb)
        {
            string json = JsonConvert.SerializeObject(operation.Document);
            sb.Append(json);
            sb.Append("\n");
        }

        private bool SafeSendRequest(RequestDetails request, Func<Stream> getRequestStream)
        {
            try
            {
                using (var stream = new StreamWriter(getRequestStream()))
                {
                    stream.Write(request.Content);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogLog.Error(GetType(), "Invalid request to ElasticSearch", ex);
            }
            return false;
        }

        private void SafeGetAndCheckResponse(Func<WebResponse> getResponse)
        {
            try
            {
                using (var httpResponse = (HttpWebResponse)getResponse())
                {
                    CheckResponse(httpResponse);
                }
            }
            catch (Exception ex)
            {
                LogLog.Error(GetType(), "Got error while reading response from ElasticSearch", ex);
            }
        }

        private void SetHeaders(HttpWebRequest webRequest, string url, string requestString)
        {
            var requestData = new RequestData { WebRequest = webRequest, Url = url, RequestString = requestString };

            var authorizationHeaderValue = AuthenticationMethod.CreateAuthenticationHeader(requestData);

            if (!string.IsNullOrEmpty(authorizationHeaderValue))
                webRequest.Headers[HttpRequestHeader.Authorization] = authorizationHeaderValue;
        }

        private bool AcceptSelfSignedServerCertCallback(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            var certificate2 = certificate as X509Certificate2;
            if (certificate2 == null)
                return false;

            string subjectCn = certificate2.GetNameInfo(X509NameType.DnsName, false);
            string issuerCn = certificate2.GetNameInfo(X509NameType.DnsName, true);
            var serverAddresses = Servers.Select(s => s.Address);
            if (sslPolicyErrors == SslPolicyErrors.None
                || (serverAddresses.Contains(subjectCn) && subjectCn != null && subjectCn.Equals(issuerCn)))
            {
                return true;
            }

            return false;
        }

        private static void CheckResponse(HttpWebResponse httpResponse)
        {
            using (var response = httpResponse.GetResponseStream())
            {
                if (response == null)
                {
                    return;
                }

                using (var reader = new StreamReader(response, Encoding.UTF8))
                {
                    var stringResponse = reader.ReadToEnd();
                    var jsonResponse = JsonConvert.DeserializeObject<PartialElasticResponse>(stringResponse);

                    bool responseHasError = jsonResponse.Errors || httpResponse.StatusCode != HttpStatusCode.OK;
                    if (responseHasError)
                    {
                        throw new InvalidOperationException(
                            string.Format("Some error occurred while sending request to Elasticsearch.{0}{1}",
                                Environment.NewLine, stringResponse));
                    }
                }
            }
        }

        public override void Dispose()
        {
            ServicePointManager.ServerCertificateValidationCallback -= AcceptSelfSignedServerCertCallback;
        }
    }
}
