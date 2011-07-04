﻿using System.Linq;
using System.Collections.Generic;
using AutoTest.TestRunners.Shared;
using AutoTest.TestRunners.Shared.Logging;
using AutoTest.TestRunners.Shared.AssemblyAnalysis;
using AutoTest.TestRunners.Shared.Results;
using AutoTest.TestRunners.Shared.Options;
using AutoTest.TestRunners.MSTest.Extensions;
using AutoTest.TestRunners.Shared.Communication;

namespace AutoTest.TestRunners.MSTest
{
    public class Runner : IAutoTestNetTestRunner
    {
        private ILogger _logger;
        private ITestFeedbackProvider _channel = new NullTestFeedbackProvider();

        public string Identifier { get { return "MSTest"; } }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void SetLiveFeedbackChannel(ITestFeedbackProvider channel)
        {
            _channel = channel;
        }

        public bool IsTest(string assembly, string member)
        {
            var locator = new SimpleTypeLocator(assembly);
            var method = locator.LocateMethod(member);
            if (method == null)
                return false;
            return method.Attributes.Contains("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");
        }

        public bool ContainsTestsFor(string assembly, string member)
        {
            var locator = new SimpleTypeLocator(assembly);
            var cls = locator.LocateClass(member);
            if (cls == null)
                return false;
            return cls.Attributes.Contains("Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute");
        }

        public bool ContainsTestsFor(string assembly)
        {
            var parser = new AssemblyReader();
            return parser.GetReferences(assembly).Contains("Microsoft.VisualStudio.QualityTools.UnitTestFramework");
        }

        public bool Handles(string identifier)
        {
            return identifier.ToLower().Equals(identifier.ToLower());
        }

        public IEnumerable<TestResult> Run(RunSettings settings)
        {
            return new CelerRunner(_logger, _channel).Run(settings);
        }
    }
}
