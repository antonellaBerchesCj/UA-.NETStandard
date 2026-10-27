/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// Bridges the legacy history source to the existing aggregate calculators
    /// without changing the HistoryFile storage contract.
    /// </summary>
    internal static class ProcessedHistoryAdapter
    {
        internal const int PageSize = 1000;
        internal const int MaxBufferedOutputs = 100000;

        public static ServiceResult Read(
            ServerSystemContext context,
            IServerInternal server,
            NodeId variableId,
            IHistoryDataSource source,
            ReadProcessedDetails details,
            NodeId aggregateId,
            IReadOnlyList<DateTime> annotationTimes,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result)
        {
            if (details.StartTime == details.EndTime)
            {
                result.StatusCode = StatusCodes.BadInvalidArgument;
                return StatusCodes.BadInvalidArgument;
            }

            ProcessedHistoryContinuationState state = null;
            if (nodeToRead.ContinuationPoint != null &&
                nodeToRead.ContinuationPoint.Length > 0)
            {
                state = Restore(context, nodeToRead.ContinuationPoint);
                if (state == null ||
                    state.VariableId != variableId ||
                    state.AggregateId != aggregateId)
                {
                    state?.Dispose();
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = null;
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            if (state == null)
            {
                ServiceResult error = Compute(
                    server,
                    variableId,
                    source,
                    details,
                    aggregateId,
                    annotationTimes,
                    out List<DataValue> outputs);
                if (ServiceResult.IsBad(error))
                {
                    result.StatusCode = error.StatusCode;
                    return error;
                }

                state = new ProcessedHistoryContinuationState(
                    variableId,
                    aggregateId,
                    outputs);
            }

            return EmitPage(context, state, timestampsToReturn, nodeToRead, result);
        }

        public static ProcessedHistoryContinuationState Restore(
            ServerSystemContext context,
            byte[] continuationPoint)
        {
            if (context?.OperationContext?.Session == null)
            {
                return null;
            }

            return context.OperationContext.Session.RestoreHistoryContinuationPoint(
                continuationPoint) as ProcessedHistoryContinuationState;
        }

        private static ServiceResult Compute(
            IServerInternal server,
            NodeId variableId,
            IHistoryDataSource source,
            ReadProcessedDetails details,
            NodeId aggregateId,
            IReadOnlyList<DateTime> annotationTimes,
            out List<DataValue> outputs)
        {
            outputs = [];

            if (source == null)
            {
                return StatusCodes.BadNotReadable;
            }

            AggregateConfiguration configuration = ResolveConfiguration(
                server.AggregateManager,
                variableId,
                details.AggregateConfiguration);

            if (configuration.PercentDataGood > 100 ||
                configuration.PercentDataBad > 100 ||
                configuration.PercentDataGood < 100 - configuration.PercentDataBad)
            {
                return StatusCodes.BadAggregateInvalidInputs;
            }

            double processingInterval = details.ProcessingInterval;
            if (processingInterval > 0 &&
                Math.Abs((details.EndTime - details.StartTime).TotalMilliseconds) /
                processingInterval >
                MaxBufferedOutputs)
            {
                return StatusCodes.BadTooManyOperations;
            }

            IAggregateCalculator calculator = server.AggregateManager.CreateCalculator(
                aggregateId,
                details.StartTime,
                details.EndTime,
                processingInterval,
                false,
                configuration);
            if (calculator == null)
            {
                return StatusCodes.BadAggregateNotSupported;
            }

            // AnnotationCount counts the Annotations of the variable (Part 13 §5.4.3.20); the raw
            // values decide where the data starts and ends (Table 68).
            if (annotationTimes != null && calculator is AggregateCalculator aggregateCalculator)
            {
                foreach (DateTime timestamp in annotationTimes)
                {
                    aggregateCalculator.QueueAnnotation(timestamp);
                }
            }

            bool isForward = details.StartTime < details.EndTime;
            DataValue value = source.FirstRaw(
                details.StartTime,
                !isForward,
                false,
                out int position);

            // If the leading bound does not exist, begin with the first value
            // in the requested direction.
            if (value == null)
            {
                value = source.FirstRaw(
                    details.StartTime,
                    isForward,
                    false,
                    out position);
            }

            while (value != null)
            {
                if (!calculator.QueueRawValue(value))
                {
                    Flush(calculator, outputs, false);
                }

                if (outputs.Count > MaxBufferedOutputs)
                {
                    return StatusCodes.BadTooManyOperations;
                }

                bool passedEnd = isForward
                    ? value.ServerTimestamp > details.EndTime
                    : value.ServerTimestamp < details.EndTime;
                if (passedEnd)
                {
                    break;
                }

                value = source.NextRaw(
                    value.ServerTimestamp,
                    isForward,
                    false,
                    ref position);
            }

            Flush(calculator, outputs, true);
            return outputs.Count > MaxBufferedOutputs
                ? StatusCodes.BadTooManyOperations
                : ServiceResult.Good;
        }

        private static AggregateConfiguration ResolveConfiguration(
            AggregateManager manager,
            NodeId variableId,
            AggregateConfiguration requested)
        {
            bool implicitDefault = requested != null &&
                !requested.UseServerCapabilitiesDefaults &&
                requested.PercentDataBad == 0 &&
                requested.PercentDataGood == 0 &&
                !requested.TreatUncertainAsBad &&
                !requested.UseSlopedExtrapolation;

            if (requested == null ||
                requested.UseServerCapabilitiesDefaults ||
                implicitDefault)
            {
                return manager.GetDefaultConfiguration(variableId);
            }

            return requested;
        }

        private static void Flush(
            IAggregateCalculator calculator,
            List<DataValue> outputs,
            bool returnPartial)
        {
            DataValue value;
            while ((value = calculator.GetProcessedValue(returnPartial)) != null)
            {
                outputs.Add(value);
            }
        }

        private static ServiceResult EmitPage(
            ServerSystemContext context,
            ProcessedHistoryContinuationState state,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result)
        {
            int count = Math.Min(PageSize, state.Outputs.Count - state.Offset);
            var data = new HistoryData();

            for (int ii = 0; ii < count; ii++)
            {
                DataValue value = (DataValue)state.Outputs[state.Offset + ii].Clone();
                ApplyResultOptions(value, timestampsToReturn, nodeToRead);
                data.DataValues.Add(value);
            }

            state.Offset += count;
            result.HistoryData = new ExtensionObject(data);
            result.StatusCode = StatusCodes.Good;

            if (state.Offset >= state.Outputs.Count)
            {
                result.ContinuationPoint = null;
                state.Dispose();
                return ServiceResult.Good;
            }

            state.Id = Guid.NewGuid();

            // OPC 10000-4 7.9: no more continuation points for this request.
            if (!HistoryContinuationPoints.TrySave(context, state.Id, state))
            {
                state.Dispose();
                result.ContinuationPoint = null;
                result.HistoryData = null;
                result.StatusCode = StatusCodes.BadNoContinuationPoints;
                return StatusCodes.BadNoContinuationPoints;
            }

            result.ContinuationPoint = state.Id.ToByteArray();
            return ServiceResult.Good;
        }

        private static void ApplyResultOptions(
            DataValue value,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead)
        {
            if (StatusCode.IsGood(value.StatusCode))
            {
                object valueToReturn = value.Value;
                if (nodeToRead.ParsedIndexRange != NumericRange.Empty)
                {
                    StatusCode error = nodeToRead.ParsedIndexRange.ApplyRange(ref valueToReturn);
                    if (StatusCode.IsBad(error))
                    {
                        value.Value = null;
                        value.StatusCode = error;
                    }
                    else
                    {
                        value.Value = valueToReturn;
                    }
                }

                if (!QualifiedName.IsNull(nodeToRead.DataEncoding))
                {
                    value.Value = null;
                    value.StatusCode = StatusCodes.BadDataEncodingUnsupported;
                }
            }

            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Server)
            {
                value.SourceTimestamp = DateTime.MinValue;
            }

            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Source)
            {
                value.ServerTimestamp = DateTime.MinValue;
            }
        }
    }

    internal sealed class ProcessedHistoryContinuationState : IDisposable
    {
        public ProcessedHistoryContinuationState(
            NodeId variableId,
            NodeId aggregateId,
            List<DataValue> outputs)
        {
            Id = Guid.NewGuid();
            VariableId = variableId;
            AggregateId = aggregateId;
            Outputs = outputs;
        }

        public Guid Id { get; set; }

        public NodeId VariableId { get; }

        public NodeId AggregateId { get; }

        public List<DataValue> Outputs { get; }

        public int Offset { get; set; }

        public void Dispose()
        {
            // The state owns only managed, immutable-for-the-read data.
        }
    }
}
