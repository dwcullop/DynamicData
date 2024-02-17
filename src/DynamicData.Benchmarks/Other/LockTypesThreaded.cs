// Copyright (c) 2011-2019 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace DynamicData.Benchmarks.Other
{
    [MemoryDiagnoser]
    [MarkdownExporterAttribute.GitHub]
    public class LockTypesThreaded
    {
        [Benchmark(Baseline = true)]
        [Arguments(10000, 2)]
        public async Task Monitor(int count, int threads)
        {
            var locker = new object();
            var value = 0;

            int Run(int loops)
            {
                while (loops-- > 0)
                {
                    lock (locker!)
                    {
                        value++;
                    }
                }

                return value;
            }

            var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() => Run(count))).ToArray();

            await Task.WhenAll(tasks);
        }

        [Benchmark()]
        [Arguments(10000, 2)]
        public async Task Semaphore(int count, int threads)
        {
            var locker = new Semaphore(1, 1);
            var value = 0;

            int Run(int loops)
            {
                while (loops-- > 0)
                {
                    locker.WaitOne();
                    try
                    {
                        value++;
                    }
                    finally
                    {
                        locker.Release();
                    }
                }

                return value;
            }

            var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() => Run(count))).ToArray();

            await Task.WhenAll(tasks);
        }

        [Benchmark()]
        [Arguments(10000, 2)]
        public async Task SemaphoreSlim(int count, int threads)
        {
            var locker = new SemaphoreSlim(1, 1);
            var value = 0;

            int Run(int loops)
            {
                while (loops-- > 0)
                {
                    locker.Wait();
                    try
                    {
                        value++;
                    }
                    finally
                    {
                        locker.Release();
                    }
                }

                return value;
            }

            var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() => Run(count))).ToArray();

            await Task.WhenAll(tasks);
        }
    }
}
