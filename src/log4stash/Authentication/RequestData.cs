using System.Net;

namespace BMX.Infra.log4stash.Authentication
{
    public class RequestData
    {
        public HttpWebRequest WebRequest { get; set; }

        public string Url { get; set; }

        public string RequestString { get; set; }
    }
}
