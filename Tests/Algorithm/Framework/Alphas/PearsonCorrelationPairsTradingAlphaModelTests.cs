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
using Python.Runtime;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Interfaces;
using QuantConnect.Tests.Common.Data.UniverseSelection;
using QuantConnect.Tests.Engine.DataFeeds;

namespace QuantConnect.Tests.Algorithm.Framework.Alphas
{
    [TestFixture]
    public class PearsonCorrelationPairsTradingAlphaModelTests
    {
        [TestCase(Language.CSharp, 0)]
        [TestCase(Language.CSharp, 1)]
        [TestCase(Language.CSharp, 2)]
        [TestCase(Language.Python, 0)]
        [TestCase(Language.Python, 1)]
        [TestCase(Language.Python, 2)]
        public void WaitsForAPairAndSelectsWhenHistoryBecomesAvailable(Language language, int initialCount)
        {
            using (Py.GIL())
            {
                var algorithm = new QCAlgorithm();
                algorithm.SetPandasConverter();
                algorithm.SubscriptionManager.SetDataManager(new DataManagerStub(algorithm));
                algorithm.SetDateTime(new DateTime(2024, 1, 3, 16, 0, 0));
                var securities = new[] { algorithm.AddEquity("SPY"), algorithm.AddEquity("IBM") };
                var historyAvailable = initialCount != 2;
                var provider = new Mock<IHistoryProvider>();
                provider.Setup(x => x.GetHistory(It.IsAny<IEnumerable<HistoryRequest>>(), It.IsAny<DateTimeZone>()))
                    .Returns((IEnumerable<HistoryRequest> requests, DateTimeZone zone) => historyAvailable
                        ? CreateHistory(requests.Select(x => x.Symbol).Distinct().ToArray()) : Enumerable.Empty<Slice>());
                algorithm.HistoryProvider = provider.Object;

                using var pythonModel = language == Language.Python
                    ? Py.Import("PearsonCorrelationPairsTradingAlphaModel").GetAttr("PearsonCorrelationPairsTradingAlphaModel").Invoke()
                    : null;
                IAlphaModel model = pythonModel == null
                    ? new PearsonCorrelationPairsTradingAlphaModel()
                    : new AlphaModelPythonWrapper(pythonModel);
                bool HasPassed(Symbol first, Symbol second)
                {
                    if (pythonModel == null)
                    {
                        return ((PearsonCorrelationPairsTradingAlphaModel)model).HasPassedTest(algorithm, first, second);
                    }
                    dynamic instance = pythonModel;
                    return (bool)instance.has_passed_test(algorithm, first, second);
                }

                var initial = SecurityChangesTests.CreateNonInternal(securities.Take(initialCount).ToArray(), Array.Empty<QuantConnect.Securities.Security>());
                Assert.DoesNotThrow(() => model.OnSecuritiesChanged(algorithm, initial));
                Assert.IsFalse(HasPassed(securities[0].Symbol, securities[1].Symbol));
                Assert.IsFalse(HasPassed(securities[0].Symbol, securities[0].Symbol));

                historyAvailable = true;
                var added = SecurityChangesTests.CreateNonInternal(securities.Skip(initialCount).ToArray(), Array.Empty<QuantConnect.Securities.Security>());
                Assert.DoesNotThrow(() => model.OnSecuritiesChanged(algorithm, added));
                Assert.IsTrue(HasPassed(securities[0].Symbol, securities[1].Symbol)
                    || HasPassed(securities[1].Symbol, securities[0].Symbol));
            }
        }

        private static IEnumerable<Slice> CreateHistory(Symbol[] symbols)
        {
            var prices = new[]
            {
                new[] { 100m, 101m, 103m, 102m, 105m, 106m },
                new[] { 200m, 203m, 206m, 205m, 211m, 212m }
            };
            for (var i = 0; i < prices[0].Length; i++)
            {
                var time = new DateTime(2024, 1, 3, 10, 0, 0).AddMinutes(i);
                var bars = symbols.Select((symbol, index) => (BaseData)new TradeBar(time, symbol,
                    prices[index][i], prices[index][i], prices[index][i], prices[index][i], 1000)).ToList();
                yield return new Slice(time.AddMinutes(1), bars, time.AddHours(5).AddMinutes(1));
            }
        }
    }
}
