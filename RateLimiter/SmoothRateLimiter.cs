﻿using System;

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

    /// <remarks>
    /// How is the RateLimiter designed, and why?
    ///
    /// The primary feature of a RateLimiter is its "stable rate", the maximum rate that
    /// is should allow at normal conditions. This is enforced by "throttling" incoming
    /// requests as needed, i.e. compute, for an incoming request, the appropriate throttle time,
    /// and make the calling thread wait as much.
    ///
    /// The simplest way to maintain a rate of QPS is to keep the timestamp of the last
    /// granted request, and ensure that (1/QPS) seconds have elapsed since then. For example,
    /// for a rate of QPS=5 (5 tokens per second), if we ensure that a request isn't granted
    /// earlier than 200ms after the last one, then we achieve the intended rate.
    /// If a request comes and the last request was granted only 100ms ago, then we wait for
    /// another 100ms. At this rate, serving 15 fresh permits (i.e. for an acquire(15) request)
    /// naturally takes 3 seconds.
    ///
    /// It is important to realize that such a RateLimiter has a very superficial memory
    /// of the past: it only remembers the last request. What if the RateLimiter was unused for
    /// a long period of time, then a request arrived and was immediately granted?
    /// This RateLimiter would immediately forget about that past underutilization. This may
    /// result in either underutilization or overflow, depending on the real world consequences
    /// of not using the expected rate.
    ///
    /// Past underutilization could mean that excess resources are available. Then, the RateLimiter
    /// should speed up for a while, to take advantage of these resources. This is important
    /// when the rate is applied to networking (limiting bandwidth), where past underutilization
    /// typically translates to "almost empty buffers", which can be filled immediately.
    ///
    /// On the other hand, past underutilization could mean that "the server responsible for
    /// handling the request has become less ready for future requests", i.e. its caches become
    /// stale, and requests become more likely to trigger expensive operations (a more extreme
    /// case of this example is when a server has just booted, and it is mostly busy with getting
    /// itself up to speed).
    ///
    /// To deal with such scenarios, we add an extra dimension, that of "past underutilization",
    /// modeled by "storedPermits" variable. This variable is zero when there is no
    /// underutilization, and it can grow up to maxStoredPermits, for sufficiently large
    /// underutilization. So, the requested permits, by an invocation acquire(permits),
    /// are served from:
    /// - stored permits (if available)
    /// - fresh permits (for any remaining permits)
    ///
    /// How this works is best explained with an example:
    ///
    /// For a RateLimiter that produces 1 token per second, every second
    /// that goes by with the RateLimiter being unused, we increase storedPermits by 1.
    /// Say we leave the RateLimiter unused for 10 seconds (i.e., we expected a request at time
    /// X, but we are at time X + 10 seconds before a request actually arrives; this is
    /// also related to the point made in the last paragraph), thus storedPermits
    /// becomes 10.0 (assuming maxStoredPermits >= 10.0). At that point, a request of acquire(3)
    /// arrives. We serve this request out of storedPermits, and reduce that to 7.0 (how this is
    /// translated to throttling time is discussed later). Immediately after, assume that an
    /// acquire(10) request arriving. We serve the request partly from storedPermits,
    /// using all the remaining 7.0 permits, and the remaining 3.0, we serve them by fresh permits
    /// produced by the rate limiter.
    ///
    /// We already know how much time it takes to serve 3 fresh permits: if the rate is
    /// "1 token per second", then this will take 3 seconds. But what does it mean to serve 7
    /// stored permits? As explained above, there is no unique answer. If we are primarily
    /// interested to deal with underutilization, then we want stored permits to be given out
    /// /faster/ than fresh ones, because underutilization = free resources for the taking.
    /// If we are primarily interested to deal with overflow, then stored permits could
    /// be given out /slower/ than fresh ones. Thus, we require a (different in each case)
    /// function that translates storedPermits to throtting time.
    ///
    /// This role is played by storedPermitsToWaitTime(double storedPermits, double permitsToTake).
    /// The underlying model is a continuous function mapping storedPermits
    /// (from 0.0 to maxStoredPermits) onto the 1/rate (i.e. intervals) that is effective at the given
    /// storedPermits. "storedPermits" essentially measure unused time; we spend unused time
    /// buying/storing permits. Rate is "permits / time", thus "1 / rate = time / permits".
    /// Thus, "1/rate" (time / permits) times "permits" gives time, i.e., integrals on this
    /// function (which is what storedPermitsToWaitTime() computes) correspond to minimum intervals
    /// between subsequent requests, for the specified number of requested permits.
    ///
    /// Here is an example of storedPermitsToWaitTime:
    /// If storedPermits == 10.0, and we want 3 permits, we take them from storedPermits,
    /// reducing them to 7.0, and compute the throttling for these as a call to
    /// storedPermitsToWaitTime(storedPermits = 10.0, permitsToTake = 3.0), which will
    /// evaluate the integral of the function from 7.0 to 10.0.
    ///
    /// Using integrals guarantees that the effect of a single acquire(3) is equivalent
    /// to { acquire(1); acquire(1); acquire(1); }, or { acquire(2); acquire(1); }, etc,
    /// since the integral of the function in [7.0, 10.0] is equivalent to the sum of the
    /// integrals of [7.0, 8.0], [8.0, 9.0], [9.0, 10.0] (and so on), no matter
    /// what the function is. This guarantees that we handle correctly requests of varying weight
    /// (permits), /no matter/ what the actual function is - so we can tweak the latter freely.
    /// (The only requirement, obviously, is that we can compute its integrals).
    ///
    /// Note well that if, for this function, we chose a horizontal line, at height of exactly
    /// (1/QPS), then the effect of the function is non-existent: we serve storedPermits at
    /// exactly the same cost as fresh ones (1/QPS is the cost for each). We use this trick later.
    ///
    /// If we pick a function that goes /below/ that horizontal line, it means that we reduce
    /// the area of the function, thus time. Thus, the RateLimiter becomes /faster/ after a
    /// period of underutilization. If, on the other hand, we pick a function that
    /// goes /above/ that horizontal line, then it means that the area (time) is increased,
    /// thus storedPermits are more costly than fresh permits, thus the RateLimiter becomes
    /// /slower/ after a period of underutilization.
    ///
    /// Last, but not least: consider a RateLimiter with rate of 1 permit per second, currently
    /// completely unused, and an expensive acquire(100) request comes. It would be nonsensical
    /// to just wait for 100 seconds, and /then/ start the actual task. Why wait without doing
    /// anything? A much better approach is to /allow/ the request right away (as if it was an
    /// acquire(1) request instead), and postpone /subsequent/ requests as needed. In this version,
    /// we allow starting the task immediately, and postpone by 100 seconds future requests,
    /// thus we allow for work to get done in the meantime instead of waiting idly.
    ///
    /// This has important consequences: it means that the RateLimiter doesn't remember the time
    /// of the _last_ request, but it remembers the (expected) time of the _next_ request. This
    /// also enables us to tell immediately (see tryAcquire(timeout)) whether a particular
    /// timeout is enough to get us to the point of the next scheduling time, since we always
    /// maintain that. And what we mean by "an unused RateLimiter" is also defined by that
    /// notion: when we observe that the "expected arrival time of the next request" is actually
    /// in the past, then the difference (now - past) is the amount of time that the RateLimiter
    /// was formally unused, and it is that amount of time which we translate to storedPermits.
    /// (We increase storedPermits with the amount of permits that would have been produced
    /// in that idle time). So, if rate == 1 permit per second, and arrivals come exactly
    /// one second after the previous, then storedPermits is _never_ increased -- we would only
    /// increase it for arrivals _later_ than the expected one second.
    /// </remarks>
    public abstract class SmoothRateLimiter : RateLimiter
    {
        ///<remarks>
        /// This implements the following function where coldInterval = coldFactor * stableInterval.
        ///
        ///          ^ throttling
        ///          |
        ///    cold  +                  /
        /// interval |                 /.
        ///          |                / .
        ///          |               /  .   - "warmup period" is the area of the trapezoid between
        ///          |              /   .       thresholdPermits and maxPermits
        ///          |             /    .
        ///          |            /     .
        ///          |           /      .
        ///   stable +----------/  WARM .
        /// interval |          .   UP  .
        ///          |          . PERIOD.
        ///          |          .       .
        ///        0 +----------+-------+--------------> storedPermits
        ///          0 thresholdPermits maxPermits
        /// Before going into the details of this particular function, let's keep in mind the basics:
        /// 1) The state of the RateLimiter (storedPermits) is a vertical line in this figure.
        /// 2) When the RateLimiter is not used, this goes right (up to maxPermits)
        /// 3) When the RateLimiter is used, this goes left (down to zero), since if we have storedPermits,
        ///    we serve from those first
        /// 4) When _unused_, we go right at a constant rate! The rate at which we move to
        ///    the right is chosen as maxPermits / warmupPeriod.  This ensures that the time it takes to
        ///    go from 0 to maxPermits is equal to warmupPeriod.
        /// 5) When _used_, the time it takes, as explained in the introductory class note, is
        ///    equal to the integral of our function, between X permits and X-K permits, assuming
        ///    we want to spend K saved permits.
        ///
        ///    In summary, the time it takes to move to the left (spend K permits), is equal to the
        ///    area of the function of width == K.
        ///
        ///    Assuming we have saturated demand, the time to go from maxPermits to thresholdPermits is
        ///    equal to warmupPeriod.  And the time to go from thresholdPermits to 0 is
        ///    warmupPeriod/2.  (The reason that this is warmupPeriod/2 is to maintain the behavior of
        ///    the original implementation where coldFactor was hard coded as 3.)
        ///
        ///  It remains to calculate thresholdsPermits and maxPermits.
        ///
        ///  - The time to go from thresholdPermits to 0 is equal to the integral of the function between
        ///    0 and thresholdPermits.  This is thresholdPermits * stableIntervals.  By (5) it is also
        ///    equal to warmupPeriod/2.  Therefore
        ///
        ///        thresholdPermits = 0.5 * warmupPeriod / stableInterval.
        ///
        ///  - The time to go from maxPermits to thresholdPermits is equal to the integral of the function
        ///    between thresholdPermits and maxPermits.  This is the area of the pictured trapezoid, and it
        ///    is equal to 0.5 * (stableInterval + coldInterval) * (maxPermits - thresholdPermits).  It is
        ///    also equal to warmupPeriod, so
        ///
        ///        maxPermits = thresholdPermits + 2 * warmupPeriod / (stableInterval + coldInterval).
        /// </remarks>
        public sealed class SmoothWarmingUp : SmoothRateLimiter
        {
            /// <summary>
            /// The slope of the line from the stable interval (when permits == 0), to the cold interval
            /// (when permits == maxPermits)
            /// </summary>
            private readonly long _warmupPeriodMicros;
            private double _slope;
            private double _thresholdPermits;
            private readonly double _coldFactor;

            public SmoothWarmingUp(ISleepingStopwatch stopwatch, long warmupPeriod, TimeUnit timeUnit, double coldFactor) : base(stopwatch)
            {
                _warmupPeriodMicros = timeUnit.ToMicros(warmupPeriod);
                _coldFactor = coldFactor;
            }

            protected override void DoSetRate(double permitsPerSecond, double stableIntervalMicros)
            {
                var oldMaxPermits = _maxPermits;
                var coldIntervalMicros = stableIntervalMicros * _coldFactor;
                _thresholdPermits = 0.5 * _warmupPeriodMicros / stableIntervalMicros;
                _maxPermits = _thresholdPermits + 2.0 * _warmupPeriodMicros / (stableIntervalMicros + coldIntervalMicros);
                _slope = (coldIntervalMicros - stableIntervalMicros) / (_maxPermits - _thresholdPermits);

                if (double.IsPositiveInfinity(oldMaxPermits))
                {
                    // if we don't special-case this, we would get storedPermits == NaN, below
                    _storedPermits = 0.0;
                }
                else
                {
                    _storedPermits = (oldMaxPermits == 0.0)
                        ? _maxPermits // initial state is cold
                        : _storedPermits * _maxPermits / oldMaxPermits;
                }
            }

            protected override long StoredPermitsToWaitTime(double storedPermits, double permitsToTake)
            {
                var availablePermitsAboveThreshold = storedPermits - _thresholdPermits;
                var micros = 0L;

                // measuring the integral on the right part of the function (the climbing line)
                if (availablePermitsAboveThreshold > 0.0)
                {
                    double permitsAboveThresholdToTake = Math.Min(availablePermitsAboveThreshold, permitsToTake);
                    // TODO(cpovirk): Figure out a good name for this variable.
                    double length = PermitsToTime(availablePermitsAboveThreshold)
                            + PermitsToTime(availablePermitsAboveThreshold - permitsAboveThresholdToTake);
                    micros = (long)(permitsAboveThresholdToTake * length / 2.0);
                    permitsToTake -= permitsAboveThresholdToTake;
                }

                // measuring the integral on the left part of the function (the horizontal line)
                micros += (long)(_stableIntervalMicros * permitsToTake);
                return micros;
            }

            private double PermitsToTime(double permits)
            {
                return _stableIntervalMicros + permits * _slope;
            }

            protected override double CoolDownIntervalMicros()
            {
                return _warmupPeriodMicros / _maxPermits;
            }
        }

        ///<summary>
        /// This implements a "bursty" RateLimiter, where storedPermits are translated to zero throttling.
        /// <para>
        /// The maximum number of permits that can be saved (when the RateLimiter is
        /// unused) is defined in terms of time, in this sense:
        /// </para>
        /// If a RateLimiter is 2qps, and this time is specified as 10 seconds, we can save up to 2 * 10 = 20 permits. 
        ///</summary>
        public sealed class SmoothBursty : SmoothRateLimiter
        {
            ///<summary>
            /// The work (permits) of how many seconds can be saved up if this RateLimiter is unused?
            ///</summary>
            private readonly double _maxBurstSeconds;

            public SmoothBursty(ISleepingStopwatch stopwatch, double maxBurstSeconds) : base(stopwatch)
            {
                _maxBurstSeconds = maxBurstSeconds;
            }

            protected override void DoSetRate(double permitsPerSecond, double stableIntervalMicros)
            {
                var oldMaxPermits = _maxPermits;
                _maxPermits = _maxBurstSeconds * permitsPerSecond;
                if (double.IsPositiveInfinity(oldMaxPermits))
                {
                    // if we don't special-case this, we would get storedPermits == NaN, below
                    _storedPermits = _maxPermits;
                }
                else
                {
                    _storedPermits = (oldMaxPermits == 0.0)
                        ? 0.0 // initial state
                        : _storedPermits * _maxPermits / oldMaxPermits;
                }
            }

            protected override long StoredPermitsToWaitTime(double storedPermits, double permitsToTake)
            {
                return 0L;
            }

            protected override double CoolDownIntervalMicros()
            {
                return _stableIntervalMicros;
            }
        }

        ///<summary>
        /// The currently stored permits.
        ///</summary>
        private double _storedPermits;

        ///<summary>
        /// The maximum number of stored permits.
        ///</summary>
        private double _maxPermits;
        
        ///<summary>
        /// The interval between two unit requests, at our stable rate. E.g., a stable rate of 5 permits
        /// per second has a stable interval of 200ms.
        ///</summary>
        private double _stableIntervalMicros;

        ///<summary>
        /// The time when the next request (no matter its size) will be granted. After granting a
        /// request, this is pushed further in the future. Large requests push this further than small
        /// requests.
        ///</summary>
        private long _nextFreeTicketMicros; // could be either in the past or future

        private SmoothRateLimiter(ISleepingStopwatch stopwatch) : base(stopwatch) { }

        protected sealed override void DoSetRate(double permitsPerSecond, long nowMicros)
        {
            Resync(nowMicros);
            var stableIntervalMicros = TimeUnit.Seconds.ToMicros(1L) / permitsPerSecond;
            _stableIntervalMicros = stableIntervalMicros;
            DoSetRate(permitsPerSecond, stableIntervalMicros);
        }

        protected abstract void DoSetRate(double permitsPerSecond, double stableIntervalMicros);

        protected sealed override double DoGetRate()
        {
            return TimeUnit.Seconds.ToMicros(1L) / _stableIntervalMicros;
        }

        protected sealed override long QueryEarliestAvailable(long nowMicros)
        {
            return _nextFreeTicketMicros;
        }

        protected sealed override long ReserveEarliestAvailable(int requiredPermits, long nowMicros)
        {
            Resync(nowMicros);
            var returnValue = _nextFreeTicketMicros;
            var storedPermitsToSpend = Math.Min(requiredPermits, _storedPermits);
            var freshPermits = requiredPermits - storedPermitsToSpend;
            var waitMicros = StoredPermitsToWaitTime(_storedPermits, storedPermitsToSpend)
                              + (long)(freshPermits * _stableIntervalMicros);

            _nextFreeTicketMicros = LongMath.SaturatedAdd(_nextFreeTicketMicros, waitMicros);
            _storedPermits -= storedPermitsToSpend;
            return returnValue;
        }

        ///<remarks>
        /// Translates a specified portion of our currently stored permits which we want to
        /// spend/acquire, into a throttling time. Conceptually, this evaluates the integral
        /// of the underlying function we use, for the range of
        /// [(storedPermits - permitsToTake), storedPermits].
        /// 
        /// This always holds: <coode> 0 &lt;= permitsToTake &lt;= storedPermits </coode>
        ///</remarks>
        protected abstract long StoredPermitsToWaitTime(double storedPermits, double permitsToTake);

        ///<summary>
        /// Returns the number of microseconds during cool down that we have to wait to get a new permit.
        ///</summary>
        protected abstract double CoolDownIntervalMicros();

        private void Resync(long nowMicros)
        {
            // if nextFreeTicket is in the past, resync to now
            if (nowMicros > _nextFreeTicketMicros)
            {
                double newPermits = (nowMicros - _nextFreeTicketMicros) / CoolDownIntervalMicros();
                _storedPermits = Math.Min(_maxPermits, _storedPermits + newPermits);
                _nextFreeTicketMicros = nowMicros;
            }
        }
    }
}