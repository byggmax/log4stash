﻿using RestSharp;

namespace BMX.Infra.log4stash.Authentication
{
    public class RequestData
    {
        public RestRequest RestRequest { get; set; }

        public string Url { get; set; }

        public string RequestString { get; set; }
    }
}
