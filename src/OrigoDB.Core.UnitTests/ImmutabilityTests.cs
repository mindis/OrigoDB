﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OrigoDB.Core.Configuration;

namespace OrigoDB.Core.Test
{
    class ImmutableModel : Model
    {
        private readonly List<int> _numbers;

        public ImmutableModel()
            : this(Enumerable.Empty<int>())
        {
            
        }

        public ImmutableModel(IEnumerable<int> numbers)
        {
            _numbers = new List<int>(numbers);
        }

        public IEnumerable<int> Numbers()
        {
            foreach (int n in _numbers) yield return n;
        }

        private IEnumerable<int> WithNumber(int n)
        {
            foreach (var number in Numbers())
            {
                yield return number;
            }
            yield return n;
        }

        public ImmutableModel WithNewNumber(int number)
        {
            return new ImmutableModel(WithNumber(number));
        }
    }

    class AppendNumberCommand : ImmutabilityCommand<ImmutableModel>
    {
        public readonly int Number;

        public AppendNumberCommand(int number)
        {
            Number = number;
        }

        public override ImmutableModel ExecuteImmutably(ImmutableModel model)
        {
            return model.WithNewNumber(Number);
        }
    }

    internal class AppendNumberAndGetSumCommand : ImmutabilityCommand<ImmutableModel, int>
    {
        public readonly int Number;

        public AppendNumberAndGetSumCommand(int number)
        {
            Number = number;
        }
        public override Tuple<ImmutableModel, int> ExecuteImmutably(ImmutableModel model)
        {
            ImmutableModel newModel = model.WithNewNumber(Number);
            int sum = newModel.Numbers().Sum();
            return Tuple.Create(newModel, sum);
        }
    }

    class NumberSumQuery : Query<ImmutableModel, int>
    {
        protected override int Execute(ImmutableModel model)
        {
            return model.Numbers().Sum();
        }
    }


    
    [TestFixture]
    public class ImmutabilityTests
    {
        [Test]
        public void ImmutabilityKernelSmokeTest()
        {
            var config = EngineConfiguration.Create();
            config.SetSynchronizerFactory(c => new NullSynchronizer());
            var model = new ImmutableModel();
            ImmutabilityKernel kernel = new ImmutabilityKernel(config, model);

            int actual = (int) kernel.ExecuteCommand(new AppendNumberAndGetSumCommand(42));
            Assert.AreEqual(42, actual);
            kernel.ExecuteCommand(new AppendNumberCommand(58));
            actual = kernel.ExecuteQuery(new NumberSumQuery());
            Assert.AreEqual(actual, 42 + 58);
        }

        [Test]
        public void ImmutabilityEngineSmokeTest()
        {
            var config = EngineConfiguration.Create();
            config.SetSynchronizerFactory(c => new NullSynchronizer());
            config.Kernel = Kernels.Immutability;
            config.SetCommandJournalFactory(c => new NullJournal());
            var model = new ImmutableModel();
            var engine = new Engine<ImmutableModel>(model, new NullStore(), config);

            int actual = engine.Execute(new AppendNumberAndGetSumCommand(42));
            Assert.AreEqual(42, actual);
            engine.Execute(new AppendNumberCommand(58));
            actual = engine.Execute(new NumberSumQuery());
            Assert.AreEqual(actual, 42 + 58);
        }

        
        
    }
}