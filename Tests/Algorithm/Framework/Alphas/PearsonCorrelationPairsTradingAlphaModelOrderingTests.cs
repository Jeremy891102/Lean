/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NodaTime;
using NUnit.Framework;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Tests.Common.Data.UniverseSelection;
using QuantConnect.Tests.Engine.DataFeeds;

namespace QuantConnect.Tests.Algorithm.Framework.Alphas
{
    [TestFixture]
    public class PearsonCorrelationPairsTradingAlphaModelOrderingTests
    {
        [TestCase(Resolution.Minute, false)]
        [TestCase(Resolution.Daily, false)]
        [TestCase(Resolution.Daily, true)]
        public void SelectedSymbolsMatchCorrelatedVectors(Resolution resolution, bool multipleTimeZones)
        {
            var algorithm = new QCAlgorithm();
            algorithm.SubscriptionManager.SetDataManager(new DataManagerStub(algorithm));
            algorithm.SetDateTime(new DateTime(2024, 1, 8));
            var securities = new[]
            {
                algorithm.AddEquity("US1", resolution),
                algorithm.AddEquity("USMID", resolution, multipleTimeZones ? Market.India : Market.USA),
                algorithm.AddEquity("USZ", resolution)
            };
            Assert.AreEqual(multipleTimeZones ? 2 : 1, securities.Select(x => x.Exchange.TimeZone).Distinct().Count());
            // SecurityChanges orders additions by ticker, so these names preserve the intended universe order.
            var symbols = securities.Select(x => x.Symbol).ToArray();
            var history = CreateHistory(symbols, resolution, multipleTimeZones).ToArray();
            CollectionAssert.AreEqual(multipleTimeZones ? new[] { symbols[1], symbols[0], symbols[2] } : symbols,
                history.SelectMany(x => x.Values).Select(x => x.Symbol).Distinct());
            var provider = new Mock<IHistoryProvider>();
            provider.Setup(x => x.GetHistory(It.IsAny<IEnumerable<HistoryRequest>>(), It.IsAny<DateTimeZone>()))
                .Returns(history);
            algorithm.HistoryProvider = provider.Object;
            var model = new PearsonCorrelationPairsTradingAlphaModel(resolution: resolution);
            model.OnSecuritiesChanged(algorithm, SecurityChangesTests.CreateNonInternal(securities, Array.Empty<QuantConnect.Securities.Security>()));

            CollectionAssert.AreEqual(symbols, model.Securities.Select(x => x.Symbol));
            Assert.IsTrue(model.HasPassedTest(algorithm, symbols[0], symbols[2]), "The two positively correlated US return series must be selected.");
            Assert.IsFalse(model.HasPassedTest(algorithm, symbols[1], symbols[2]));
            Assert.IsFalse(model.HasPassedTest(algorithm, symbols[2], symbols[1]));
            Assert.IsFalse(model.HasPassedTest(algorithm, symbols[0], symbols[1]));
            Assert.IsFalse(model.HasPassedTest(algorithm, symbols[1], symbols[0]));
        }

        private static IEnumerable<Slice> CreateHistory(Symbol[] symbols, Resolution resolution, bool multipleTimeZones)
        {
            var returns = new[]
            {
                new[] { 0.01, 0.02, -0.01 },
                new[] { 0.1, -0.2, 0.3 },
                new[] { 0.012, 0.015, -0.008 }
            };
            var prices = new[] { 100m, 100m, 100m };
            var period = resolution.ToTimeSpan();
            var order = new[] { 1, 0, 2 };
            for (var i = 0; i <= returns[0].Length; i++)
            {
                var date = new DateTime(2024, 1, 2);
                var time = resolution == Resolution.Daily ? date.AddDays(i) : date.AddHours(10).AddMinutes(i);
                var bars = new List<BaseData>();
                foreach (var index in order)
                {
                    if (i > 0)
                    {
                        prices[index] *= (decimal)Math.Exp(returns[index][i - 1]);
                    }
                    bars.Add(new TradeBar(time - period, symbols[index], prices[index], prices[index], prices[index], prices[index], 1000, period));
                }
                if (multipleTimeZones)
                {
                    // The earlier-closing exchange arrives in a separate slice on the same history date.
                    yield return new Slice(time.AddHours(4), bars.Take(1).ToList(), time.AddHours(9));
                    yield return new Slice(time.AddHours(16), bars.Skip(1).ToList(), time.AddHours(21));
                }
                else
                {
                    yield return new Slice(time, bars, time.AddHours(5));
                }
            }
        }
    }
}
