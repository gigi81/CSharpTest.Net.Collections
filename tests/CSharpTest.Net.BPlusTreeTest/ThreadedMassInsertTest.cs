#region Copyright 2012-2014 by Roger Knapp, Licensed under the Apache License, Version 2.0
/* Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CSharpTest.Net.Collections;
using CSharpTest.Net.IO;
using CSharpTest.Net.Serialization;
using NUnit.Framework;

namespace CSharpTest.Net.BPlusTree.Test
{
    [TestFixture]
    public class ThreadedMassInsertTest
    {
        static readonly ManualResetEvent mreStop = new ManualResetEvent(false);

        [Test]
        public void TestConcurrency()
        {
            mreStop.Reset();
            using(TempFile temp = new TempFile())
            {
                BPlusTree<Guid, TestInfo>.OptionsV2 options = new BPlusTree<Guid, TestInfo>.OptionsV2(
                    PrimitiveSerializer.Guid, new TestInfoSerializer());
                options.CalcBTreeOrder(16, 24);
                options.CreateFile = CreatePolicy.Always;
                options.FileName = temp.TempPath;
                using (BPlusTree<Guid, TestInfo> tree = new BPlusTree<Guid, TestInfo>(options))
                {
                    tree.EnableCount();
                    var actions = new List<Task>();
                    var tests = new Action<BPlusTree<Guid, TestInfo>>[] 
                    {
                        DeleteStuff, UpdateStuff, AddStuff, AddRanges, BulkyInserts,
                        FetchStuff, FetchStuff, FetchStuff, FetchStuff, FetchStuff
                    };

                    foreach (var t in tests)
                        actions.Add(Task.Run(()=>t(tree)));

                    do
                    {
                        Trace.TraceInformation("Dictionary.Count = {0}", tree.Count);
                        Thread.Sleep(1000);
                    } while (Debugger.IsAttached);

                    mreStop.Set();
                    for (int i = 0; i < actions.Count; i++)
                    {
                        actions[i].Wait(1000);
                    }

                    Trace.TraceInformation("Dictionary.Count = {0}", tree.Count);
                }
            }
        }

        void DeleteStuff(BPlusTree<Guid, TestInfo> tree)
        {
            while (!mreStop.WaitOne(0, false))
            {
                if (tree.Count > 1000)
                {
                    int limit = tree.Count - 1000;
                    foreach (Guid key in tree.Keys)
                    {
                        if (!tree.Remove(key))
                            throw new ApplicationException();
                        if (--limit <= 0)
                            break;
                    }
                }
                else
                    Thread.Sleep(1);
            }
        }

        void FetchStuff(BPlusTree<Guid, TestInfo> tree)
        {
            while (!mreStop.WaitOne(0, false))
            {
                foreach (Guid k in tree.Keys)
                {
                    TestInfo ti;
                    if (tree.TryGetValue(k, out ti) && ti.MyKey != k)
                        throw new ApplicationException();
                }
            }
        }

        void UpdateStuff(BPlusTree<Guid, TestInfo> tree)
        {
            while (!mreStop.WaitOne(0, false))
            {
                foreach (var pair in tree)
                {
                    bool updated = tree.TryUpdate(pair.Key, (k, v) => { v.UpdateCount++; return v; });
                    if (!updated && tree.ContainsKey(pair.Key))
                        throw new ApplicationException();
                }
            }
        }

        private void AddIdle(BPlusTree<Guid, TestInfo> tree)
        {
            int size = tree.Count;
            if (size > 100000)
                Thread.Sleep(size - 100000);
        }

        void AddStuff(BPlusTree<Guid, TestInfo> tree)
        {
            while (!mreStop.WaitOne(0, false))
            {
                foreach(var pair in CreateData(100))
                    tree.Add(pair.Key, pair.Value);
                AddIdle(tree);
            }
        }

        void AddRanges(BPlusTree<Guid, TestInfo> tree)
        {
            while (!mreStop.WaitOne(0, false))
            {
                tree.AddRange(CreateData(100));
                AddIdle(tree);
            }
        }

        void BulkyInserts(BPlusTree<Guid, TestInfo> tree)
        {
            while (!mreStop.WaitOne(0, false))
            {
                tree.BulkInsert(CreateData(100));
                AddIdle(tree);
                Thread.Sleep(100);
            }
        }

        static IEnumerable<KeyValuePair<Guid, TestInfo>> CreateData(int size)
        {
            for (int i = 0; i < size; i++)
            {
                Guid id = Guid.NewGuid();
                yield return new KeyValuePair<Guid, TestInfo>(id, new TestInfo(id));
            }
        }
    }
}