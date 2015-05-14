using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

namespace Expresharp
{
    /// <summary>
    /// Next handler.
    /// </summary>
    /// <param name="o">'route', exception, or null</param>
    public delegate void Next(Object o = null);
    /// <summary>
    /// Middleware handler.
    /// </summary>
    /// <param name="req"><see cref="IHttpRequest"/></param>
    /// <param name="res"><see cref="IHttpResponse"/></param>
    /// <param name="next"><see cref="Next"/></param>
    public delegate void Middleware(IHttpRequest req, IHttpResponse res, Next next);
    /// <summary>
    /// Middleware exception handler.
    /// </summary>
    /// <param name="error">the error occurred</param>
    /// <param name="req"><see cref="IHttpRequest"/></param>
    /// <param name="res"><see cref="IHttpResponse"/></param>
    /// <param name="next"><see cref="Next"/></param>
    public delegate void MiddlewareEx(Exception error, IHttpRequest req, IHttpResponse res, Next next);
    /// <summary>
    /// Pamareter handler for <see cref="Express.Param(String, ParamCallback)"/>.
    /// </summary>
    /// <param name="req"><see cref="IHttpRequest"/></param>
    /// <param name="res"><see cref="IHttpResponse"/></param>
    /// <param name="next"><see cref="Next"/></param>
    /// <param name="val">the value of the param</param>
    /// <param name="name">the name of the param</param>
    public delegate void ParamCallback(IHttpRequest req, IHttpResponse res, Next next, Object val, String name);

    /// <summary>
    /// Express application.
    /// </summary>
    public class Express : IMiddleware
    {
        IDictionary<Object, Object> _settings = new Dictionary<Object, Object>();
        Lazy<Router> _router;
        String _mountPath;
        Express _parent;

        /// <summary>
        /// Occurs when this app is mounted on a parent app.
        /// </summary>
        public event EventHandler<MountEventArgs> Mount;

        /// <summary>
        /// Initializes.
        /// </summary>
        public Express()
        {
            _router = new Lazy<Router>(CreateRouter);

            // default settings
            Enable(Settings.XPoweredBy);
        }

        /// <summary>
        /// Gets the parent app on which this app was mounted.
        /// </summary>
        public Express Parent
        {
            get { return _parent; }
        }

        /// <summary>
        /// Gets the path pattern(s) on which this app was mounted.
        /// </summary>
        public String MountPath
        {
            get { return _mountPath; }
        }

        /// <summary>
        /// Gets the app's absolute pathname based on the parent(s)
        /// that have mounted it.
        /// </summary>
        public String Path
        {
            get { return _parent == null ? String.Empty : (_parent.Path + _mountPath); }
        }

        #region Settings

        /// <summary>
        /// Assigns <paramref name="setting"/> to <paramref name="val"/>.
        /// </summary>
        /// <param name="setting">the setting name</param>
        /// <param name="val">the value</param>
        /// <returns>self for chaining</returns>
        public Express Set(String setting, Object val)
        {
            _settings[setting] = val;
            return this;
        }

        /// <summary>
        /// Gets the value of a <paramref name="setting"/>.
        /// </summary>
        /// <param name="setting">the setting name</param>
        /// <returns>the setting value, or null if not exists</returns>
        public Object Get(String setting)
        {
            Object ret;
            return _settings.TryGetValue(setting, out ret) ? ret : null;
        }

        /// <summary>
        /// Enables <paramref name="setting"/>.
        /// </summary>
        /// <param name="setting">the setting name</param>
        /// <returns>self for chaining</returns>
        public Express Enable(String setting)
        {
            return Set(setting, true);
        }

        /// <summary>
        /// Disables <paramref name="setting"/>.
        /// </summary>
        /// <param name="setting">the setting name</param>
        /// <returns>self for chaining</returns>
        public Express Disable(String setting)
        {
            return Set(setting, false);
        }

        /// <summary>
        /// Checks if <paramref name="setting"/> is enabled.
        /// </summary>
        /// <param name="setting">the setting name</param>
        /// <returns><code>true</code> if enabled, or <code>false</code> if not</returns>
        public Boolean Enabled(String setting)
        {
            Object val = Get(setting);
            return val != null && Object.Equals(val, true);
        }

        /// <summary>
        /// Checks if <paramref name="setting"/> is disabled.
        /// </summary>
        /// <param name="setting">the setting name</param>
        /// <returns><code>true</code> if disabled, or <code>false</code> if not</returns>
        public Boolean Disabled(String setting)
        {
            Object val = Get(setting);
            return val == null || Object.Equals(val, false);
        }

        /// <summary>
        /// Application Settings
        /// </summary>
        public static class Settings
        {
            /// <summary>
            /// Case sensitive routing.
            /// </summary>
            public static readonly String CaseSensitiveRouting = "case sensitive routing";
            /// <summary>
            /// Strict routing.
            /// </summary>
            public static readonly String StrictRouting = "strict routing";
            /// <summary>
            /// x-powered-by.
            /// </summary>
            public static readonly String XPoweredBy = "x-powered-by";
        }

        #endregion

        /// <summary>
        /// Proxy to <see cref="Router.Param(String, ParamCallback)"/>.
        /// </summary>
        /// <seealso cref="Router.Param(String, ParamCallback)"/>
        /// <param name="name">the param name</param>
        /// <param name="callback">callback</param>
        /// <returns>self for chaining</returns>
        public Express Param(String name, ParamCallback callback)
        {
            _router.Value.Param(name, callback);
            return this;
        }

        /// <summary>
        /// Proxy to <see cref="Router.Param(String, ParamCallback)"/>.
        /// </summary>
        /// <seealso cref="Router.Param(String, ParamCallback)"/>
        /// <param name="names">the param names</param>
        /// <param name="callback">callback</param>
        /// <returns>self for chaining</returns>
        public Express Param(IEnumerable<String> names, ParamCallback callback)
        {
            foreach (var name in names)
            {
                _router.Value.Param(name, callback);
            }
            return this;
        }

        #region HTTP Methods

        /// <summary>
        /// Routes all HTTP requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express All(String path, params Middleware[] mws)
        {
            RouteFor(path).All(mws);
            return this;
        }

        /// <summary>
        /// Routes all HTTP requests to the specified path with the specified callback exception functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the exception handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express All(String path, params MiddlewareEx[] mws)
        {
            RouteFor(path).All(mws);
            return this;
        }

        /// <summary>
        /// Routes the given HTTP <paramref name="method"/> on <paramref name="path"/> with the specified callback function.
        /// </summary>
        /// <param name="method">the method to handle</param>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public Express On(String method, String path, Action<IHttpRequest, IHttpResponse> action)
        {
            RouteFor(path).On(method, action);
            return this;
        }

        /// <summary>
        /// Routes the given HTTP <paramref name="method"/> on <paramref name="path"/> with the specified callback functions.
        /// </summary>
        /// <param name="method">the method to handle</param>
        /// <param name="path">the route path</param>
        /// <param name="mws">the handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express On(String method, String path, params Middleware[] mws)
        {
            RouteFor(path).On(method, mws);
            return this;
        }

        /// <summary>
        /// Routes the given HTTP <paramref name="method"/> on <paramref name="path"/> with the specified callback exception functions.
        /// </summary>
        /// <param name="method">the method to handle</param>
        /// <param name="path">the route path</param>
        /// <param name="mws">the exception handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express On(String method, String path, params MiddlewareEx[] mws)
        {
            RouteFor(path).On(method, mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP GET requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public Express Get(String path, Action<IHttpRequest, IHttpResponse> action)
        {
            RouteFor(path).Get(action);
            return this;
        }

        /// <summary>
        /// Routes HTTP GET requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Get(String path, params Middleware[] mws)
        {
            RouteFor(path).Get(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP GET requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the exception handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Get(String path, params MiddlewareEx[] mws)
        {
            RouteFor(path).Get(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP POST requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public Express Post(String path, Action<IHttpRequest, IHttpResponse> action)
        {
            RouteFor(path).Post(action);
            return this;
        }

        /// <summary>
        /// Routes HTTP POST requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Post(String path, params Middleware[] mws)
        {
            RouteFor(path).Post(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP POST requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the exception handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Post(String path, params MiddlewareEx[] mws)
        {
            RouteFor(path).Post(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP PUT requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public Express Put(String path, Action<IHttpRequest, IHttpResponse> action)
        {
            RouteFor(path).Put(action);
            return this;
        }

        /// <summary>
        /// Routes HTTP PUT requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Put(String path, params Middleware[] mws)
        {
            RouteFor(path).Put(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP PUT requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the exception handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Put(String path, params MiddlewareEx[] mws)
        {
            RouteFor(path).Put(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP DELETE requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public Express Delete(String path, Action<IHttpRequest, IHttpResponse> action)
        {
            RouteFor(path).Delete(action);
            return this;
        }

        /// <summary>
        /// Routes HTTP DELETE requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Delete(String path, params Middleware[] mws)
        {
            RouteFor(path).Delete(mws);
            return this;
        }

        /// <summary>
        /// Routes HTTP DELETE requests to the specified path with the specified callback functions.
        /// </summary>
        /// <param name="path">the route path</param>
        /// <param name="mws">the exception handler(s)</param>
        /// <returns>self for chaining</returns>
        public Express Delete(String path, params MiddlewareEx[] mws)
        {
            RouteFor(path).Delete(mws);
            return this;
        }

        #endregion

        /// <summary>
        /// Returns a new <see cref="Route"/> instance for the <paramref name="path"/>.
        /// </summary>
        /// <param name="path">the path</param>
        /// <returns>the new <see cref="Route"/></returns>
        public Route RouteFor(String path)
        {
            var router = _router.Value;
            return router.Route(path);
        }

        /// <summary>
        /// Proxy <see cref="Router.Use(Middleware[])"/> to add middleware to the app router.
        /// </summary>
        /// <seealso cref="Router.Use(Middleware[])"/>
        /// <returns>self for chaining</returns>
        public Express Use(params Middleware[] mws)
        {
            _router.Value.Use(mws);
            return this;
        }

        /// <summary>
        /// Proxy <see cref="Router.Use(String, Middleware[])"/> to add middleware to the app router.
        /// </summary>
        /// <seealso cref="Router.Use(String, Middleware[])"/>
        /// <returns>self for chaining</returns>
        public Express Use(String path, params Middleware[] mws)
        {
            _router.Value.Use(path, mws);
            return this;
        }

        /// <summary>
        /// Proxy <see cref="Router.Use(MiddlewareEx[])"/> to add middleware to the app router.
        /// </summary>
        /// <seealso cref="Router.Use(MiddlewareEx[])"/>
        /// <returns>self for chaining</returns>
        public Express Use(params MiddlewareEx[] mws)
        {
            _router.Value.Use(mws);
            return this;
        }

        /// <summary>
        /// Proxy <see cref="Router.Use(String, MiddlewareEx[])"/> to add middleware to the app router.
        /// </summary>
        /// <seealso cref="Router.Use(String, MiddlewareEx[])"/>
        /// <returns>self for chaining</returns>
        public Express Use(String path, params MiddlewareEx[] mws)
        {
            _router.Value.Use(path, mws);
            return this;
        }

        /// <summary>
        /// Proxy <see cref="Router.Use(IMiddleware[])"/> to add middleware to the app router.
        /// </summary>
        /// <seealso cref="Router.Use(IMiddleware[])"/>
        /// <returns>self for chaining</returns>
        public Express Use(params IMiddleware[] mws)
        {
            return Use("/", mws);
        }

        /// <summary>
        /// Proxy <see cref="Router.Use(String, IMiddleware[])"/> to add middleware to the app router.
        /// </summary>
        /// <seealso cref="Router.Use(String, IMiddleware[])"/>
        /// <returns>self for chaining</returns>
        public Express Use(String path, params IMiddleware[] mws)
        {
            foreach (var mw in mws)
            {
                _router.Value.Use(path, mw);

                Express exp = mw as Express;
                if (exp != null)
                {
                    exp._mountPath = path;
                    exp._parent = this;
                    exp.FireMount(this);
                }
            }
            return this;
        }

        /// <summary>
        /// Dispatches a req, res pair into the application. Starts pipeline processing.
        /// </summary>
        /// <param name="req">the <see cref="IHttpRequest"/></param>
        /// <param name="res">the <see cref="IHttpResponse"/></param>
        /// <param name="done">the final filter</param>
        public void Handle(IHttpRequest req, IHttpResponse res, Next done)
        {
            if (done == null)
            {
                done = o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                    {
                        res.StatusCode = 500;
                    }
                    else
                    {
                        res.StatusCode = 404;
                    }

                    res.End();
                };
            }

            if (_router.IsValueCreated)
            {
                _router.Value.Handle(req, res, done);
            }
            else
            {
                done();
            }
        }

        /// <summary>
        /// Dispatches a <see cref="HttpListenerContext"/> into the application.
        /// </summary>
        /// <param name="ctx"><see cref="HttpListenerContext"/></param>
        public void Handle(HttpListenerContext ctx)
        {
            Handle(new HttpListenerRequestWrapper(ctx.Request),
                new HttpListenerResponseWrapper(ctx.Response), null);
        }

        /// <summary>
        /// Binds and listens for connections on the specified host and port.
        /// </summary>
        /// <param name="port"></param>
        /// <param name="host"></param>
        /// <returns>the <see cref="HttpListener"/> created</returns>
        public HttpListener Listen(Int32 port, String host = null)
        {
            WindowsPrincipal pricipal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (host == null)
                host= pricipal.IsInRole(WindowsBuiltInRole.Administrator) ? "*" : "localhost";
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(String.Format("http://{0}:{1}/", host, port));
            Listen(listener);
            return listener;
        }

        /// <summary>
        /// Listens for connections on the specified <paramref name="httpListener"/>.
        /// </summary>
        /// <param name="httpListener">the <see cref="HttpListener"/> to mount this app on</param>
        public void Listen(HttpListener httpListener)
        {
            if (!httpListener.IsListening)
                httpListener.Start();

            ThreadPool.QueueUserWorkItem(o => ListenSync(o as HttpListener), httpListener);
        }

        /// <summary>
        /// Listens for connections on the specified <paramref name="httpListener"/>, synchronously.
        /// </summary>
        /// <param name="httpListener">the <see cref="HttpListener"/> to mount this app on</param>
        public void ListenSync(HttpListener httpListener)
        {
            while (httpListener.IsListening)
            {
                var ctx = httpListener.GetContext();
                ThreadPool.QueueUserWorkItem(o => Handle((HttpListenerContext)o), ctx);
            }
        }

        private void FireMount(Express parent)
        {
            var h = Mount;
            if (h != null)
                h(this, new MountEventArgs(parent));
        }

        private Router CreateRouter()
        {
            Router rt = new Router(new Option()
            {
                CaseSensitive = Enabled(Settings.CaseSensitiveRouting),
                Strict = Enabled(Settings.StrictRouting)
            });
            rt.Use(Init);
            return rt;
        }

        private void Init(IHttpRequest req, IHttpResponse res, Next next)
        {
            if (Enabled(Settings.XPoweredBy))
                res.SetHeader("X-Powered-By", "Express");
            req.Params("_next", next);
            next();
        }

        #region Router

        /// <summary>
        /// Options for routing.
        /// </summary>
        public class Option
        {
            /// <summary>
            /// Default options.
            /// </summary>
            public static readonly Option Default = new Option();

            /// <summary>
            /// Enable case sensitivity of paths.
            /// Default <code>false</code>.
            /// </summary>
            public Boolean CaseSensitive { get; set; }
            /// <summary>
            /// Not in use yet.
            /// </summary>
            public Boolean MergeParams { get; set; }
            /// <summary>
            /// Enable strict routing.
            /// Default <code>false</code>.
            /// </summary>
            public Boolean Strict { get; set; }
            internal Boolean? End { get; set; }
        }

        /// <summary>
        /// The router.
        /// </summary>
        public class Router : IMiddleware
        {
            readonly Option _options;
            readonly IList<Layer> _stack = new List<Layer>();
            readonly IDictionary<String, IList<ParamCallback>> _params = new Dictionary<String, IList<ParamCallback>>();

            /// <summary>
            /// Initializes with default options.
            /// </summary>
            public Router()
                : this(new Option())
            { }
            
            /// <summary>
            /// Initializes with the given <paramref name="options"/>.
            /// </summary>
            /// <param name="options"><see cref="Option"/></param>
            public Router(Option options)
            {
                _options = options;
            }

            /// <summary>
            /// Map the given param placeholder <paramref name="name"/> to the given <paramref name="callback"/>.
            /// </summary>
            /// <param name="name">the param name</param>
            /// <param name="callback">callback</param>
            /// <returns>self for chaining</returns>
            public Router Param(String name, ParamCallback callback)
            {
                IList<ParamCallback> list;
                if (!_params.TryGetValue(name, out list))
                    _params[name] = list = new List<ParamCallback>();
                list.Add(callback);
                return this;
            }

            /// <summary>
            /// Dispatches req, res into this router.
            /// </summary>
            /// <param name="req"><see cref="IHttpRequest"/></param>
            /// <param name="res"><see cref="IHttpResponse"/></param>
            /// <param name="done">the final filter</param>
            public void Handle(IHttpRequest req, IHttpResponse res, Next done)
            {
                var idx = 0;
                var stack = _stack;

                // store options for OPTIONS request
                // only used if OPTIONS request
                var options = new List<String>();

                var url = req.RouteUrl;
                if (url == null)
                    // we only process path segment here
                    url = req.RouteUrl = req.Url != null ? req.Url.AbsolutePath : String.Empty;

                var search = 1 + url.IndexOf('?');
                var pathlength = search > 0 ? search - 1 : url.Length;
                var fqdn = !String.IsNullOrEmpty(url) && url[0] != '/' ? 1 + url.Substring(0, pathlength).IndexOf("://") : 0;
                var protohost = fqdn > 0 ? url.Substring(0, url.IndexOf('/', 2 + fqdn)) : String.Empty;
                var removed = String.Empty;
                var slashAdded = false;

                // manage inter-router variables
                var parentUrl = req.BaseUrl ?? String.Empty;
                done = Restore(done, req, "_next");

                var paramCalled = new Dictionary<String, ParamContext>();

                Action<Layer, Object, String, String> trimPrefix = null;
                Next next = null;

                next = o =>
                {
                    var layerError = Object.Equals(o, "route") ? null : o;

                    // remove added slash
                    if (slashAdded)
                    {
                        req.RouteUrl = req.RouteUrl.Substring(1);
                        slashAdded = false;
                    }

                    // restore altered req.url
                    if (removed.Length != 0)
                    {
                        req.BaseUrl = parentUrl;
                        req.RouteUrl = protohost + removed + req.RouteUrl.Substring(protohost.Length); 
                        removed = String.Empty;
                    }

                    // no more matching layers
                    if (idx >= stack.Count)
                    {
                        // setImmediate?
                        done(layerError);
                        return;
                    }

                    // get pathname of request
                    var path = GetPathName(req.RouteUrl);

                    if (path == null)
                    {
                        done(layerError);
                        return;
                    }

                    // find next matching layer
                    Layer layer = null;
                    RouteData rd = null;
                    Route route = null;

                    while (rd == null && idx < stack.Count)
                    {
                        layer = stack[idx++];
                        route = layer.Route;

                        try
                        {
                            rd = MatchLayer(layer, path);
                        }
                        catch (Exception ex)
                        {
                            // hold on to layerError
                            if (layerError == null)
                                layerError = ex;
                        }

                        if (rd == null)
                            continue;

                        if (route == null)
                            // process non-route handlers normally
                            continue;

                        if (layerError != null)
                        {
                            // routes do not match with a pending error
                            rd = null;
                            continue;
                        }

                        var method = req.HttpMethod;
                        var hasMethod = route.HandlesMethod(method);

                        // build up automatic options response
                        if (!hasMethod && method == "OPTIONS")
                        {
                            AppendMethods(options, route.Options());
                        }

                        // don't even bother matching route
                        if (!hasMethod && method != "HEAD")
                        {
                            rd = null;
                            continue;
                        }
                    }

                    if (rd == null)
                    {
                        // no match
                        done(layerError);
                        return;
                    }

                    // store route for dispatch on change
                    if (route != null)
                        req.Params("_route", route);

                    if (rd.Params != null)
                    {
                        foreach (var pair in rd.Params)
                        {
                            req.Params(pair.Key, pair.Value);
                        }
                    }
                    var layerPath = rd.Path;

                    // this should be done for the layer
                    ProcessParams(layer, paramCalled, req, res, e =>
                    {
                        if (e != null)
                        {
                            next(layerError ?? e);
                        }
                        else if (route != null)
                        {
                            layer.HandleRequest(req, res, next);
                        }
                        else
                        {
                            trimPrefix(layer, layerError, layerPath, path);
                        }
                    });
                };

                trimPrefix = (layer, layerError, layerPath, path) =>
                {
                    char c;
                    if (layerPath.Length < path.Length
                        && (c = path[layerPath.Length]) != '/' && '.' != c)
                    {
                        next(layerError);
                        return;
                    }

                    // Trim off the part of the url that matches the route
                    // middleware (.use stuff) needs to have the path stripped
                    if (layerPath.Length != 0)
                    {
                        removed = layerPath;
                        var trimUrl = protohost + req.RouteUrl.Substring(protohost.Length + removed.Length);

                        // Ensure leading slash
                        if (fqdn == 0 && (trimUrl.Length == 0 || trimUrl[0] != '/'))
                        {
                            trimUrl = "/" + trimUrl;
                            slashAdded = true;
                        }

                        req.RouteUrl = trimUrl;

                        // Setup base URL (no trailing slash)
                        req.BaseUrl = parentUrl + (!String.IsNullOrEmpty(removed) && removed[removed.Length - 1] == '/'
                            ? removed.Substring(0, removed.Length - 1) : removed);
                    }

                    var ex = layerError != null ? layerError as Exception : null;
                    if (ex != null)
                        layer.HandleError(ex, req, res, next);
                    else
                        layer.HandleRequest(req, res, next);
                };

                // setup next layer
                req.Params("_next", next);

                // for options requests, respond with a default if nothing else responds
                if (req.HttpMethod == "OPTIONS")
                {
                    var old = done;
                    done = o =>
                    {
                        if (o != null || options.Count == 0)
                            old(o);
                        else
                            SendOptionsResponse(res, options, old);
                    };
                }

                // setup basic req values
                req.BaseUrl = parentUrl;

                next();
            }

            /// <summary>
            /// Use the given middleware function.
            /// </summary>
            /// <param name="action">the middleware function</param>
            /// <returns>self for chaining</returns>
            public Router Use(Action<IHttpRequest, IHttpResponse> action)
            {
                return Use(Wrap(action));
            }

            /// <summary>
            /// Use the given middleware function with mount <code>path</code>.
            /// </summary>
            /// <param name="path">the path</param>
            /// <param name="action">the middleware function</param>
            /// <returns>self for chaining</returns>
            public Router Use(String path, Action<IHttpRequest, IHttpResponse> action)
            {
                return Use(path, Wrap(action));
            }

            /// <summary>
            /// Use the given middleware function.
            /// </summary>
            /// <param name="mws">the middleware function(s)</param>
            /// <returns>self for chaining</returns>
            public Router Use(params Middleware[] mws)
            {
                return Use("/", mws);
            }

            /// <summary>
            /// Use the given middleware function.
            /// </summary>
            /// <param name="mws">the middleware function(s)</param>
            /// <returns>self for chaining</returns>
            public Router Use(params MiddlewareEx[] mws)
            {
                return Use("/", mws);
            }

            /// <summary>
            /// Use the given middleware function with mount <code>path</code>.
            /// </summary>
            /// <param name="path">the path</param>
            /// <param name="mws">the middleware function(s)</param>
            /// <returns>self for chaining</returns>
            public Router Use(String path, params Middleware[] mws)
            {
                foreach (var mw in mws)
                {
                    var opts = new Option() { CaseSensitive = _options.CaseSensitive, Strict = false, End = false };
                    var layer = new Layer(path, opts, mw);
                    _stack.Add(layer);
                }
                return this;
            }

            /// <summary>
            /// Use the given middleware function with mount <code>path</code>.
            /// </summary>
            /// <param name="path">the path</param>
            /// <param name="mws">the middleware function(s)</param>
            /// <returns>self for chaining</returns>
            public Router Use(String path, params MiddlewareEx[] mws)
            {
                foreach (var mw in mws)
                {
                    var opts = new Option() { CaseSensitive = _options.CaseSensitive, Strict = false, End = false };
                    var layer = new Layer(path, opts, mw);
                    _stack.Add(layer);
                }
                return this;
            }

            /// <summary>
            /// Use the given middlewares.
            /// </summary>
            /// <param name="mws">the <see cref="IMiddleware"/>(s)</param>
            /// <returns>self for chaining</returns>
            public Router Use(params IMiddleware[] mws)
            {
                return Use("/", mws);
            }

            /// <summary>
            /// Use the given middlewares with mount <code>path</code>.
            /// </summary>
            /// <param name="path">the path</param>
            /// <param name="mws">the <see cref="IMiddleware"/>(s)</param>
            /// <returns>self for chaining</returns>
            public Router Use(String path, params IMiddleware[] mws)
            {
                return Use(path, mws.Select<IMiddleware, Middleware>(mw => mw.Handle).ToArray());
            }

            /// <summary>
            /// Create a new <see cref="Route"/> for the given path.
            /// </summary>
            /// <param name="path">the path</param>
            /// <returns>the <see cref="Route"/> created</returns>
            public Route Route(String path)
            {
                var route = new Route(path);
                var opts = new Option() { CaseSensitive = _options.CaseSensitive, Strict = _options.Strict, End = true };
                var layer = new Layer(path, opts, route.Dispatch);
                layer.Route = route;
                _stack.Add(layer);
                return route;
            }

            #region HTTP Methods

            /// <summary>
            /// Adds handler(s) for all HTTP verbs on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router All(String path, params Middleware[] mws)
            {
                Route(path).All(mws);
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for all HTTP verbs on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router All(String path, params MiddlewareEx[] mws)
            {
                Route(path).All(mws);
                return this;
            }

            /// <summary>
            /// Adds a handler for the given HTTP <paramref name="method"/> on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="method">the method to handle</param>
            /// <param name="path">the route path</param>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Router On(String method, String path, Action<IHttpRequest, IHttpResponse> action)
            {
                Route(path).On(method, action);
                return this;
            }

            /// <summary>
            /// Adds handler(s) for the given HTTP <paramref name="method"/> on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="method">the method to handle</param>
            /// <param name="path">the route path</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router On(String method, String path, params Middleware[] mws)
            {
                Route(path).On(method, mws);
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for the given HTTP <paramref name="method"/> on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="method">the method to handle</param>
            /// <param name="path">the route path</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router On(String method, String path, params MiddlewareEx[] mws)
            {
                Route(path).On(method, mws);
                return this;
            }

            /// <summary>
            /// Adds a handler for HTTP GET on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Router Get(String path, Action<IHttpRequest, IHttpResponse> action)
            {
                Route(path).Get(action);
                return this;
            }

            /// <summary>
            /// Adds handler(s) for HTTP GET on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Get(String path, params Middleware[] mws)
            {
                Route(path).Get(mws);
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP GET on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Get(String path, params MiddlewareEx[] mws)
            {
                Route(path).Get(mws);
                return this;
            }

            /// <summary>
            /// Adds a handler for HTTP POST on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Router Post(String path, Action<IHttpRequest, IHttpResponse> action)
            {
                Route(path).Post(action);
                return this;
            }

            /// <summary>
            /// Adds handler(s) for HTTP POST on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Post(String path, params Middleware[] mws)
            {
                Route(path).Post(mws);
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP POST on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Post(String path, params MiddlewareEx[] mws)
            {
                Route(path).Post(mws);
                return this;
            }

            /// <summary>
            /// Adds a handler for HTTP PUT on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Router Put(String path, Action<IHttpRequest, IHttpResponse> action)
            {
                Route(path).Put(action);
                return this;
            }

            /// <summary>
            /// Adds handler(s) for HTTP PUT on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Put(String path, params Middleware[] mws)
            {
                Route(path).Put(mws);
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP PUT on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Put(String path, params MiddlewareEx[] mws)
            {
                Route(path).Put(mws);
                return this;
            }

            /// <summary>
            /// Adds a handler for HTTP DELETE on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Router Delete(String path, Action<IHttpRequest, IHttpResponse> action)
            {
                Route(path).Delete(action);
                return this;
            }

            /// <summary>
            /// Adds handler(s) for HTTP DELETE on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Delete(String path, params Middleware[] mws)
            {
                Route(path).Delete(mws);
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP DELETE on <paramref name="path"/> to this router.
            /// </summary>
            /// <param name="path">the route path</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Router Delete(String path, params MiddlewareEx[] mws)
            {
                Route(path).Delete(mws);
                return this;
            }

            #endregion

            /// <summary>
            /// Process any parameters for the layer.
            /// </summary>
            private void ProcessParams(Layer layer, IDictionary<String, ParamContext> called, IHttpRequest req, IHttpResponse res, Next done)
            {
                var callbacks = _params;
                // captured parameters from the layer, keys and values
                var keys = layer.Keys;

                // fast track
                if (keys == null || keys.Count == 0)
                {
                    done();
                    return;
                }

                var idx = 0;
                var count = keys.Count;
                var paramIndex = 0;
                String name = null;
                PathKey key = null;
                Object paramVal = null;
                IList<ParamCallback> paramCallbacks = null;
                ParamContext paramCalled = null;

                // process params in order
                // param callbacks can be async
                Next param = null;

                // single param callbacks
                Next paramCallback = null;

                paramCallback = o =>
                {
                    var fn = paramCallbacks.ElementAtOrDefault(paramIndex++);

                    // store updated value
                    paramCalled.Value = req.Params(key.Name);

                    Exception err = o as Exception;
                    if (err != null)
                    {
                        // store error
                        paramCalled.Error = err;
                        param(err);
                        return;
                    }

                    if (fn == null)
                    {
                        param();
                        return;
                    }

                    try
                    {
                        fn(req, res, paramCallback, paramVal, key.Name);
                    }
                    catch (Exception ex)
                    {
                        paramCallback(ex);
                    }
                };

                param = o =>
                {
                    if (o != null || idx >= count)
                    {
                        done(o);
                        return;
                    }

                    paramIndex = 0;
                    key = keys[idx++];
                    name = key.Name;
                    paramVal = req.Params(name);

                    if (paramVal == null || !callbacks.TryGetValue(name, out paramCallbacks))
                    {
                        // next param
                        param();
                        return;
                    }

                    // param previously called with same value or error occurred
                    if (called.TryGetValue(name, out paramCalled)
                        && (paramCalled.Error != null || Object.Equals(paramCalled.Match, paramVal)))
                    {
                        // restore value
                        req.Params(name, paramCalled.Value);
                        // next param
                        param(paramCalled.Error);
                        return;
                    }

                    called[name] = paramCalled = new ParamContext()
                    {
                        Match = paramVal,
                        Value = paramVal
                    };

                    paramCallback();
                };

                param();
            }

            private static RouteData MatchLayer(Layer layer, String path)
            {
                return layer.Match(path);
            }

            private static void AppendMethods(ICollection<String> list, IEnumerable<String> addition)
            {
                foreach (var method in addition)
                {
                    if (!list.Contains(method))
                        list.Add(method);
                }
            }

            private static void SendOptionsResponse(IHttpResponse res, IEnumerable<String> options, Next next) 
            {
                try
                {
                    var body = String.Join(",", options);
                    res.SetHeader("Allow", body);
                    res.Send(body);
                }
                catch (Exception ex)
                {
                    next(ex);
                }
            }

            /// <summary>
            /// restore request props after function.
            /// </summary>
            private static Next Restore(Next fn, IHttpRequest req, params String[] props)
            {
                String baseUrl = req.BaseUrl;
                Object[] vals = props.Select(p => req.Params(p)).ToArray();
                return o =>
                {
                    // restore vals
                    req.BaseUrl = baseUrl;
                    for (int i = 0; i < props.Length; i++)
                    {
                        req.Params(props[i], vals[i]);
                    }
                    fn(o);
                };
            }

            private static String GetPathName(String url)
            {
                if (String.IsNullOrEmpty(url))
                    return null;
                if (url[0] == '/')
                    return url;
                try
                {
                    return new Uri(url).AbsolutePath;
                }
                catch
                {
                    return null;
                }
            }

            class ParamContext
            {
                public Object Value { get; set; }
                public Object Match { get; set; }
                public Exception Error { get; set; }
            }
        }

        /// <summary>
        /// Represent a route.
        /// </summary>
        public class Route
        {
            readonly String _path;
            readonly IList<Layer> _stack = new List<Layer>();
            readonly IDictionary<String, Boolean> _methods = new Dictionary<String, Boolean>(StringComparer.OrdinalIgnoreCase);
            private Boolean _all;

            /// <summary>
            /// Initializes with the given <paramref name="path"/>.
            /// </summary>
            /// <param name="path">the path of this route</param>
            public Route(String path)
            {
                _path = path;
            }

            /// <summary>
            /// Dispatches req, res into this route.
            /// </summary>
            /// <param name="req"><see cref="IHttpRequest"/></param>
            /// <param name="res"><see cref="IHttpResponse"/></param>
            /// <param name="done">the final filter</param>
            public void Dispatch(IHttpRequest req, IHttpResponse res, Next done)
            {
                var idx = 0;
                var stack = _stack;

                if (stack.Count == 0)
                {
                    done();
                    return;
                }

                var method = req.HttpMethod;
                if (String.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
                    && !_methods.ContainsKey("HEAD"))
                    method = "GET";

                req.Params("_route", this);

                Next next = null;
                next = o =>
                {
                    var dst = o as String;
                    if (dst != null && dst.Equals("route"))
                    {
                        done();
                        return;
                    }

                    var layer = stack.ElementAtOrDefault(idx++);
                    if (layer == null)
                    {
                        done(o);
                        return;
                    }

                    if (layer.Method != null && layer.Method != method)
                    {
                        next(o);
                        return;
                    }

                    Exception err = o as Exception;
                    if (err != null)
                        layer.HandleError(err, req, res, next);
                    else
                        layer.HandleRequest(req, res, next);
                };

                next();
            }

            /// <summary>
            /// Adds handler(s) for all HTTP verbs to this route.
            /// </summary>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route All(params Middleware[] mws)
            {
                foreach (var mw in mws)
                {
                    var layer = new Layer("/", new Option(), mw);
                    _all = true;
                    _stack.Add(layer);
                }
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for all HTTP verbs to this route.
            /// </summary>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route All(params MiddlewareEx[] mws)
            {
                foreach (var mw in mws)
                {
                    var layer = new Layer("/", new Option(), mw);
                    _all = true;
                    _stack.Add(layer);
                }
                return this;
            }

            #region HTTP Methods

            /// <summary>
            /// Adds a handler for the given HTTP <paramref name="method"/> to this route.
            /// </summary>
            /// <param name="method">the method to handle</param>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Route On(String method, Action<IHttpRequest, IHttpResponse> action)
            {
                return On(method, Wrap(action));
            }

            /// <summary>
            /// Adds handler(s) for the given HTTP <paramref name="method"/> to this route.
            /// </summary>
            /// <param name="method">the method to handle</param>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route On(String method, params Middleware[] mws)
            {
                foreach (var mw in mws)
                {
                    var layer = new Layer("/", new Option(), mw);
                    layer.Method = method;
                    _methods[method] = true;
                    _stack.Add(layer);
                }
                return this;
            }

            /// <summary>
            /// Adds exception handler(s) for the given HTTP <paramref name="method"/> to this route.
            /// </summary>
            /// <param name="method">the method to handle</param>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route On(String method, params MiddlewareEx[] mws)
            {
                foreach (var mw in mws)
                {
                    var layer = new Layer("/", new Option(), mw);
                    layer.Method = method;
                    _methods[method] = true;
                    _stack.Add(layer);
                }
                return this;
            }

            /// <summary>
            /// Adds a handler for HTTP GET to this route.
            /// </summary>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Route Get(Action<IHttpRequest, IHttpResponse> action)
            {
                return Get(Wrap(action));
            }

            /// <summary>
            /// Adds handler(s) for HTTP GET to this route.
            /// </summary>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Get(params Middleware[] mws)
            {
                return On("GET", mws);
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP GET to this route.
            /// </summary>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Get(params MiddlewareEx[] mws)
            {
                return On("GET", mws);
            }

            /// <summary>
            /// Adds a handler for HTTP POST to this route.
            /// </summary>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Route Post(Action<IHttpRequest, IHttpResponse> action)
            {
                return Post(Wrap(action));
            }

            /// <summary>
            /// Adds handler(s) for HTTP POST to this route.
            /// </summary>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Post(params Middleware[] mws)
            {
                return On("POST", mws);
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP POST to this route.
            /// </summary>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Post(params MiddlewareEx[] mws)
            {
                return On("POST", mws);
            }

            /// <summary>
            /// Adds a handler for HTTP PUT to this route.
            /// </summary>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Route Put(Action<IHttpRequest, IHttpResponse> action)
            {
                return Put(Wrap(action));
            }

            /// <summary>
            /// Adds handler(s) for HTTP PUT to this route.
            /// </summary>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Put(params Middleware[] mws)
            {
                return On("PUT", mws);
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP PUT to this route.
            /// </summary>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Put(params MiddlewareEx[] mws)
            {
                return On("PUT", mws);
            }

            /// <summary>
            /// Adds a handler for HTTP DELETE to this route.
            /// </summary>
            /// <param name="action">the handler</param>
            /// <returns>self for chaining</returns>
            public Route Delete(Action<IHttpRequest, IHttpResponse> action)
            {
                return Delete(Wrap(action));
            }

            /// <summary>
            /// Adds handler(s) for HTTP DELETE to this route.
            /// </summary>
            /// <param name="mws">the handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Delete(params Middleware[] mws)
            {
                return On("DELETE", mws);
            }

            /// <summary>
            /// Adds exception handler(s) for HTTP DELETE to this route.
            /// </summary>
            /// <param name="mws">the exception handler(s)</param>
            /// <returns>self for chaining</returns>
            public Route Delete(params MiddlewareEx[] mws)
            {
                return On("DELETE", mws);
            }

            #endregion

            internal Boolean HandlesMethod(String method)
            {
                if (_all)
                    return true;

                return _methods.ContainsKey(method) ||
                    (String.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
                    && _methods.ContainsKey("GET"));
            }

            internal IEnumerable<String> Options()
            {
                IEnumerable<String> methods = _methods.Keys;
                // append automatic head
                if (_methods.ContainsKey("GET") && !_methods.ContainsKey("HEAD"))
                    methods = methods.Concat(new String[] { "HEAD" });
                return methods.Select(m => m.ToUpperInvariant());
            }
        }

        class RouteData
        {
            readonly String _path;
            readonly IDictionary<Object, Object> _params;

            public RouteData(String path, IDictionary<Object, Object> parameters)
            {
                _path = path;
                _params = parameters;
            }

            public String Path
            {
                get { return _path; }
            }

            public IDictionary<Object, Object> Params
            {
                get { return _params; }
            }
        }

        class Layer
        {
            readonly Middleware _handle;
            readonly MiddlewareEx _errorHandle;
            readonly Regex _regex;
            readonly List<PathKey> _keys = new List<PathKey>();
            readonly Boolean _fastSlash;

            public Layer(String path, Option options, Middleware handle)
                : this(path, options)
            {
                _handle = handle;
            }

            public Layer(String path, Option options, MiddlewareEx handle)
                : this(path, options)
            {
                _errorHandle = handle;
            }

            protected Layer(String path, Option options)
            {
                _regex = PathtoRegex(path, _keys, options);

                if (path == "/" && options.End == false)
                    _fastSlash = true;
            }

            public String Method { get; set; }

            public Route Route { get; set; }

            public IList<PathKey> Keys
            {
                get { return _keys; }
            }

            public void HandleRequest(IHttpRequest req, IHttpResponse res, Next next)
            {
                var fn = _handle;

                if (fn == null)
                {
                    next();
                    return;
                }

                try
                {
                    fn(req, res, next);
                }
                catch (Exception e)
                {
                    next(e);
                }
            }

            public void HandleError(Exception error, IHttpRequest req, IHttpResponse res, Next next)
            {
                var fn = _errorHandle;

                if (fn == null)
                {
                    next(error);
                    return;
                }

                try
                {
                    fn(error, req, res, next);
                }
                catch (Exception e)
                {
                    next(e);
                }
            }

            public RouteData Match(String path)
            {
                if (path == null)
                {
                    // no path, nothing matches
                    return null;
                }

                if (_fastSlash)
                {
                    // fast path non-ending match for / (everything matches)
                    return new RouteData(String.Empty, null);
                }

                var m = _regex.Match(path);

                if (!m.Success)
                    return null;

                // store values
                IDictionary<Object, Object> parameters = null;
                if (m.Groups.Count > 1)
                {
                    parameters = new Dictionary<Object, Object>();
                    for (int i = 1; i < m.Groups.Count; i++)
                    {
                        var key = _keys.ElementAtOrDefault(i - 1);
                        var prop = key != null ? key.Name : (Object)(i++);
                        var val = DecodeParam(m.Groups[i].Value);

                        if (val != null || !parameters.ContainsKey(prop))
                            parameters[prop] = val;
                    }
                }

                return new RouteData(m.Value, parameters);
            }

            static Object DecodeParam(Object val)
            {
                String s = val as String;

                if (s == null)
                    return val;

                return HttpUtility.UrlDecode(s);
            }
        }

        /// <summary>
        /// Represents a variable in a path.
        /// </summary>
        public class PathKey
        {
            readonly String _name;
            readonly Boolean _optional;

            /// <summary>
            /// Initializes.
            /// </summary>
            /// <param name="name">the name</param>
            /// <param name="optional">optional or not</param>
            public PathKey(String name, Boolean optional)
            {
                _name = name;
                _optional = optional;
            }

            /// <summary>
            /// Gets the name of this key.
            /// </summary>
            public String Name
            {
                get { return _name; }
            }

            /// <summary>
            /// Is this key optional?
            /// </summary>
            public Boolean Optional
            {
                get { return _optional; }
            }
        }

        #endregion

        #region Static

        /// <summary>
        /// Turn an Express-style path string such as `/user/:name` into a regular expression.
        /// </summary>
        public static Regex PathtoRegex(String path, ICollection<PathKey> keys, Option options)
        {
            options = options ?? Option.Default;
            keys = keys ?? new List<PathKey>();
            var strict = options.Strict;
            var end = options.End.GetValueOrDefault(true);
            var flags = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

            path = "^" + path + (strict ? String.Empty : path[path.Length - 1] == '/' ? "?" : "/?");
            path = Regex.Replace(path, @"\/\(", "/(?:");
            path = Regex.Replace(path, @"([\/\.])", m => m.Result("\\$1"));
            path = Regex.Replace(path, @"(\\\/)?(\\\.)?:(\w+)(\(.*?\))?(\*)?(\?)?", m =>
            {
                String slash = m.Groups[1].Value;
                String format = m.Groups[2].Value;
                String key = m.Groups[3].Value;
                String capture = m.Groups[4].Value;
                Boolean star = !String.IsNullOrEmpty(m.Groups[5].Value);
                String optionalStr = m.Groups[6].Value;
                Boolean optional = !String.IsNullOrEmpty(optionalStr);

                if (String.IsNullOrEmpty(capture))
                    capture = "([^\\/" + format + "]+?)";

                keys.Add(new PathKey(key, optional));

                return (optional ? String.Empty : slash)
                    + "(?:"
                    + format + (optional ? slash : String.Empty) + capture
                    + (star ? "((?:[\\/" + format + "].+?)?)" : String.Empty)
                    + ")"
                    + optionalStr;
            });
            path = Regex.Replace(path, @"\*", "(.*)");

            // If the path is non-ending, match until the end or a slash.
            path += (end ? "$" : (path[path.Length - 1] == '/' ? String.Empty : "(?=\\/|$)"));

            return new Regex(path, flags);
        }

        /// <summary>
        /// Turn Express-style paths such as `/user/:name` into a regular expression.
        /// </summary>
        public static Regex PathtoRegex(Object path, ICollection<PathKey> keys, Option options)
        {
            var regex = path as Regex;
            if (regex != null)
                return regex;

            options = options ?? Option.Default;
            keys = keys ?? new List<PathKey>();
            var flags = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

            var s = path as String;
            if (s != null)
                return PathtoRegex(s, keys, options);

            var e = path as IEnumerable;
            if (e != null)
            {
                var sources = e.Cast<Object>().Select(p => PathtoRegex(p, keys, options).ToString());

                return new Regex("(?:" + String.Join("|", sources) + ")", flags);
            }

            throw new ArgumentException("Unknown type", "path");
        }

        internal static Middleware Wrap(Action<IHttpRequest, IHttpResponse> action)
        {
            return (req, res, next) => action(req, res);
        }

        internal static MiddlewareEx Wrap(Action<Exception, IHttpRequest, IHttpResponse> action)
        {
            return (err, req, res, next) => action(err, req, res);
        }

        #endregion

        #region Middlewares

        /// <summary>
        /// Creates a built-in middleware for serving the static assets.
        /// </summary>
        /// <param name="root">the root directory from which the static assets are to be served</param>
        /// <param name="options"><see cref="ServeStaticOption"/></param>
        /// <returns>the middleware for serving the static assets</returns>
        public static IMiddleware Static(String root, ServeStaticOption options = null)
        {
            return new ServeStatic(root, options);
        }

        /// <summary>
        /// ServeStatic options.
        /// </summary>
        public class ServeStaticOption
        {
            static readonly String[] DefaultWelcomeFiles = new String[] { "index.html", "index.htm" };

            /// <summary>
            /// Initializes.
            /// </summary>
            public ServeStaticOption()
            {
                Index = DefaultWelcomeFiles;
                DirectoryBrowsing = false;
                Redirect = true;
            }

            /// <summary>
            /// Enable or disable directory browsing.
            /// </summary>
            public Boolean DirectoryBrowsing { get; set; }

            /// <summary>
            /// Gets or sets index files.
            /// </summary>
            public IEnumerable<String> Index { get; set; }

            /// <summary>
            /// Redirect to trailing “/” when the pathname is a directory.
            /// </summary>
            public Boolean Redirect { get; set; }

            /// <summary>
            /// Option for serving dotfiles. Possible values are “allow”, “deny”, and “ignore”
            /// </summary>
            public String Dotfiles { get; set; }

            /// <summary>
            /// Gets or sets the max-age property of the Cache-Control header in milliseconds.
            /// </summary>
            public Int32 MaxAge { get; set; }
        }

        class ServeStatic : IMiddleware
        {
            readonly String _root;
            readonly ServeStaticOption _options;

            public ServeStatic(String root, ServeStaticOption options)
            {
                _root = root;
                _options = options ?? new ServeStaticOption();
            }

            public void Handle(IHttpRequest req, IHttpResponse res, Next next)
            {
                if (!String.Equals("GET", req.HttpMethod, StringComparison.OrdinalIgnoreCase)
                    && !String.Equals("HEAD", req.HttpMethod, StringComparison.OrdinalIgnoreCase))
                {
                    next();
                    return;
                }

                var originalUrl = req.Url;
                var path = req.RouteUrl;
                var hasTrailingSlash = originalUrl.AbsolutePath[originalUrl.AbsolutePath.Length - 1] == '/';

                if (path == "/" && !hasTrailingSlash)
                    // make sure redirect occurs at mount
                    path = String.Empty;

                path = HttpUtility.UrlDecode(path);

                // null byte(s)
                if (path.Contains('\0'))
                {
                    res.Status((Int32)HttpStatusCode.BadRequest).End();
                }

                if (path.Length > 0 && path[0] == '/')
                    path = path.Substring(1);
                path = path.Replace('/', System.IO.Path.DirectorySeparatorChar);
                path = System.IO.Path.Combine(_root, path);

                DirectoryInfo di;
                FileInfo fi;

                if ((di = new DirectoryInfo(path)).Exists)
                {
                    if (!hasTrailingSlash && _options.Redirect)
                    {
                        UriBuilder ub = new UriBuilder(originalUrl);
                        ub.Path += "/";
                        res.Redirect(ub.ToString());
                    }
                    else
                    {
                        var index = FindTargetFile(di, _options.Index);
                        if (index != null)
                        {
                            SendFile(index, res);
                        }
                        else if (!_options.DirectoryBrowsing)
                        {
                            next();
                            return;
                        }
                        else
                        {
                            SendDirectory(di, req, res);
                        }
                    }
                }
                else if ((fi = new FileInfo(path)).Exists)
                {
                    SendFile(fi, res);
                }
                else
                {
                    next();
                    return;
                }

                res.End();
            }

            private static void SendFile(FileInfo fi, IHttpResponse res)
            {
                res.SetHeader("Content-Type", FindContentType(fi.Extension));
                var output = res.OutputStream;
                using (FileStream stream = fi.OpenRead())
                {
                    Byte[] buf = new Byte[2048];
                    Int32 n;
                    while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, n);
                    }
                }
            }

            private static void SendDirectory(DirectoryInfo di, IHttpRequest req, IHttpResponse res)
            {
                res.SetHeader("Content-Type", FindContentType(".html"));
                using (var writer = new StreamWriter(res.OutputStream))
                {
                    writer.Write("<!DOCTYPE html><html><head><title>");
                    writer.Write(req.UserHostName);
                    writer.Write(" - ");
                    writer.Write(req.Url.AbsolutePath);
                    writer.Write("</title></head><body><h1>");
                    writer.Write(req.UserHostName);
                    writer.Write(" - ");
                    writer.Write(req.Url.AbsolutePath);
                    writer.Write("</h1><hr><pre>");
                    foreach (var item in di.GetDirectories())
                    {
                        writer.Write("<a href='");
                        String link = item.Name;
                        writer.Write(link);
                        writer.Write("/'>");
                        writer.Write(item.Name);
                        writer.WriteLine("</a>");
                    }
                    foreach (var item in di.GetFiles())
                    {
                        writer.Write("<a href='");
                        String link = item.Name;
                        writer.Write(link);
                        writer.Write("'>");
                        writer.Write(item.Name);
                        writer.WriteLine("</a>");
                    }
                    writer.Write("</pre><hr></body></html>");
                }
            }

            private static FileInfo FindTargetFile(DirectoryInfo di, IEnumerable<String> welcomeFiles)
            {
                if (welcomeFiles != null)
                {
                    foreach (String file in welcomeFiles)
                    {
                        String filePath = System.IO.Path.Combine(di.FullName, file);
                        if (File.Exists(filePath))
                            return new FileInfo(filePath);
                    }
                }

                return null;
            }

            private static String FindContentType(String ext)
            {
                String result;
                if (ext == null || !MimeTypes.TryGetValue(ext, out result))
                    result = MimeTypes[String.Empty];
                return result;
            }

            public static readonly IDictionary<String, String> MimeTypes = new Dictionary<String, String>()
            {
                { String.Empty, "application/octet-stream" },
                { ".zip", "application/zip" },
                { ".tar", "application/x-tar" },
                { ".wav", "audio/x-wav" },
                { ".gif", "image/gif" },
                { ".jpg", "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png", "image/png" },
                { ".ico", "image/vnd.microsoft.icon" },
                { ".htm", "text/html"},
                { ".html", "text/html" },
                { ".css", "text/css" },
                { ".js", "text/javascript" },
                { ".txt", "text/plain" },
                { ".swf", "application/x-shockwave-flash" },
                { ".flv", "video/x-flv" }
            };
        }

        #endregion
    }

    /// <summary>
    /// Args for <see cref="Express.Mount"/> event.
    /// </summary>
    public class MountEventArgs : EventArgs
    {
        readonly Express _parent;

        /// <summary>
        /// Initializes.
        /// </summary>
        /// <param name="parent"></param>
        public MountEventArgs(Express parent)
        {
            _parent = parent;
        }

        /// <summary>
        /// Gets the parent app.
        /// </summary>
        public Express Parent
        {
            get { return _parent; }
        }
    }

    #region Interfaces

    /// <summary>
    /// Provides a middleware function.
    /// </summary>
    public interface IMiddleware
    {
        /// <summary>
        /// Dispatches a req, res pair into this middleware.
        /// </summary>
        /// <param name="req">the <see cref="IHttpRequest"/></param>
        /// <param name="res">the <see cref="IHttpResponse"/></param>
        /// <param name="next">the next filter</param>
        void Handle(IHttpRequest req, IHttpResponse res, Next next);
    }

    /// <summary>
    /// Represents a HTTP request.
    /// </summary>
    public interface IHttpRequest
    {
        /// <summary>
        /// Gets the HTTP version used by the requesting client.
        /// </summary>
        Version Version { get; }
        /// <summary>
        /// Gets the HTTP method specified by the client.
        /// </summary>
        String HttpMethod { get; }
        /// <summary>
        /// Gets the URL information (without the host and port) requested by the client.
        /// </summary>
        String RawUrl { get; }
        /// <summary>
        /// Gets the System.Uri object requested by the client.
        /// </summary>
        Uri Url { get; }
        /// <summary>
        /// Gets the collection of header name/value pairs sent in the request.
        /// </summary>
        NameValueCollection Headers { get; }
        /// <summary>
        /// Gets a stream that contains the body data sent by the client.
        /// </summary>
        Stream InputStream { get; }
        /// <summary>
        /// Gets the client IP address and port number from which the request originated.
        /// </summary>
        IPEndPoint RemoteEndPoint { get; }
        /// <summary>
        /// Gets the DNS name and, if provided, the port number specified by the client.
        /// </summary>
        String UserHostName { get; }
        /// <summary>
        /// Gets or sets the route URL in an <see cref="Express"/> app.
        /// </summary>
        String RouteUrl { get; set; }
        /// <summary>
        /// Gets or sets the base URL in an <see cref="Express"/> app.
        /// </summary>
        String BaseUrl { get; set; }
        /// <summary>
        /// Stores a parameter of <paramref name="value"/> associated with <paramref name="key"/>.
        /// </summary>
        /// <param name="key">the name of the parameter</param>
        /// <param name="value">the value of the parameter</param>
        void Params(Object key, Object value);
        /// <summary>
        /// Gets a parameter's value associated with <paramref name="key"/>.
        /// </summary>
        /// <param name="key">the name of the parameter</param>
        /// <returns>the value, of null if not found</returns>
        Object Params(Object key);
        /// <summary>
        /// Gets a parameter's value associated with <paramref name="key"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">the name of the parameter</param>
        /// <param name="def">the default value if not found</param>
        /// <returns>the value, of default value if not found</returns>
        T ParamsAs<T>(Object key, T def = default(T));
    }

    /// <summary>
    /// Represents a HTTP response.
    /// </summary>
    public interface IHttpResponse : IDisposable
    {
        /// <summary>
        /// Gets or sets the HTTP status code to be returned to the client.
        /// </summary>
        Int32 StatusCode { get; set; }
        /// <summary>
        /// Gets or sets a text description of the HTTP status code returned to the client.
        /// </summary>
        String StatusDescription { get; set; }
        /// <summary>
        /// Gets a <see cref="System.IO.Stream"/> object to which a response can be written.
        /// </summary>
        Stream OutputStream { get; }
        /// <summary>
        /// <see cref="StatusCode"/>
        /// </summary>
        /// <returns>self for chaining</returns>
        IHttpResponse Status(Int32 status);
        /// <summary>
        /// Sets an HTTP header.
        /// </summary>
        /// <param name="name">the header name</param>
        /// <param name="val">the value</param>
        /// <returns></returns>
        IHttpResponse SetHeader(String name, String val);
        /// <summary>
        /// Configures the response to redirect the client to the specified URL.
        /// </summary>
        /// <param name="url">the URL that the client should use to locate the requested resource</param>
        void Redirect(String url);
        /// <summary>
        /// Sends the response with <paramref name="body"/> to the client
        /// and releases the resources held by this instance.
        /// </summary>
        /// <param name="body">the content to send</param>
        void Send(String body);
        /// <summary>
        /// Sends the response to the client and releases the resources held by this instance.
        /// </summary>
        void End();
    }

    #endregion

    #region Extensions

    /// <summary>
    /// Extensions for <see cref="Express"/>.
    /// </summary>
    public static class ExpressExtensions
    {
        #region Express

        /// <summary>
        /// Proxy to <see cref="Express.RouteFor(String)"/>.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="path"></param>
        /// <returns>the new <see cref="Route"/></returns>
        public static Express.Route Route(this Express app, String path)
        {
            return app.RouteFor(path);
        }

        /// <summary>
        /// Routes HTTP GET requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="app">the <see cref="Express"/> instance</param>
        /// <param name="mw">a middleware</param>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public static Express Get(this Express app, String path,
            Middleware mw, Action<IHttpRequest, IHttpResponse> action)
        {
            return app.Get(path, mw, Express.Wrap(action));
        }

        /// <summary>
        /// Routes HTTP GET requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="app">the <see cref="Express"/> instance</param>
        /// <param name="mw1">the first middleware</param>
        /// <param name="mw2">the second middleware</param>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public static Express Get(this Express app, String path,
            Middleware mw1, Middleware mw2,
            Action<IHttpRequest, IHttpResponse> action)
        {
            return app.Get(path, mw1, mw2, Express.Wrap(action));
        }

        /// <summary>
        /// Routes HTTP GET requests to the specified path with the specified callback function.
        /// </summary>
        /// <param name="app">the <see cref="Express"/> instance</param>
        /// <param name="mw1">the first middleware</param>
        /// <param name="mw2">the second middleware</param>
        /// <param name="mw3">the third middleware</param>
        /// <param name="path">the route path</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public static Express Get(this Express app, String path,
            Middleware mw1, Middleware mw2, Middleware mw3,
            Action<IHttpRequest, IHttpResponse> action)
        {
            return app.Get(path, mw1, mw2, mw3, Express.Wrap(action));
        }

        #endregion

        #region Route

        /// <summary>
        /// Adds a handler for HTTP GET to this route.
        /// </summary>
        /// <param name="route">the <see cref="Express.Route"/> instance</param>
        /// <param name="mw">a middleware</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public static Express.Route Get(this Express.Route route,
            Middleware mw, Action<IHttpRequest, IHttpResponse> action)
        {
            return route.Get(mw, Express.Wrap(action));
        }

        /// <summary>
        /// Adds a handler for HTTP GET to this route.
        /// </summary>
        /// <param name="route">the <see cref="Express.Route"/> instance</param>
        /// <param name="mw1">the first middleware</param>
        /// <param name="mw2">the second middleware</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public static Express.Route Get(this Express.Route route,
            Middleware mw1, Middleware mw2,
            Action<IHttpRequest, IHttpResponse> action)
        {
            return route.Get(mw1, mw2, Express.Wrap(action));
        }

        /// <summary>
        /// Adds a handler for HTTP GET to this route.
        /// </summary>
        /// <param name="route">the <see cref="Express.Route"/> instance</param>
        /// <param name="mw1">the first middleware</param>
        /// <param name="mw2">the second middleware</param>
        /// <param name="mw3">the third middleware</param>
        /// <param name="action">the handler</param>
        /// <returns>self for chaining</returns>
        public static Express.Route Get(this Express.Route route,
            Middleware mw1, Middleware mw2, Middleware mw3,
            Action<IHttpRequest, IHttpResponse> action)
        {
            return route.Get(mw1, mw2, mw3, Express.Wrap(action));
        }

        #endregion

        #region Request

        /// <summary>
        /// Is the <paramref name="req"/> a Web Socket request?
        /// </summary>
        /// <param name="req">the <see cref="IHttpRequest"/></param>
        /// <returns>true if the request is a Web Socket request, otherwise false</returns>
        public static Boolean IsWebSocketRequest(this IHttpRequest req)
        {
            return req.HttpMethod == "GET" &&
                              req.Version > HttpVersion.Version10 &&
                              req.Headers.Contains("Upgrade", "websocket") &&
                              req.Headers.Contains("Connection", "Upgrade");
        }

        /// <summary>
        /// Checks whether the specified <see cref="NameValueCollection"/> contains the entry
        /// with the specified both <paramref name="name"/> and <paramref name="value"/>.
        /// </summary>
        /// <param name="col">the <see cref="NameValueCollection"/> to test</param>
        /// <param name="name">the name of the key to find</param>
        /// <param name="value">the value of the key to find</param>
        /// <returns>
        /// <c>true</c> if <paramref name="col"/> contains the entry with both
        /// <paramref name="name"/> and <paramref name="value"/>; otherwise, <c>false</c>.
        /// </returns>
        public static Boolean Contains(this NameValueCollection col, String name, String value)
        {
            if (col == null || col.Count == 0)
                return false;

            var vals = col.GetValues(name);
            if (vals == null)
                return false;

            if (vals.Any(val => String.Equals(val, value, StringComparison.OrdinalIgnoreCase)))
                return true;

            return vals.Any(val => String.Equals(val, value, StringComparison.OrdinalIgnoreCase)) ||
                vals.Any(vs => vs.Split(',').Any(val => String.Equals(val.Trim(), value, StringComparison.OrdinalIgnoreCase)));
        }

        #endregion
    }

    #endregion

    #region HttpListener Extensions

    /// <summary>
    /// Extensions for <see cref="System.Net.HttpListener"/>.
    /// </summary>
    public static class HttpListenerExtensions
    {
        /// <summary>
        /// Enables <see cref="Express"/> on a <paramref name="listener"/>.
        /// All <see cref="System.Net.HttpListenerContext"/>s accepted will
        /// be handled by a new Express app.
        /// </summary>
        /// <param name="listener"><see cref="HttpListener"/></param>
        /// <returns>the created <see cref="Express"/> app</returns>
        public static Express Express(this HttpListener listener)
        {
            Express app = new Express();
            app.Listen(listener);
            return app;
        }
    }

    class HttpListenerRequestWrapper : IHttpRequest
    {
        readonly HttpListenerRequest _request;
        readonly IDictionary<Object, Object> _params = new Dictionary<Object, Object>();

        public HttpListenerRequestWrapper(HttpListenerRequest request)
        {
            _request = request;
        }

        public HttpListenerRequest Request
        {
            get { return _request; }
        }

        public Version Version
        {
            get { return _request.ProtocolVersion; }
        }

        public String HttpMethod
        {
            get { return _request.HttpMethod; }
        }

        public String RawUrl
        {
            get { return _request.RawUrl; }
        }

        public Uri Url
        {
            get { return _request.Url; }
        }

        public String RouteUrl { get; set; }

        public String BaseUrl { get; set; }

        public NameValueCollection Headers
        {
            get { return _request.Headers; }
        }

        public Stream InputStream
        {
            get { return _request.InputStream; }
        }

        public IPEndPoint RemoteEndPoint
        {
            get { return _request.RemoteEndPoint; }
        }

        public String UserHostName
        {
            get { return _request.UserHostName; }
        }

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
    }

    class HttpListenerResponseWrapper : IHttpResponse
    {
        readonly HttpListenerResponse _response;

        public HttpListenerResponseWrapper(HttpListenerResponse response)
        {
            _response = response;
        }

        public Int32 StatusCode
        {
            get { return _response.StatusCode; }
            set { _response.StatusCode = value; }
        }

        public String StatusDescription
        {
            get { return _response.StatusDescription; }
            set { _response.StatusDescription = value; }
        }

        public Stream OutputStream
        {
            get { return _response.OutputStream; }
        }

        public IHttpResponse Status(Int32 status)
        {
            StatusCode = status;
            return this;
        }

        public IHttpResponse SetHeader(String name, String val)
        {
            _response.Headers[name] = val;
            return this;
        }

        public void Redirect(String url)
        {
            _response.Redirect(url);
        }

        public void Send(String body)
        {
            if (_response.Headers["Content-Type"] == null)
                SetHeader("Content-Type", "text/html");
            using (StreamWriter w = new StreamWriter(_response.OutputStream))
            {
                w.Write(body);
            }
        }

        public void End()
        {
            _response.Close();
        }

        public void Dispose()
        {
            ((IDisposable)_response).Dispose();
        }
    }

    #endregion
}
