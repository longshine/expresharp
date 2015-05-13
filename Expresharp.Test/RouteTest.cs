using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Route = Expresharp.Express.Route;

namespace Expresharp
{
    [TestClass]
    public class RouteTest
    {
        [TestMethod]
        public void TestAll()
        {
            {
                var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
                var route = new Route("/foo");

                route.All((req, res, next) =>
                {
                    req.Params("called", true);
                    next();
                });

                route.Dispatch(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.IsTrue(request.ParamsAs<Boolean>("called"), "should add handler");
                });
            }

            {
                var count = 0;
                var methods = 0;
                var route = new Route("/foo");

                route.All((req, res, next) =>
                {
                    count++;
                    next();
                });

                foreach (var method in MockRequest.Methods)
                {
                    methods++;
                    var request = new MockRequest() { HttpMethod = method, RawUrl = "/" };
                    route.Dispatch(request, new MockResponse(), o =>
                    {
                        Exception err = o as Exception;
                        if (err != null)
                            Assert.Fail(err.Message);

                        if (methods == MockRequest.Methods.Length)
                            Assert.AreEqual(methods, count, "should handle VERBS");
                    });
                }
            }

            {
                var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
                var route = new Route("/foo");
                request.Params("count", 0);

                route.All((req, res, next) =>
                {
                    req.Params("count", req.ParamsAs<Int32>("count") + 1);
                    next();
                });

                route.All((req, res, next) =>
                {
                    req.Params("count", req.ParamsAs<Int32>("count") + 1);
                    next();
                });

                route.Dispatch(request, new MockResponse(), o =>
                {
                    Exception err = o as Exception;
                    if (err != null)
                        Assert.Fail(err.Message);
                    Assert.AreEqual(2, request.ParamsAs<Int32>("count"), "should stack");
                });
            }
        }

        [TestMethod]
        public void TestGet()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            route.Get((req, res, next) =>
            {
                req.Params("called", true);
                next();
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.IsTrue(request.ParamsAs<Boolean>("called"), "should support .Get");
            });
        }

        [TestMethod]
        public void TestPostOnly()
        {
            var request = new MockRequest() { HttpMethod = "POST", RawUrl = "/" };
            var route = new Route("");

            route.Get((req, res, next) =>
            {
                throw new Exception("not me!");
            });

            route.Post((req, res, next) =>
            {
                req.Params("called", true);
                next();
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.IsTrue(request.ParamsAs<Boolean>("called"), "should limit to just .VERB");
            });
        }

        [TestMethod]
        public void TestFallthrough()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            request.Params("order", String.Empty);

            route.Get((req, res, next) =>
            {
                request.Params("order", request.Params("order") + "a");
                next();
            });

            route.All((req, res, next) =>
            {
                request.Params("order", request.Params("order") + "b");
                next();
            });

            route.Get((req, res, next) =>
            {
                request.Params("order", request.Params("order") + "c");
                next();
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.AreEqual("abc", request.Params("order"), "should allow fallthrough");
            });
        }

        [TestMethod]
        public void TestHandleError()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            request.Params("order", String.Empty);

            route.All((req, res, next) =>
            {
                 next(new Exception("foobar"));
            });

            route.All((req, res, next) =>
            {
                request.Params("order", request.Params("order") + "0");
                next();
            });

            route.All((err, req, res, next) =>
            {
                request.Params("order", request.Params("order") + "a");
                next(err);
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                Assert.IsNotNull(err);
                Assert.AreEqual("foobar", err.Message, "should handle errors");
                Assert.AreEqual("a", request.Params("order"), "should handle errors");
            });
        }

        [TestMethod]
        public void TestHandleThrow()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            request.Params("order", String.Empty);

            route.All((req, res, next) =>
            {
                throw new Exception("foobar");
            });

            route.All((req, res, next) =>
            {
                request.Params("order", request.Params("order") + "0");
                next();
            });

            route.All((err, req, res, next) =>
            {
                request.Params("order", request.Params("order") + "a");
                next(err);
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                Assert.IsNotNull(err);
                Assert.AreEqual("foobar", err.Message, "should handle throw");
                Assert.AreEqual("a", request.Params("order"), "should handle throw");
            });
        }

        [TestMethod]
        public void TestHandleThrowInsideErrorHandler()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            route.Get((req, res, next) =>
            {
                throw new Exception("boom!");
            });

            route.Get((err, req, res, next) =>
            {
                throw new Exception("oops");
            });

            route.Get((err, req, res, next) =>
            {
                request.Params("message", err.Message);
                next();
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                if (err != null)
                    Assert.Fail(err.Message);
                Assert.AreEqual("oops", request.Params("message"), "should handle throwing inside error handlers");
            });
        }

        [TestMethod]
        public void TestHandleThrowInAll()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            route.All((req, res, next) =>
            {
                throw new Exception("boom!");
            });

            route.Dispatch(request, new MockResponse(), o =>
            {
                Exception err = o as Exception;
                Assert.IsNotNull(err);
                Assert.AreEqual("boom!", err.Message, "should handle throw in .All");
            });
        }

        [TestMethod]
        public void TestHandleSingleErrorHandler()
        {
            var request = new MockRequest() { HttpMethod = "GET", RawUrl = "/" };
            var route = new Route("");

            route.All((err, req, res, next) =>
            {
                Assert.Fail("this should not execute");
            });

            route.Dispatch(request, new MockResponse(), o => { });
        }
    }
}
