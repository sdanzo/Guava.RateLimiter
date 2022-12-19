using System;

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
    
    public enum TimeUnit
    {
        Nanoseconds,
        Microseconds,
        Milliseconds,
        Seconds,
        Minutes,
        Hours,
        Days
    }

    public static class TimeUnitExtensions
    {
        public static double ToMicros(this TimeUnit unit, double value)
        {
            switch (unit)
            {
                case TimeUnit.Nanoseconds:
                    return value/1000;
                case TimeUnit.Microseconds:
                    return value;
                case TimeUnit.Milliseconds:
                    return value*1000;
                case TimeUnit.Seconds:
                    return value*1000000;
                case TimeUnit.Minutes:
                    return value*60000000;
                case TimeUnit.Hours:
                    return value*3600000000;
                case TimeUnit.Days:
                    return value*86400000000;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }

        public static long ToMicros(this TimeUnit unit, long value)
        {
            switch (unit)
            {
                case TimeUnit.Nanoseconds:
                    return value/1000;
                case TimeUnit.Microseconds:
                    return value;
                case TimeUnit.Milliseconds:
                    return value*1000;
                case TimeUnit.Seconds:
                    return value*1000000;
                case TimeUnit.Minutes:
                    return value*60000000;
                case TimeUnit.Hours:
                    return value*3600000000;
                case TimeUnit.Days:
                    return value*86400000000;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }

        public static long ToMillis(this TimeUnit unit, long value)
        {
            switch (unit)
            {
                case TimeUnit.Nanoseconds:
                    return value/1000000;
                case TimeUnit.Microseconds:
                    return value / 1000;
                case TimeUnit.Milliseconds:
                    return value;
                case TimeUnit.Seconds:
                    return value*1000;
                case TimeUnit.Minutes:
                    return value*60000;
                case TimeUnit.Hours:
                    return value*3600000;
                case TimeUnit.Days:
                    return value*86400000;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }

        public static long ToNanos(this TimeUnit unit, long value)
        {
            switch (unit)
            {
                case TimeUnit.Nanoseconds:
                    return value;
                case TimeUnit.Microseconds:
                    return value * 1000;
                case TimeUnit.Milliseconds:
                    return value * 1000000;
                case TimeUnit.Seconds:
                    return value * 1000000000;
                case TimeUnit.Minutes:
                    return value * 60000000000;
                case TimeUnit.Hours:
                    return value * 3600000000000;
                case TimeUnit.Days:
                    return value * 86400000000000;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }
    }
}