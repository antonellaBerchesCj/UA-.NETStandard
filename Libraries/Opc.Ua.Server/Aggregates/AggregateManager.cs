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
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages aggregate factories supported by the server.
    /// </summary>
    public class AggregateManager : IDisposable
    {
        /// <summary>
        /// Initilizes the manager.
        /// </summary>
        public AggregateManager(IServerInternal server)
        {
            m_server = server;
            m_factories = new NodeIdDictionary<AggregatorFactory>();
            m_minimumProcessingInterval = 1000;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }

        /// <summary>
        /// Checks if the aggregate is supported by the server.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        /// <returns>True if the aggregate is supported.</returns>
        public bool IsSupported(NodeId aggregateId)
        {
            if (NodeId.IsNull(aggregateId))
            {
                return false;
            }

            lock (m_lock)
            {
                return m_factories.ContainsKey(aggregateId);
            }
        }

        /// <summary>
        /// The minimum processing interval for any aggregate calculation.
        /// </summary>
        public double MinimumProcessingInterval
        {
            get
            {
                lock (m_lock)
                {
                    return m_minimumProcessingInterval;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_minimumProcessingInterval = value;
                }
            }
        }

        /// <summary>
        /// Returns the default configuration for the specified variable id.
        /// </summary>
        /// <param name="variableId">The id of history data node.</param>
        /// <returns>The configuration.</returns>
        public AggregateConfiguration GetDefaultConfiguration(NodeId variableId)
        {
            lock (m_lock)
            {
                if (m_defaultConfiguration == null)
                {
                    m_defaultConfiguration = new AggregateConfiguration
                    {
                        PercentDataBad = 100,
                        PercentDataGood = 100,
                        // OPC UA Part 13 specifies that uncertain values are treated as bad by default.
                        TreatUncertainAsBad = true,
                        UseSlopedExtrapolation = false,
                        UseServerCapabilitiesDefaults = false
                    };
                }

                return m_defaultConfiguration;
            }
        }

        /// <summary>
        /// Sets the default aggregate configuration.
        /// </summary>
        /// <param name="configuration">The default aggregate configuration..</param>
        public void SetDefaultConfiguration(AggregateConfiguration configuration)
        {
            lock (m_lock)
            {
                m_defaultConfiguration = configuration;
            }
        }

        /// <summary>
        /// Returns the configuration to use for a request (OPC 10000-4 §7.22.4 Table 146).
        /// With UseServerCapabilitiesDefaults = True (or no request configuration) the values of the
        /// variable's AggregateConfiguration object are used and the other request fields are
        /// ignored; without an HA Configuration the server defaults apply. Otherwise the request
        /// configuration applies unchanged.
        /// </summary>
        /// <param name="haConfiguration">The HA Configuration of the variable, if any.</param>
        /// <param name="requested">The configuration of the request, if any.</param>
        /// <returns>The configuration to use.</returns>
        public AggregateConfiguration GetConfiguration(
            HistoricalDataConfigurationState haConfiguration,
            AggregateConfiguration requested)
        {
            if (requested != null && !requested.UseServerCapabilitiesDefaults)
            {
                return requested;
            }

            AggregateConfiguration defaults = GetDefaultConfiguration(null);
            AggregateConfigurationState exposed = haConfiguration?.AggregateConfiguration;

            if (exposed == null)
            {
                return defaults;
            }

            return new AggregateConfiguration
            {
                UseServerCapabilitiesDefaults = false,
                TreatUncertainAsBad = exposed.TreatUncertainAsBad != null
                    ? exposed.TreatUncertainAsBad.Value
                    : defaults.TreatUncertainAsBad,
                PercentDataBad = exposed.PercentDataBad != null
                    ? exposed.PercentDataBad.Value
                    : defaults.PercentDataBad,
                PercentDataGood = exposed.PercentDataGood != null
                    ? exposed.PercentDataGood.Value
                    : defaults.PercentDataGood,
                UseSlopedExtrapolation = exposed.UseSlopedExtrapolation != null
                    ? exposed.UseSlopedExtrapolation.Value
                    : defaults.UseSlopedExtrapolation
            };
        }

        /// <summary>
        /// Returns the HA Configuration of a historizing variable (its child "HA Configuration",
        /// OPC 10000-11 §5.2.2), or null if it has none.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="variable">The historizing variable.</param>
        /// <returns>The HA Configuration, or null.</returns>
        public static HistoricalDataConfigurationState FindHistoricalConfiguration(
            ISystemContext context,
            NodeState variable)
        {
            if (variable == null)
            {
                return null;
            }

            return variable.FindChild(context, new QualifiedName(BrowseNames.HAConfiguration))
                as HistoricalDataConfigurationState;
        }

        /// <summary>
        /// True if the HA Configuration says Stepped = True; a missing configuration or Stepped
        /// Property means Stepped = False (OPC 10000-11 §5.2.2 default).
        /// </summary>
        /// <param name="haConfiguration">The HA Configuration, if any.</param>
        /// <returns>The Stepped value.</returns>
        public static bool IsStepped(HistoricalDataConfigurationState haConfiguration)
        {
            return haConfiguration?.Stepped != null && haConfiguration.Stepped.Value;
        }

        /// <summary>
        /// Creates a new aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        /// <param name="startTime">When to start processing.</param>
        /// <param name="endTime">When to stop processing.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether stepped interpolation should be used.</param>
        /// <param name="configuration">The configuration to use.</param>
        public IAggregateCalculator CreateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        {
            if (NodeId.IsNull(aggregateId))
            {
                return null;
            }

            AggregatorFactory factory = null;

            lock (m_lock)
            {
                if (!m_factories.TryGetValue(aggregateId, out factory))
                {
                    return null;
                }
            }

            if (configuration.UseServerCapabilitiesDefaults)
            {
                // ensure the configuration is initialized
                configuration = GetDefaultConfiguration(null);
            }

            return factory(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                stepped,
                configuration,
                m_server?.Telemetry);
        }

        /// <summary>
        /// Registers an aggregate factory.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        /// <param name="aggregateName">The id of the aggregate name.</param>
        /// <param name="factory">The factory used to create calculators.</param>
        public void RegisterFactory(
            NodeId aggregateId,
            string aggregateName,
            AggregatorFactory factory)
        {
            lock (m_lock)
            {
                m_factories[aggregateId] = factory;
            }

            m_server?.DiagnosticsNodeManager?.AddAggregateFunction(
                aggregateId,
                aggregateName,
                true);
        }

        /// <summary>
        /// Unregisters an aggregate factory.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        public void RegisterFactory(NodeId aggregateId)
        {
            lock (m_lock)
            {
                m_factories.Remove(aggregateId);
            }
        }

        private readonly object m_lock = new object();
        private readonly IServerInternal m_server;
        private AggregateConfiguration m_defaultConfiguration;
        private readonly NodeIdDictionary<AggregatorFactory> m_factories;
        private double m_minimumProcessingInterval;
    }
}
