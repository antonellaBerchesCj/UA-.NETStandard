/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class CountAggregateCalculator : AggregateCalculator
    {
        /// <summary>
        /// Initializes the aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public CountAggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration,
            ITelemetryContext telemetry)
            : base(aggregateId, startTime, endTime, processingInterval, stepped, configuration, telemetry)
        {
            SetPartialBit = true;
        }

        /// <summary>
        /// Computes the value for the timeslice.
        /// </summary>
        protected override DataValue ComputeValue(TimeSlice slice)
        {
            uint? id = AggregateId.Identifier as uint?;

            if (id == null)
            {
                return base.ComputeValue(slice);
            }
            switch (id.Value)
            {
                case Objects.AggregateFunction_Count:
                    return ComputeCount(slice);
                case Objects.AggregateFunction_AnnotationCount:
                    return ComputeAnnotationCount(slice);
                case Objects.AggregateFunction_DurationInStateZero:
                    return ComputeDurationInState(slice, false);
                case Objects.AggregateFunction_DurationInStateNonZero:
                    return ComputeDurationInState(slice, true);
                case Objects.AggregateFunction_NumberOfTransitions:
                    return ComputeNumberOfTransitions(slice);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            if (values == null || values.Count == 0)
            {
                var status = CurrentSlice.OutOfDataRange ? StatusCodes.BadNoData : StatusCodes.Good;
                var noData = new DataValue
                {
                    StatusCode = status,
                    SourceTimestamp = GetTimestamp(slice),
                    ServerTimestamp = GetTimestamp(slice),
                    WrappedValue = new Variant(0, TypeInfo.Scalars.Int32)
                };

                if (!StatusCode.IsBad(noData.StatusCode))
                {
                    noData.StatusCode = noData.StatusCode.SetAggregateBits(noData.StatusCode.AggregateBits | AggregateBits.Calculated);
                }
                return noData;
            }

            // count the values.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                // Mantis 11495 answer (12.10.2026): the Count definition (5.4.3.21) overrides the
                // general rule of 4.2.1.2. Only Good values are counted, whatever TreatUncertainAsBad
                // is; non-Good values only affect the StatusCode (value-based, 5.4.3), where an
                // Uncertain value is not Good. A count of 0 means that there is no Good data.
                if (StatusCode.IsGood(values[ii].StatusCode))
                {
                    count++;
                }
            }

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(count, TypeInfo.Scalars.Int32),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode, uncertainIsGood: false);

            if (!StatusCode.IsBad(value.StatusCode))
            {
                // set aggregate bits fon non Bad values
                value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Calculated);
            }
            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the AnnotationCount aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeAnnotationCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // count the values.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                count++;
            }

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(count, TypeInfo.Scalars.Int32),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the DurationInStateZero and DurationInStateNonZero aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeDurationInState(TimeSlice slice, bool isNonZero)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                // handle empty slices by returning no data or marking the whole slice as BadNoData for status calculaton.
                // if the slice overlaps the start of the data.
                if (!ContainsDataStart(slice))
                {
                    return GetNoDataValue(slice);
                }

                values = new List<DataValue>
                {
                    CreateNoDataValue(slice.StartTime),
                    CreateNoDataValue(slice.EndTime)
                };
            }

            // Boolean + Stepped=False: CTT does not interpolate Boolean
            // Stepped=False -> replace invalid Boolean bounds with BadNoData and track whether the end bound is missing.
            bool isEndBoundMissing = false;

            if (values.Count > 1 && !Stepped && RawDataType?.BuiltInType == BuiltInType.Boolean)
            {
                if (IsBooleanBoundMissing(values[0], slice, slice.StartTime))
                {
                    values[0] = CreateNoDataValue(slice.StartTime);
                }

                if (IsBooleanBoundMissing(values[values.Count - 1], slice, slice.EndTime))
                {
                    values[values.Count - 1] = CreateNoDataValue(slice.EndTime);
                    isEndBoundMissing = true;
                }
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, true);

            double duration = 0;

            // Without an end bound, GetRegionsInValueSet truncates the last Good region to 1 tick.
            // The remaining time up to EndTime is UncertainDataSubNormal (included in status calculation, but not in duration).
            SubRegion goodRegionForStatusAdjustment = null;

            if (isEndBoundMissing && regions != null && regions.Count > 0)
            {
                SubRegion lastRegion = regions[regions.Count - 1];

                // the duration from the start of the region up to EndTime
                double remainingDurationMs = (slice.EndTime - lastRegion.StartTime).TotalMilliseconds;

                if (lastRegion.Duration == 1 && StatusCode.IsGood(lastRegion.StatusCode))
                {
                    goodRegionForStatusAdjustment = lastRegion;   // Good for duration, Uncertain for status
                }

                if (lastRegion.Duration == 1 && remainingDurationMs > 1)
                {
                    regions.Add(new SubRegion
                    {
                        StartTime = lastRegion.StartTime.AddMilliseconds(1),
                        Duration = remainingDurationMs - 1,
                        StartValue = lastRegion.StartValue,
                        EndValue = lastRegion.StartValue,
                        StatusCode = StatusCodes.UncertainDataSubNormal
                    });
                }
            }


            // the tick is still Good -> it counts in duration
            for (int ii = 0; ii < regions.Count; ii++)
            {
                if (StatusCode.IsNotGood(regions[ii].StatusCode))
                {
                    continue;
                }

                if (isNonZero ? regions[ii].StartValue != 0 : regions[ii].StartValue == 0)
                {
                    duration += regions[ii].Duration;
                }
            }

            // 5.4.3.2.2: SlopedInterpolation (Stepped=False):
            // - Good region ending at a Bad/Uncertain value is treated as Uncertain (for status calculation only).
            // - Its duration has already been calculated, doesn't change.
            if (!Stepped)
            {
                foreach (SubRegion region in regions)
                {
                    if (!StatusCode.IsGood(region.StatusCode))
                    {
                        continue;
                    }

                    DateTime regionEnd = region.StartTime.AddMilliseconds(region.Duration);

                    DataValue endPoint = values.Find(v => Math.Abs((v.SourceTimestamp - regionEnd).TotalMilliseconds) < 0.5);

                    if (endPoint != null && StatusCode.IsNotGood(endPoint.StatusCode))
                    {
                        region.StatusCode = StatusCodes.UncertainDataSubNormal;
                    }
                }
            }

            // set final Good tick as Uncertain for status calculation
            if (goodRegionForStatusAdjustment != null)
            {
                goodRegionForStatusAdjustment.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            // set TreatUncertainAsBad AFTER final tick mark
            // all Uncertain regions (including that tick) --> counted as Bad (for status calculation)
            if (Configuration.TreatUncertainAsBad)
            {
                foreach (SubRegion region in regions)
                {
                    if (StatusCode.IsUncertain(region.StatusCode))
                    {
                        region.StatusCode = StatusCodes.BadNoData;
                    }
                }
            }

            // set the timestamp and status.
            var value = new DataValue
            {
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };

            StatusCode status = GetTimeBasedStatusCode(regions, value.StatusCode);

            // when PercentDataGood + PercentDataBad == 100 --> use UncertainDataSubNormal (not Bad) if the interval falls below PercentDataGood.
            // leave BadNoData unchanged
            if (status.CodeBits == StatusCodes.Bad && Configuration.PercentDataGood + Configuration.PercentDataBad == 100)
            {
                status = value.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
            }

            // Bad interval --> Null value and status Bad + Calculated
            if (StatusCode.IsBad(status))
            {
                value.WrappedValue = Variant.Null;
                value.StatusCode = StatusCodes.Bad;
            }
            else
            {
                value.WrappedValue = new Variant(duration, TypeInfo.Scalars.Double);
                value.StatusCode = status;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Determines whether an interpolated Boolean bound should be treated as missing when Stepped=False:
        /// - non-interpolated bounds -> kept
        /// - Good interpolated bounds -> missing
        /// - other interpolated bounds -> missing only if the next raw point exists and is not Bad.
        /// </summary>
        private static bool IsBooleanBoundMissing(DataValue bound, TimeSlice slice, DateTime timestamp)
        {
            if ((bound.StatusCode.AggregateBits & AggregateBits.Interpolated) == 0)
            {
                return false;
            }

            if (StatusCode.IsGood(bound.StatusCode))
            {
                return true;
            }

            DataValue after = FindFirstRawAfter(slice, timestamp);
            return after != null && !StatusCode.IsBad(after.StatusCode);
        }

        /// <summary>
        /// Returns the FIRST raw data point strictly after the specified timestamp or NULL if no such point is found.
        /// </summary>
        private static DataValue FindFirstRawAfter(TimeSlice slice, DateTime timestamp)
        {
            LinkedListNode<DataValue> node = slice.EarlyBound ?? slice.Begin;

            while (node != null && node.Value.SourceTimestamp <= timestamp)
            {
                node = node.Next;
            }

            return node?.Value;
        }

        /// <summary>
        /// Creates a BadNoData value at the specified timestamp with an exact BadNoData status.
        /// </summary>
        private static DataValue CreateNoDataValue(DateTime timestamp)
        {
            return new DataValue
            {
                StatusCode = StatusCodes.BadNoData,   // must be exactly BadNoData to match the check in GetRegionsInValueSet
                SourceTimestamp = timestamp,
                ServerTimestamp = timestamp
            };
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeNumberOfTransitions(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // The first non-bad value is a transition when no previous non-bad value exists.
            LinkedListNode<DataValue> previousValue = slice.NonBadEarlyBound;
            bool hasLastValue = previousValue != null;
            object lastValue = previousValue?.Value.Value;

            // count the transitions.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    continue;
                }

                object nextValue = values[ii].Value;

                if (!hasLastValue || !Utils.IsEqual(lastValue, nextValue))
                {
                    count++;
                }

                hasLastValue = true;
                lastValue = nextValue;
            }

            // set the timestamp and status.
            var value = new DataValue
            {
                WrappedValue = new Variant(count, TypeInfo.Scalars.Int32),
                SourceTimestamp = GetTimestamp(slice),
                ServerTimestamp = GetTimestamp(slice)
            };
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode);

            // return result.
            return value;
        }
    }
}
