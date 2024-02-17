// Copyright (c) 2011-2019 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Threading;
using BenchmarkDotNet.Attributes;

namespace DynamicData.Benchmarks.Other
{
    [MemoryDiagnoser]
    [MarkdownExporterAttribute.GitHub]
    public class LockTypes
    {
        [Benchmark(Baseline = true)]
        [Arguments(10000)]
        public void Monitor(int count)
        {
            var locker = new object();
            var value = 0;

            while (count-- > 0)
            {
                lock (locker!)
                {
                    value++;
                }
            }
        }

        [Benchmark()]
        [Arguments(10000)]
        public void Semaphore(int count)
        {
            var locker = new Semaphore(1, 1);
            var value = 0;

            while (count-- > 0)
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
        }

        [Benchmark()]
        [Arguments(10000)]
        public void SemaphoreSlim(int count)
        {
            var locker = new SemaphoreSlim(1, 1);
            var value = 0;

            while (count-- > 0)
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
        }
    }
}
