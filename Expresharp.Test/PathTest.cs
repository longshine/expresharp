using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Expresharp
{
    [TestClass]
    public class PathTest
    {
        [TestMethod]
        public void TestPathtoRegex()
        {
            TestPathtoRegex("/user/:id", @"^\/user\/(?:([^\/]+?))\/?$", new Express.PathKey[]
            {
                new Express.PathKey("id", false)
            });

            TestPathtoRegex("/user/:id([0-9]+)", @"^\/user\/(?:([0-9]+))\/?$", new Express.PathKey[]
            {
                new Express.PathKey("id", false)
            });

            TestPathtoRegex(@"/range/:range(\w+..\w+)", @"^\/range\/(?:(\w+\.\.\w+))\/?$", new Express.PathKey[]
            {
                new Express.PathKey("range", false)
            });
        }

        private void TestPathtoRegex(String path, String expectedRegex, IEnumerable<Express.PathKey> expectedKeys)
        {
            var keys = new List<Express.PathKey>();
            var regex = Express.PathtoRegex(path, keys, null);
            Assert.AreEqual(expectedRegex, regex.ToString());
            AreEqual(expectedKeys, keys, PathKeyComparer.Instance);
        }

        class PathKeyComparer : EqualityComparer<Express.PathKey>
        {
            public static readonly PathKeyComparer Instance = new PathKeyComparer();

            public override bool Equals(Express.PathKey x, Express.PathKey y)
            {
                return String.Equals(x.Name, y.Name) && x.Optional == y.Optional;
            }

            public override int GetHashCode(Express.PathKey obj)
            {
                return obj.Name.GetHashCode();
            }
        }

        static void AreEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T> comparer)
        {
            using (IEnumerator<T> t1 = expected.GetEnumerator(), t2 = actual.GetEnumerator())
            {
                while (t1.MoveNext() && t2.MoveNext())
                {
                    Assert.IsTrue(comparer.Equals(t1.Current, t2.Current));
                }
                Assert.IsFalse(t1.MoveNext() && t2.MoveNext());
            }
        }
    }
}
