using System;

namespace Expresharp
{
    class MockResponse : IHttpResponse
    {
        public Action<String> SendHandler { get; set; }

        public Int32 StatusCode { get; set; }

        public String StatusDescription { get; set; }

        public IHttpResponse Status(Int32 status)
        {
            throw new NotImplementedException();
        }

        public void Send(String body)
        {
            if (SendHandler != null)
                SendHandler(body);
        }

        public void End()
        {
            // do nothing
        }

        public IHttpResponse SetHeader(String key, String val)
        {
            throw new NotImplementedException();
        }

        public System.IO.Stream OutputStream
        {
            get { throw new NotImplementedException(); }
        }

        public void Redirect(string url)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            // do nothing
        }
    }
}
