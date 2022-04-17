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

        private static Random random = new Random();

        public static string GetRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
