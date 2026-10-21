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

            // Mantis 11426 (OPC Classic semantics of Minimum, Maximum, MinimumActualTime,
            // MaximumActualTime and Range): Uncertain values are looked at as if they are Good and are
            // candidates whatever TreatUncertainAsBad is; the status is Good unless Bad values exist in
            // the interval or the selected extremum is Uncertain (then Uncertain_DataSubNormal), and
            // Bad_NoData if all values are Bad.
            double minimumValue = double.MaxValue;
            double maximumValue = double.MinValue;

            DateTime minimumTimestamp = DateTime.MinValue;
            DateTime maximumTimestamp = DateTime.MinValue;

            TypeInfo minimumOriginalType = null;
            TypeInfo maximumOriginalType = null;

            bool minimumIsUncertain = false;
            bool maximumIsUncertain = false;
            bool badValuesExist = false;
            bool badValueWithDataExists = false;
            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool nonBadValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values (TreatUncertainAsBad does not apply).
                if (!IsBoundCandidate(values[ii]))
                {
                    badValuesExist = true;

                    // a Bad sample that still carries a value was rejected from the selection.
                    if (StatusCode.IsBad(currentStatus) && values[ii].Value != null)
                    {
                        badValueWithDataExists = true;
                    }

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

                bool currentIsUncertain = StatusCode.IsUncertain(currentStatus);

                // check for new minimum.
                if (!nonBadValueExists || minimumValue > currentValue)
                {
                    minimumValue = currentValue;
                    minimumTimestamp = currentTime;
                    minimumOriginalType = values[ii].WrappedValue.TypeInfo;
                    minimumIsUncertain = currentIsUncertain;
                    duplicatesMinimumsExist = false;
                }
                // check for duplicate minimums.
                else if (minimumValue == currentValue)
                {
                    duplicatesMinimumsExist = true;
                }

                // check for new maximum.
                if (!nonBadValueExists || maximumValue < currentValue)
                {
                    maximumValue = currentValue;
                    maximumTimestamp = currentTime;
                    maximumOriginalType = values[ii].WrappedValue.TypeInfo;
                    maximumIsUncertain = currentIsUncertain;
                    duplicatesMaximumsExist = false;
                }
                // check for duplicate maximums.
                else if (maximumValue == currentValue)
                {
                    duplicatesMaximumsExist = true;
                }

                nonBadValueExists = true;
            }

            // check if at least one non-bad value exists.
            if (!nonBadValueExists)
            {
                return GetNoDataValue(slice);
            }

            // set the status code.
            StatusCode statusCode = StatusCodes.Good;

            // uncertain if any bad values exist or the selected extremum is uncertain.
            bool uncertainExtremum =
                ((valueType == 1 || valueType == 3) && minimumIsUncertain) ||
                ((valueType == 2 || valueType == 3) && maximumIsUncertain);

            if (badValuesExist || uncertainExtremum)
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
                processedValue = minimumValue;
                processedTimestamp = minimumTimestamp;
                processedType = minimumOriginalType;
                duplicatesExist = duplicatesMinimumsExist;
            }
            else if (valueType == 2)
            {
                processedValue = maximumValue;
                processedTimestamp = maximumTimestamp;
                processedType = maximumOriginalType;
                duplicatesExist = duplicatesMaximumsExist;
            }
            else if (valueType == 3)
            {
                double range = Math.Abs(maximumValue - minimumValue);
                processedValue = range;
                processedType = GetRangeType(minimumOriginalType, range);
            }

            // Minimum/Maximum/Range are Calculated unless the extremum is at the request-direction
            // interval start (the later bound for reverse reads) and Good.
            // MinimumActualTime and MaximumActualTime return a raw value with its own timestamp, so
            // they keep Raw when the interval is Uncertain_DataSubNormal only because of Bad samples
            // without a value (Annex A Tables MinimumActualTime/MaximumActualTime, Historian2/3/5 at
            // 12:00:39; CTT 1.05.513 on Historian3). They are Calculated when the status is Uncertain
            // because of non-Good values that carry a value: a rejected Bad sample with a value or an
            // Uncertain extremum (Tables 60/61, Mantis 11427; CTT 1.05.513 Aggregate tests on
            // Historian1, e.g. 005_06 MinimumActualTime 44.46 Uncertain at 12:08:00).
            if (returnActualTime
                ? StatusCode.IsUncertain(statusCode) && (badValueWithDataExists || uncertainExtremum)
                : StatusCode.IsUncertain(statusCode) || processedTimestamp != GetTimestamp(slice))
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

            // Minimum2/Maximum2/Range2 return the source data type (Tables 63-67): an interpolated
            // bound of an integer variable is converted to that type (OPC 10000-4 §7.7.3, half up)
            // before the extremes are selected, so a bound equal to a raw value is a duplicate
            // (CTT 1.05.513 on HistorianInt1, Mantis 11494).
            bool roundToInteger = rawOriginalType != null && IsIntegerType(rawOriginalType.BuiltInType);

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

                if (roundToInteger)
                {
                    currentValue = RoundHalfUp(currentValue);
                }

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
                double range = Math.Abs(maximumGoodValue - minimumGoodValue);
                processedValue = range;
                processedType = GetRangeType(rawOriginalType ?? minimumOriginalType, range);
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
                // an interpolated bound is a real value: round it as OPC 10000-4 §7.7.3 says.
                double real = (double)processedValue;
                if (IsIntegerType(processedType.BuiltInType))
                {
                    real = RoundHalfUp(real);
                }

                processedValue = TypeInfo.Cast(
                    real,
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

        /// <summary>
        /// Returns the data type of a Range/Range2 result: the source type (Part 13 Tables 62
        /// and 67), or Double when the source is not a numeric type or the range does not fit.
        /// </summary>
        private static TypeInfo GetRangeType(TypeInfo sourceType, double range)
        {
            if (sourceType == null ||
                sourceType.ValueRank >= 0 ||
                !TypeInfo.IsNumericType(sourceType.BuiltInType) ||
                !FitsInType(range, sourceType.BuiltInType))
            {
                return TypeInfo.Scalars.Double;
            }

            return sourceType;
        }
    }
}
