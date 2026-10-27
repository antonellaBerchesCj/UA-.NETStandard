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
    public class MinMaxAggregateCalculator : AggregateCalculator
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
        public MinMaxAggregateCalculator(
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
                case Objects.AggregateFunction_Minimum:
                    return ComputeMinMax(slice, 1, false);
                case Objects.AggregateFunction_MinimumActualTime:
                    return ComputeMinMax(slice, 1, true);
                case Objects.AggregateFunction_Maximum:
                    return ComputeMinMax(slice, 2, false);
                case Objects.AggregateFunction_MaximumActualTime:
                    return ComputeMinMax(slice, 2, true);
                case Objects.AggregateFunction_Range:
                    return ComputeMinMax(slice, 3, false);
                case Objects.AggregateFunction_Minimum2:
                    return ComputeMinMax2(slice, 1, false);
                case Objects.AggregateFunction_MinimumActualTime2:
                    return ComputeMinMax2(slice, 1, true);
                case Objects.AggregateFunction_Maximum2:
                    return ComputeMinMax2(slice, 2, false);
                case Objects.AggregateFunction_MaximumActualTime2:
                    return ComputeMinMax2(slice, 2, true);
                case Objects.AggregateFunction_Range2:
                    return ComputeMinMax2(slice, 3, false);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculate the Minimum, Maximum, MinimumActualTime and MaximumActualTime aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeMinMax(TimeSlice slice, int valueType, bool returnActualTime)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double minimumGoodValue = double.MaxValue;
            double minimumUncertainValue = double.MaxValue;
            double maximumGoodValue = double.MinValue;
            double maximumUncertainValue = double.MinValue;

            DateTime minimumGoodTimestamp = DateTime.MinValue;
            DateTime maximumGoodTimestamp = DateTime.MinValue;

            TypeInfo minimumOriginalType = null;
            TypeInfo maximumOriginalType = null;

            bool badValuesExist = false;
            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool goodValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values (as determined by the TreatUncertainAsBad parameter).
                if (!IsGood(values[ii]))
                {
                    badValuesExist = true;
                    continue;
                }

                double currentValue;
                // convert to double.
                try
                {
                    currentValue = CastToDouble(values[ii]);
                }
                catch (Exception)
                {
                    badValuesExist = true;
                    continue;
                }

                // Part 13 5.4.3.10-14: only Good values are candidates. An Uncertain value beyond
                // the Good result only makes the status Uncertain_DataSubNormal (Mantis 11427).
                if (StatusCode.IsUncertain(currentStatus))
                {
                    if (minimumUncertainValue > currentValue)
                    {
                        minimumUncertainValue = currentValue;
                    }

                    if (maximumUncertainValue < currentValue)
                    {
                        maximumUncertainValue = currentValue;
                    }

                    continue;
                }

                // check for new minimum.
                if (minimumGoodValue > currentValue)
                {
                    minimumGoodValue = currentValue;
                    minimumGoodTimestamp = currentTime;
                    minimumOriginalType = values[ii].WrappedValue.TypeInfo;
                    duplicatesMinimumsExist = false;
                    goodValueExists = true;
                }
                // check for duplicate minimums.
                else if (minimumGoodValue == currentValue)
                {
                    duplicatesMinimumsExist = true;
                }

                // check for new maximum.
                if (maximumGoodValue < currentValue)
                {
                    maximumGoodValue = currentValue;
                    maximumGoodTimestamp = currentTime;
                    maximumOriginalType = values[ii].WrappedValue.TypeInfo;
                    duplicatesMaximumsExist = false;
                    goodValueExists = true;
                }
                // check for duplicate maximums.
                else if (maximumGoodValue == currentValue)
                {
                    duplicatesMaximumsExist = true;
                }
            }

            // check if at least one good value exists.
            if (!goodValueExists)
            {
                return GetNoDataValue(slice);
            }

            // uncertain if any bad values exist or an uncertain value lies beyond the result.
            bool uncertainBeyondResult =
                ((valueType == 1 || valueType == 3) && minimumUncertainValue < minimumGoodValue) ||
                ((valueType == 2 || valueType == 3) && maximumUncertainValue > maximumGoodValue);

            // set the status code.
            StatusCode statusCode = StatusCodes.Good;

            if (badValuesExist || uncertainBeyondResult)
            {
                statusCode = StatusCodes.UncertainDataSubNormal;
            }

            // determine the calculated value to return.
            object processedValue = null;
            TypeInfo processedType = default;
            DateTime processedTimestamp = DateTime.MinValue;
            bool duplicatesExist = false;

            if (valueType == 1)
            {
                processedValue = minimumGoodValue;
                processedTimestamp = minimumGoodTimestamp;
                processedType = minimumOriginalType;
                duplicatesExist = duplicatesMinimumsExist;
            }
            else if (valueType == 2)
            {
                processedValue = maximumGoodValue;
                processedTimestamp = maximumGoodTimestamp;
                processedType = maximumOriginalType;
                duplicatesExist = duplicatesMaximumsExist;
            }
            else if (valueType == 3)
            {
                // Range returns a Double as in 1.5.378. Table 50 gives the raw data type, but changing
                // the result type would break clients (no breaking changes on this branch).
                processedValue = Math.Abs(maximumGoodValue - minimumGoodValue);
                processedType = TypeInfo.Scalars.Double;
            }

            // Tables 58, 59 and 62: Calculated unless the value is on the start time of the interval,
            // and whenever non-Good values made the status Uncertain_DataSubNormal. Tables 60, 61:
            // the ActualTime variants are Calculated only in the second case, Raw otherwise.
            if (StatusCode.IsUncertain(statusCode) ||
                (!returnActualTime && processedTimestamp != GetTimestamp(slice)))
            {
                statusCode = statusCode.SetAggregateBits(AggregateBits.Calculated);
            }

            // set the multiple values flags.
            if (duplicatesExist)
            {
                statusCode = statusCode.SetAggregateBits(
                    statusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            // convert back to original datatype.
            if (processedType != null && processedType.BuiltInType != BuiltInType.Double)
            {
                processedValue = TypeInfo.Cast(
                    processedValue,
                    TypeInfo.Scalars.Double,
                    processedType.BuiltInType);
            }
            else
            {
                processedType = TypeInfo.Scalars.Double;
            }

            // create processed value.
            var value = new DataValue
            {
                WrappedValue = new Variant(processedValue, processedType),
                StatusCode = statusCode
            };

            if (returnActualTime)
            {
                value.SourceTimestamp = processedTimestamp;
                value.ServerTimestamp = processedTimestamp;
            }
            else
            {
                value.SourceTimestamp = GetTimestamp(slice);
                value.ServerTimestamp = GetTimestamp(slice);
            }

            return value;
        }

        /// <summary>
        /// Calculate the Minimum2, Maximum2, MinimumActualTime2, MaximumActualTime2 and Range2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeMinMax2(TimeSlice slice, int valueType, bool returnActualTime)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double minimumGoodValue = double.MaxValue;
            double maximumGoodValue = double.MinValue;

            DateTime minimumGoodTimestamp = DateTime.MinValue;
            DateTime maximumGoodTimestamp = DateTime.MinValue;

            StatusCode minimumGoodStatusCode = StatusCodes.Good;
            StatusCode maximumGoodStatusCode = StatusCodes.Good;

            TypeInfo minimumOriginalType = default;
            TypeInfo maximumOriginalType = default;
            // the source data type: a real raw sample of the slice (bounds may be interpolated -->
            // Double), else any raw value of the variable (an interval holding only bounds).
            TypeInfo rawOriginalType = GetSourceDataType(values);

            // Minimum2/Maximum2/Range2 return the source data type (Table 50): an interpolated
            // bound is converted to that type before the extremes are selected.

            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool goodValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values (as determined by the TreatUncertainAsBad parameter).
                if (!IsGood(values[ii]))
                {
                    continue;
                }

                double currentValue;
                // convert to double.
                try
                {
                    currentValue = CastToDouble(values[ii]);
                }
                catch (Exception)
                {
                    continue;
                }

                currentValue = RoundToSourceType(currentValue, rawOriginalType);

                // skip endpoint if stepped.
                if (currentTime == slice.EndTime && Stepped)
                {
                    break;
                }

                // check for new minimum.
                if (minimumGoodValue > currentValue)
                {
                    minimumGoodValue = currentValue;
                    minimumGoodTimestamp = currentTime;
                    minimumGoodStatusCode = currentStatus;
                    minimumOriginalType = values[ii].WrappedValue.TypeInfo;
                    duplicatesMinimumsExist = false;
                    goodValueExists = true;
                }
                // check for duplicate minimums.
                else if (minimumGoodValue == currentValue)
                {
                    duplicatesMinimumsExist = true;
                }

                // check for new maximum.
                if (maximumGoodValue < currentValue)
                {
                    maximumGoodValue = currentValue;
                    maximumGoodTimestamp = currentTime;
                    maximumGoodStatusCode = currentStatus;
                    maximumOriginalType = values[ii].WrappedValue.TypeInfo;
                    duplicatesMaximumsExist = false;
                    goodValueExists = true;
                }
                // check for duplicate maximums.
                else if (maximumGoodValue == currentValue)
                {
                    duplicatesMaximumsExist = true;
                }
            }

            // check if at least one good value exists.
            if (!goodValueExists)
            {
                DataValue noDataValue = GetNoDataValue(slice);
                // check if interval is partial and set the flag accordingly
                if (slice.Partial)
                {
                    noDataValue.StatusCode = noDataValue.StatusCode
                        .SetAggregateBits(AggregateBits.Partial);
                }
                return noDataValue;
            }

            // determine the calculated value to return.
            object processedValue = null;
            TypeInfo processedType = default;
            DateTime processedTimestamp = DateTime.MinValue;
            StatusCode processedStatusCode = StatusCodes.Good;
            bool duplicatesExist = false;

            if (valueType == 1)
            {
                processedValue = minimumGoodValue;
                processedTimestamp = minimumGoodTimestamp;
                processedStatusCode = minimumGoodStatusCode;
                processedType = minimumOriginalType;
                duplicatesExist = duplicatesMinimumsExist;
            }
            else if (valueType == 2)
            {
                processedValue = maximumGoodValue;
                processedTimestamp = maximumGoodTimestamp;
                processedStatusCode = maximumGoodStatusCode;
                processedType = maximumOriginalType;
                duplicatesExist = duplicatesMaximumsExist;
            }
            else if (valueType == 3)
            {
                // Range2 returns a Double as in 1.5.378 (see Range above).
                processedValue = Math.Abs(maximumGoodValue - minimumGoodValue);
                processedType = TypeInfo.Scalars.Double;
            }

            // set the status code.
            StatusCode statusCode = processedStatusCode;

            // set calculated if not returning actual time and value is not at the start time.
            // §5.4.2.2: for a backward read the start of the interval is its late time (included).
            if (!returnActualTime &&
                processedTimestamp != GetTimestamp(slice) &&
                (statusCode.AggregateBits & AggregateBits.Interpolated) == 0)
            {
                statusCode = statusCode.SetAggregateBits(
                    statusCode.AggregateBits | AggregateBits.Calculated);
            }

            // set the multiple values flags.
            if (duplicatesExist)
            {
                statusCode = statusCode.SetAggregateBits(
                    statusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            // restore the original source type when Min2/Max2 selects an interpolated bound of type Double.
            if (valueType != 3 && (processedType == null || processedType.BuiltInType == BuiltInType.Double) &&
                rawOriginalType != null)
            {
                processedType = rawOriginalType;
            }

            // convert back to original datatype.
            if (processedType != null && processedType.BuiltInType != BuiltInType.Double)
            {
                processedValue = TypeInfo.Cast(
                    processedValue,
                    TypeInfo.Scalars.Double,
                    processedType.BuiltInType);
            }
            else
            {
                processedType = TypeInfo.Scalars.Double;
            }

            // create processed value.
            var value = new DataValue
            {
                WrappedValue = new Variant(processedValue, processedType),
                StatusCode = GetTimeBasedStatusCode(slice, values, statusCode)
            };

            // zero value if status is bad.
            if (StatusCode.IsBad(value.StatusCode))
            {
                value.WrappedValue = Variant.Null;
            }

            if (returnActualTime)
            {
                // calculate effective time if end bound is used.
                if (TimeFlowsBackward)
                {
                    if (processedTimestamp == slice.StartTime)
                    {
                        processedTimestamp = processedTimestamp.AddMilliseconds(+1);
                        value.StatusCode = value.StatusCode.SetAggregateBits(
                            value.StatusCode.AggregateBits | AggregateBits.Interpolated);
                    }
                }
                else if (processedTimestamp == slice.EndTime)
                {
                    processedTimestamp = processedTimestamp.AddMilliseconds(-1);
                    value.StatusCode = value.StatusCode.SetAggregateBits(
                        value.StatusCode.AggregateBits | AggregateBits.Interpolated);
                }

                value.SourceTimestamp = processedTimestamp;
                value.ServerTimestamp = processedTimestamp;
            }
            else
            {
                value.SourceTimestamp = GetTimestamp(slice);
                value.ServerTimestamp = GetTimestamp(slice);
            }

            return value;
        }

        /// <summary>
        /// Returns the numeric scalar data type of the raw values: from a raw (not interpolated)
        /// sample of the slice, else from any raw value of the variable; null if unknown.
        /// </summary>
        private TypeInfo GetSourceDataType(List<DataValue> values)
        {
            foreach (DataValue value in values)
            {
                if ((value.StatusCode.AggregateBits & AggregateBits.Interpolated) == 0 && value.Value != null)
                {
                    TypeInfo typeInfo = TypeInfo.Construct(value.Value);
                    if (typeInfo != null && typeInfo.ValueRank < 0 && TypeInfo.IsNumericType(typeInfo.BuiltInType))
                    {
                        return typeInfo;
                    }
                }
            }

            TypeInfo rawType = RawDataType;

            if (rawType != null && rawType.ValueRank < 0 && TypeInfo.IsNumericType(rawType.BuiltInType))
            {
                return rawType;
            }

            return null;
        }
    }
}
