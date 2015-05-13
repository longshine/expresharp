using System;
using System.Collections.Generic;

namespace Expresharp
{
    class MockRequest : IHttpRequest
    {
        static readonly Uri BASE = new Uri("http://localhost");
        readonly IDictionary<Object, Object> _params = new Dictionary<Object, Object>();
        private String _rawUrl;
        private Uri _url;

        public static MockRequest Get(String url)
        {
            return new MockRequest() { HttpMethod = "GET", RawUrl = url };
        }

        public String HttpMethod { get; set; }

        public String RawUrl
        {
            get { return _rawUrl; }
            set
            {
                _rawUrl = value;
                _url = String.IsNullOrEmpty(value) ? null :
                    (value[0] != '/' ? new Uri(value) : new Uri(BASE, value));
            }
        }

        public Uri Url
        {
            get { return _url; }
            set { _url = value; }
        }

        public String RouteUrl { get; set; }

        public String BaseUrl { get; set; }

        public void Params(Object key, Object value)
        {
            _params[key] = value;
        }

        public Object Params(Object key)
        {
            Object ret;
            return _params.TryGetValue(key, out ret) ? ret : null;
        }

        public T ParamsAs<T>(Object key, T def = default(T))
        {
            Object result = Params(key);
            return result != null ? (T)result : def;
        }

        public static readonly String[] Methods = new String[]
        {
            "get",
            "post",
            "put",
            "head",
            "delete",
            "options",
            "trace",
            "copy",
            "lock",
            "mkcol",
            "move",
            "purge",
            "propfind",
            "proppatch",
            "unlock",
            "report",
            "mkactivity",
            "checkout",
            "merge",
            "m-search",
            "notify",
            "subscribe",
            "unsubscribe",
            "patch",
            "search",
            "connect"
        };

        public string UserHostName
        {
            get { throw new NotImplementedException(); }
        }

        public Version Version
        {
            get { throw new NotImplementedException(); }
        }

        public System.Collections.Specialized.NameValueCollection Headers
        {
            get { throw new NotImplementedException(); }
        }

        public System.IO.Stream InputStream
        {
            get { throw new NotImplementedException(); }
        }

        public System.Net.IPEndPoint RemoteEndPoint
        {
            get { throw new NotImplementedException(); }
        }
    }
}
