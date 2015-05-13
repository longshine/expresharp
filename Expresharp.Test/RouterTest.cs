using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Router = Expresharp.Express.Router;

namespace Expresharp
{
    [TestClass]
    public class RouterTest
    {
        [TestMethod]
        public void TestUseOfOtherRouters()
        {
            var router = new Router();
            var another = new Router();

            another.Get("/bar", (req, res) => res.End());

            router.Use("/foo", another);

            router.Handle(MockRequest.Get("/foo/bar"), new MockResponse(), o => Assert.Fail("should support .Use of other routers"));
        }

        [TestMethod]
        public void TestDynamicRouters()
        {
            var router = new Router();
            var another = new Router();

            another.Get("/:bar", (req, res) =>
            {
                Assert.AreEqual("route", req.ParamsAs<String>("bar"));
                res.End();
            });

            router.Use("/:foo", another);

            router.Handle(MockRequest.Get("/test/route"), new MockResponse(), o => Assert.Fail("should support dynamic routes"));
        }

        [TestMethod]
        public void TestHandleBlankURL()
        {
            var router = new Router();

            router.Use((req, res) =>
            {
                Assert.Fail("should handle blank URL");
            });

            router.Handle(MockRequest.Get(""), new MockResponse(), o => { });
        }

        [TestMethod]
        public void TestOverflow()
        {
            var router = new Router();
            Action<IHttpRequest, IHttpResponse> handler = (req, res) =>
            {
                throw new Exception("wrong handler");
            };

            for (var i = 0; i < 6000; i++)
            {
                router.Get("/thing" + i, handler);
            }

            router.Get("/", (req, res) => res.End());

            router.Handle(MockRequest.Get("/"), new MockResponse(), o => Assert.Fail("should not stack overflow with many registered routes"));
        }

        [TestMethod]
        public void TestDispatch()
        {
            var router = new Router();

            router.Route("/foo").Get((req, res) => res.Send("foo"));

            var resp = new MockResponse()
            {
                SendHandler = val => Assert.AreEqual("foo", val)
            };

            router.Handle(MockRequest.Get("/foo"), resp, o => Assert.Fail("should dispatch"));
        }

        [TestMethod]
        public void TestSkipNonErrorMiddleware()
        {
            var router = new Router();

            router.Get("/foo", (req, res, next) => next(new Exception("foo")));

            router.Get("/bar", (req, res, next) => next(new Exception("bar")));

            router.Use((req, res, next) =>
            {
                Assert.Fail("should skip non error middleware");
            });

            router.Use((err, req, res, next) =>
            {
                Assert.AreEqual("foo", err.Message);
            });

            router.Handle(MockRequest.Get("/foo"), new MockResponse(), o => { });
        }

        [TestMethod]
        public void TestHandleThrowingInsideRoutesWithParams()
        {
            var router = new Router();

            router.Get("/foo/:id", (req, res, next) => next(new Exception("foo")));

            router.Use((req, res, next) =>
            {
                Assert.Fail("should handle throwing inside routes with params");
            });

            router.Use((err, req, res, next) =>
            {
                Assert.AreEqual("foo", err.Message);
            });

            router.Handle(MockRequest.Get("/foo/2"), new MockResponse(), o => { });
        }

        [TestMethod]
        public void TestHandleThrowingInsideErrorHandlers()
        {
            var router = new Router();

            router.Use((req, res, next) =>
            {
                throw new Exception("boom!");
            });

            router.Use((req, res, next) =>
            {
                throw new Exception("oops");
            });

            router.Use((err, req, res, next) =>
            {
                Assert.AreEqual("oops", err.Message);
            });

            router.Handle(MockRequest.Get("/"), new MockResponse(), o => { });
        }

        [TestMethod]
        public void TestFQDN()
        {
            {
                var request = MockRequest.Get("http://example.com/foo");
                request.RouteUrl = "http://example.com/foo";
                request.Params("hit", 0);

                var router = new Router();

                router.Use((req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(0, hit);
                    Assert.AreEqual("http://example.com/foo", req.RouteUrl);
                    next();
                });

                router.Handle(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(1, request.Params("hit"), "should not obscure FQDNs");
                });
            }

            {
                var request = MockRequest.Get("/proxy?url=http://example.com/blog/post/1");
                request.Params("hit", 0);

                var router = new Router();

                router.Use("/proxy", (req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(0, hit);
                    Assert.AreEqual("/", req.RouteUrl);
                    next();
                });

                router.Handle(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(1, request.Params("hit"), "should ignore FQDN in search");
                });
            }

            {
                var request = MockRequest.Get("/proxy/http://example.com/blog/post/1");
                request.Params("hit", 0);

                var router = new Router();

                router.Use("/proxy", (req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(0, hit);
                    Assert.AreEqual("/http://example.com/blog/post/1", req.RouteUrl);
                    next();
                });

                router.Handle(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(1, request.Params("hit"), "should ignore FQDN in path");
                });
            }

            {
                var request = MockRequest.Get("http://example.com/blog/post/1");
                request.Params("hit", 0);

                var router = new Router();

                router.Use("/blog", (req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(0, hit);
                    Assert.AreEqual("/post/1", req.RouteUrl);
                    Assert.AreEqual("/blog", req.BaseUrl);
                    next();
                });

                router.Handle(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(1, request.Params("hit"), "should adjust FQDN req.RouteUrl");
                });
            }

            {
                var request = MockRequest.Get("http://example.com/blog/post/1");
                request.Params("hit", 0);

                var router = new Router();

                router.Use((req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(0, hit);
                    Assert.AreEqual("/blog/post/1", req.RouteUrl);
                    Assert.AreEqual("", req.BaseUrl);
                    next();
                });

                router.Use("/blog", (req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(1, hit);
                    Assert.AreEqual("/post/1", req.RouteUrl);
                    Assert.AreEqual("/blog", req.BaseUrl);
                    next();
                });

                router.Handle(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(2, request.Params("hit"), "should adjust FQDN req.RouteUrl with multiple handlers");
                });
            }

            {
                var request = MockRequest.Get("http://example.com/blog/post/1");
                request.Params("hit", 0);

                var router = new Router();

                router.Use("/blog", (req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(0, hit);
                    Assert.AreEqual("/post/1", req.RouteUrl);
                    Assert.AreEqual("/blog", req.BaseUrl);
                    next();
                });

                router.Use("/blog", (req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(1, hit);
                    Assert.AreEqual("/post/1", req.RouteUrl);
                    Assert.AreEqual("/blog", req.BaseUrl);
                    next();
                });

                router.Use((req, res, next) =>
                {
                    var hit = req.ParamsAs<Int32>("hit");
                    req.Params("hit", hit + 1);
                    Assert.AreEqual(2, hit);
                    Assert.AreEqual("/blog/post/1", req.RouteUrl);
                    Assert.AreEqual("", req.BaseUrl);
                    next();
                });

                router.Handle(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(3, request.Params("hit"), "should adjust FQDN req.RouteUrl with multiple routed handlers");
                });
            }
        }

        [TestMethod]
        public void TestAll()
        {
            var router = new Router();

            var count = 0;
            router.All("/foo", (req, res, next) => { count++; });

            var url = "/foo?bar=baz";

            foreach (var method in MockRequest.Methods)
            {
                var request = new MockRequest() { HttpMethod = method, RawUrl = url };
                router.Handle(request, new MockResponse(), o => { });
            }

            Assert.AreEqual(MockRequest.Methods.Length, count, "should support using .All to capture all http verbs");
        }

        [TestMethod]
        public void TestUseAcceptArray()
        {
            var count = 0;
            var router = new Router();

            Middleware mw1 = (req, res, next) =>
            {
                Assert.AreEqual(1, ++count);
                next();
            };

            Middleware mw2 = (req, res, next) =>
            {
                Assert.AreEqual(2, ++count);
                next();
            };

            router.Use(mw1, mw2, (req, res, next) =>
            {
                Assert.AreEqual(3, ++count);
            });

            router.Handle(MockRequest.Get("/foo"), new MockResponse(), o => { });

            Assert.AreEqual(3, count);
        }

        [TestMethod]
        public void TestParamWithGet()
        {
            var router = new Router();

            router.Param("id", (req, res, next, id, name) =>
            {
                Assert.AreEqual(id, "123");
                next();
            });

            router.Get("/foo/:id/bar", (req, res, next) =>
            {
                Assert.AreEqual(req.Params("id"), "123");
            });

            router.Handle(MockRequest.Get("/foo/123/bar"), new MockResponse(), o =>
                Assert.Fail("should call param function when routing VERBS"));
        }

        [TestMethod]
        public void TestParamWithMiddleware()
        {
            var router = new Router();

            router.Param("id", (req, res, next, id, name) =>
            {
                Assert.AreEqual(id, "123");
                next();
            });

            router.Use("/foo/:id/bar", (req, res, next) =>
            {
                Assert.AreEqual(req.Params("id"), "123");
                Assert.AreEqual(req.RouteUrl, "/baz");
            });

            router.Handle(MockRequest.Get("/foo/123/bar/baz"), new MockResponse(), o => { });
        }

        [TestMethod]
        public void TestParamCallOnlyOncePerRequest()
        {
            var count = 0;
            var request = MockRequest.Get("/foo/bob/bar");
            var router = new Router();
            var sub = new Router();

            sub.Get("/bar", (req, res, next) => next());

            router.Param("user", (req, res, next, user, name) =>
            {
                count++;
                req.Params("_user", user);
                next();
            });

            router.Use("/foo/:user/", new Router());
            router.Use("/foo/:user/", sub);

            router.Handle(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.AreEqual(1, count, "should only call once per request");
                Assert.AreEqual("bob", request.Params("_user"));
                Assert.AreEqual("bob", request.Params("user"));
            });
        }

        [TestMethod]
        public void TestParamCallWhenValuesDiffer()
        {
            var count = 0;
            var request = MockRequest.Get("/foo/bob/bar");
            var router = new Router();
            var sub = new Router();

            sub.Get("/bar", (req, res, next) => next());

            router.Param("user", (req, res, next, user, name) =>
            {
                count++;
                req.Params("_user", user);
                next();
            });

            router.Use("/foo/:user/", new Router());
            router.Use("/:user/bob/", sub);

            router.Handle(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.AreEqual(2, count, "should call when values differ");
                Assert.AreEqual("foo", request.Params("_user"));
                Assert.AreEqual("foo", request.Params("user"));
            });
        }

        [TestMethod]
        public void TestNotMixParallelRequests()
        {
            var count = 0;
            var req1 = MockRequest.Get("/foo/50/bar");
            var req2 = MockRequest.Get("/foo/10/bar");
            var router = new Router();
            var sub = new Router();
            var tasks = new List<Task>();

            sub.Get("/bar", (req, res, next) => next());

            router.Param("ms", (req, res, next, val, name) =>
            {
                count++;
                var ms = Int32.Parse(val.ToString());
                req.Params("_ms", ms);
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(ms);
                    next();
                }));
            });

            router.Use("/foo/:ms/", new Router());
            router.Use("/foo/:ms/", sub);

            router.Handle(req1, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.AreEqual("50", req1.Params("ms"));
                Assert.AreEqual(50, req1.Params("_ms"));
                Assert.AreEqual("/foo/50/bar", req1.RawUrl);
            });

            router.Handle(req2, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.AreEqual("10", req2.Params("ms"));
                Assert.AreEqual(10, req2.Params("_ms"));
                Assert.AreEqual("/foo/10/bar", req2.RawUrl);
            });

            Task.WaitAll(tasks.ToArray());
            Assert.AreEqual(2, count, "should not mix requests");
        }
    }
}
