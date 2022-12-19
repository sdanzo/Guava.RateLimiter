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
    
    public static class LongMath
    {
        /// <summary>
        /// Returns the sum of <param name="a"></param>
        /// and <param name="b"></param>
        /// unless it would overflow or underflow in which case
        /// <code>long.MaxValue</code> or <code>long.MaxValue</code> is returned, respectively.
        ///
        /// since 20.0
        /// </summary>
        public static long SaturatedAdd(long a, long b)
        {
            var naiveSum = unchecked(a + b);
            if ((a ^ b) < 0 | (a ^ naiveSum) >= 0)
            {
                // If a and b have different signs or a has the same sign as the result then there was no
                // overflow, return.
                return naiveSum;
            }
            // we did over/under flow, if the sign is negative we should return MAX otherwise MIN
            return naiveSum < 0 ? long.MaxValue : long.MinValue;
        }
    }
}
