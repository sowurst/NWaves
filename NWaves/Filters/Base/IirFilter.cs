﻿using System;
using System.Collections.Generic;
using System.Linq;
using NWaves.Signals;

namespace NWaves.Filters.Base
{
    /// <summary>
    /// Class representing Infinite Impulse Response filters
    /// </summary>
    public class IirFilter : LtiFilter
    {
        /// <summary>
        /// Denominator part coefficients in filter's transfer function 
        /// (recursive part in difference equations)
        /// </summary>
        public double[] A
        {
            get { return _a; }
            set
            {
                _a = value;
                Normalize();
            }
        }
        private double[] _a;

        /// <summary>
        /// Numerator part coefficients in filter's transfer function 
        /// (non-recursive part in difference equations)
        /// </summary>
        public double[] B
        {
            get { return _b; }
            set { _b = value; }
        }
        private double[] _b;

        /// <summary>
        /// If _a.Length + _b.Length exceeds this value, 
        /// the filtering code will use a circular buffer.
        /// </summary>
        public const int FilterSizeForOptimizedProcessing = 64;

        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public IirFilter()
        {
            ImpulseResponseLength = DefaultImpulseResponseLength;
        }

        /// <summary>
        /// Parameterized constructor
        /// </summary>
        /// <param name="b">TF numerator coefficients</param>
        /// <param name="a">TF denominator coefficients</param>
        /// <param name="impulseResponseLength">Length of truncated impulse response</param>
        public IirFilter(IEnumerable<double> b, 
                         IEnumerable<double> a,
                         int impulseResponseLength = DefaultImpulseResponseLength)
        {
            B = b.ToArray();
            A = a.ToArray();
            ImpulseResponseLength = impulseResponseLength;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="filteringOptions"></param>
        /// <returns></returns>
        public override DiscreteSignal ApplyTo(DiscreteSignal signal,
                                               FilteringOptions filteringOptions = FilteringOptions.Auto)
        {
            switch (filteringOptions)
            {
                case FilteringOptions.Auto:
                {
                    return _a.Length + _b.Length <= FilterSizeForOptimizedProcessing ?
                        ApplyFilterDirectly(signal) : 
                        ApplyFilterCircularBuffer(signal);
                }
                case FilteringOptions.DifferenceEquation:
                {
                    return ApplyFilterDirectly(signal);
                }

                // Currently just return copy for any other options
                default:
                    return signal.Copy();
                    // Operation.OverlapAdd(signal, Transform.Fft(ImpulseResponse));
            }
        }

        /// <summary>
        /// The most straightforward implementation of the difference equation:
        /// code the difference equation as it is
        /// </summary>
        /// <param name="signal"></param>
        /// <returns></returns>
        public DiscreteSignal ApplyFilterDirectly(DiscreteSignal signal)
        {
            var input = signal.Samples;

            var samples = new double[input.Length];

            for (var n = 0; n < input.Length; n++)
            {
                for (var k = 0; k < _b.Length; k++)
                {
                    if (n >= k) samples[n] += _b[k] * input[n - k];
                }
                for (var m = 1; m < _a.Length; m++)
                {
                    if (n >= m) samples[n] -= _a[m] * samples[n - m];
                }
            }

            return new DiscreteSignal(signal.SamplingRate, samples);
        }

        /// <summary>
        /// Quite inefficient implementation of filtering in time domain:
        /// use linear buffers for recursive and non-recursive delay lines.
        /// </summary>
        /// <param name="signal"></param>
        /// <returns></returns>        
        public DiscreteSignal ApplyFilterLinearBuffer(DiscreteSignal signal)
        {
            var input = signal.Samples;

            var samples = new double[input.Length];

            // buffers for delay lines:
            var wb = new double[_b.Length];
            var wa = new double[_a.Length];

            for (var i = 0; i < input.Length; i++)
            {
                wb[0] = input[i];

                for (var k = 0; k < _b.Length; k++)
                {
                    samples[i] += _b[k] * wb[k];
                }

                for (var m = 1; m < _a.Length; m++)
                {
                    samples[i] -= _a[m] * wa[m - 1];
                }

                // update delay line

                for (var k = _b.Length - 1; k > 0; k--)
                {
                    wb[k] = wb[k - 1];
                }
                for (var m = _a.Length - 1; m > 0; m--)
                {
                    wa[m] = wa[m - 1];
                }

                wa[0] = samples[i];
            }

            return new DiscreteSignal(signal.SamplingRate, samples);
        }

        /// <summary>
        /// More efficient implementation of filtering in time domain:
        /// use circular buffers for recursive and non-recursive delay lines.
        /// </summary>
        /// <param name="signal"></param>
        /// <returns></returns>        
        public DiscreteSignal ApplyFilterCircularBuffer(DiscreteSignal signal)
        {
            var input = signal.Samples;

            var samples = new double[input.Length];

            // buffers for delay lines:
            var wb = new double[_b.Length];
            var wa = new double[_a.Length];

            var wbpos = wb.Length - 1;
            var wapos = wa.Length - 1;
            
            for (var n = 0; n < input.Length; n++)
            {
                wb[wbpos] = input[n];

                var pos = 0;
                for (var k = wbpos; k < _b.Length; k++)
                {
                    samples[n] += _b[pos++] * wb[k];
                }
                for (var k = 0; k < wbpos; k++)
                {
                    samples[n] += _b[pos++] * wb[k];
                }

                pos = 1;
                for (var m = wapos + 1; m < _a.Length; m++)
                {
                    samples[n] -= _a[pos++] * wa[m];
                }
                for (var m = 0; m < wapos; m++)
                {
                    samples[n] -= _a[pos++] * wa[m];
                }

                wa[wapos] = samples[n];

                wbpos--;
                if (wbpos < 0) wbpos = wb.Length - 1;

                wapos--;
                if (wapos < 0) wapos = wa.Length - 1;
            }

            return new DiscreteSignal(signal.SamplingRate, samples);
        }

        /// <summary>
        /// Divide all filter coefficients by _a[0]
        /// </summary>
        private void Normalize()
        {
            var first = _a[0];

            if (Math.Abs(first - 0.0) < 1e-12)
            {
                throw new ArgumentException("The first A coefficient can not be zero!");
            }

            if (Math.Abs(first - 1.0) >= 1e-12)
            {
                return;
            }

            for (var i = 0; i < _a.Length; i++)
            {
                _a[i] = _a[i] / first;
            }

            for (var i = 0; i < _b.Length; i++)
            {
                _b[i] = _b[i] / first;
            }
        }
    }
}