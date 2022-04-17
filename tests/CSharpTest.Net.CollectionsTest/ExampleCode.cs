using System;
using System.IO;
using System.Linq;
using System.Threading;
using CSharpTest.Net.Collections;
using CSharpTest.Net.Serialization;
using NUnit.Framework;

namespace CSharpTest.Net.Library.Test
{
    [TestFixture]
    public class ExampleCode
    {
        private class Counts
        {
            public int Queued = 0, Dequeued = 0;
        }

        [Test]
        public void LurchTableDemo()
        {
            var counts = new Counts();
            //Queue where producer helps when queue is full
            var queue = new LurchTable<string, int>(LurchTableOrder.Insertion, 10);
            var stop = new ManualResetEvent(false);

            queue.ItemRemoved += kv =>
            {
                Interlocked.Increment(ref counts.Dequeued);
                Console.WriteLine("[{0}] - {1}", Thread.CurrentThread.ManagedThreadId, kv.Key);
            };

            //start some threads eating queue:
            var thread = new Thread(() =>
            {
                while (!stop.WaitOne(10))
                {
                    while (queue.TryDequeue(out var _))
                        continue;
                }
            })
            { Name = "worker", IsBackground = true };
            thread.Start();

            var names = Enumerable.Range(1, 100).Select(i => GetRandomString(100)).ToArray();
            for(var i=0; i < 100; i++)
            {
                foreach (var name in names)
                {
                    Interlocked.Increment(ref counts.Queued);
                    queue[name] = i;
                }
            }

            //help empty the queue
            while (queue.TryDequeue(out var _))
                continue;

            //shutdown
            stop.Set();
            thread.Join();
            queue.Dispose();

            Assert.AreEqual(counts.Queued, counts.Dequeued);
        }

        [Test]
        public void BPlusTreeDemo()
        {
            var options = new BPlusTree<string, DateTime>.OptionsV2(PrimitiveSerializer.String, PrimitiveSerializer.DateTime);
            options.CalcBTreeOrder(16, 24);
            options.CreateFile = CreatePolicy.Always;
            options.FileName = Path.GetTempFileName();

            var files = Enumerable.Range(1, 100).Select(i => new { FullName = GetRandomString(100), LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(random.Next(1000)) }).ToArray();

            using (var tree = new BPlusTree<string, DateTime>(options))
            {
                foreach (var file in files)
                    tree.Add(file.FullName, file.LastWriteTimeUtc);
            }

            options.CreateFile = CreatePolicy.Never;

            using (var tree = new BPlusTree<string, DateTime>(options))
            {
                tree.EnableCount();

                foreach (var file in files)
                {
                    Assert.IsTrue(tree.TryGetValue(file.FullName, out var cmpDate));
                    Assert.AreEqual(file.LastWriteTimeUtc, cmpDate);
                    tree.Remove(file.FullName);
                }

                Assert.AreEqual(0, tree.Count);
            }
        }

        [Test]
        public void UserPost_IComparable()
        {
            var now = DateTime.UtcNow;

            var user1 = new UserPost
            {
                UserId = Guid.NewGuid(),
                PostTime = now
            };

            var user2 = new UserPost
            {
                UserId = user1.UserId,
                PostTime = now
            };

            Assert.AreEqual(0, user1.CompareTo(user2));
        }

        [Test]
        public void BplusTreeDemo_WithMultifieldKey()
        {
            var now = DateTime.UtcNow;

            var options = new BPlusTree<UserPost, Guid>.OptionsV2(new UserPostKeySerializer(), PrimitiveSerializer.Guid);
            options.CalcBTreeOrder(16, 24);
            options.CreateFile = CreatePolicy.Always;
            options.FileName = Path.GetTempFileName();

            var users = Enumerable.Range(0, 10000 / 10).Select(i => Guid.NewGuid()).ToArray();
            var data = Enumerable.Range(0, 10000).Select(i => CreateData(i, users, now)).ToArray();

            using (var tree = new BPlusTree<UserPost, Guid>(options))
            {
                foreach (var d in data)
                    tree.Add(d, Guid.NewGuid());
            }

            options.CreateFile = CreatePolicy.Never;

            using (var tree = new BPlusTree<UserPost, Guid>(options))
            {
                tree.EnableCount();

                foreach (var user in users)
                {
                    //get last 5 minutes of data
                    var range = tree.EnumerateRange(new UserPost { UserId = user, PostTime = now.AddMinutes(-5) }, new UserPost { UserId = user, PostTime = now });

                    Assert.AreEqual(6, range.Count());
                }
            }
        }

        private UserPost CreateData(int i, Guid[] users, DateTime now)
        {
            return new UserPost
            {
                UserId = users[i / 10],
                PostTime = now.AddMinutes(-(i % 10))
            };
        }

        private static Random random = new Random();

        public static string GetRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public class UserPost : IComparable<UserPost>
        {
            public Guid UserId { get; set; }

            public DateTime PostTime { get; set; }

            public int CompareTo(UserPost other)
            {
                var user = this.UserId.CompareTo(other.UserId);
                if(user != 0)
                    return user;

                return this.PostTime.CompareTo(other.PostTime);
            }
        }

        public class UserPostKeySerializer : ISerializer<UserPost>
        {
            public UserPost ReadFrom(Stream stream)
            {
                var ret = new UserPost
                {
                    UserId = PrimitiveSerializer.Guid.ReadFrom(stream),
                    PostTime = PrimitiveSerializer.DateTime.ReadFrom(stream)
                };

                return ret;
            }

            public void WriteTo(UserPost value, Stream stream)
            {
                PrimitiveSerializer.Guid.WriteTo(value.UserId, stream);
                PrimitiveSerializer.DateTime.WriteTo(value.PostTime, stream);
            }
        }
    }
}
