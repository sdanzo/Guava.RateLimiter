using System.Diagnostics;
using System.Threading;

namespace Guava.RateLimiter
{
    /// <summary>
    /// Copyright (C) 2012 The Guava Authors
    /// 
    /// Licensed under the Apache License, Version 2.0 (the "License");
    /// you may not use this file except in compliance with the License.
    /// You may obtain a copy of the License at
    /// 
    /// http://www.apache.org/licenses/LICENSE-2.0
    /// 
    /// Unless required by applicable law or agreed to in writing, software
    /// distributed under the License is distributed on an "AS IS" BASIS,
    /// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    /// See the License for the specific language governing permissions and
    /// limitations under the License.
    /// </summary>

    public interface ISleepingStopwatch
    {
        long ReadMicros();
        void SleepMicrosUninterruptibly(long micros);
    }

    public sealed class SleepingStopwatch : ISleepingStopwatch
    {
        private SleepingStopwatch()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// We always hold the mutex when calling this.
        /// </summary>
        private readonly Stopwatch _stopwatch;

        public long ReadMicros()
        {
            if (!Stopwatch.IsHighResolution)
                return _stopwatch.ElapsedMilliseconds * 1000; //_stopwatch.ElapsedTicks / Stopwatch.Frequency;

            return (long)(1000000 * (double)_stopwatch.ElapsedTicks / Stopwatch.Frequency);
        }

        public void SleepMicrosUninterruptibly(long micros)
        {
            //converting microseconds to ticks
            var expectedTicks = _stopwatch.ElapsedTicks + micros * Stopwatch.Frequency / 1000000;//frequency = N of ticks per 1 second

            if (micros > 40000 || !Stopwatch.IsHighResolution)//32ms is the precision of DateTime which is used inside SpinUntil
            {
                //leaving some residual time after spinUntil to spin accurately
                SpinWait.SpinUntil(() => _stopwatch.ElapsedTicks >= expectedTicks, (int)(micros / 1000) - 10);
            }

            while (_stopwatch.ElapsedTicks < expectedTicks)
            {
                Thread.SpinWait(1);
            }
        }

        public static SleepingStopwatch CreateFromSystemTimer()
        {
            return new SleepingStopwatch();
        }
    }
}
