﻿using System;
using System.Collections.Generic;
using System.Linq;
using AutoTest.Core.Caching.Projects;
using AutoTest.Messages;
using AutoTest.Core.Messaging.MessageConsumers;
using AutoTest.Core.Configuration;
using AutoTest.TestRunners.Shared;
using AutoTest.TestRunners.Shared.Plugins;
using System.IO;
using System.Diagnostics;
using AutoTest.TestRunners.Shared.Options;
using AutoTest.TestRunners.Shared.Results;
using AutoTest.Core.Caching.RunResultCache;
using AutoTest.Core.Messaging;

namespace AutoTest.Core.TestRunners.TestRunners
{
    public class AutoTestTestRunner : ITestRunner
    {
        private readonly IConfiguration _configuration;
        private readonly IMessageBus _bus;
        private readonly IRunResultCache _runCache;
        private bool _handleRunnerFeedback = true;

        public AutoTestTestRunner(IConfiguration configuration, IMessageBus bus, IRunResultCache runCache)
        {
            _configuration = configuration;
            _bus = bus;
            _runCache = runCache;
        }

        public void DisableRunnerFeedback()
        {
            _handleRunnerFeedback = false;
        }

        public bool CanHandleTestFor(Project project)
        {
            if (!_configuration.UseAutoTestTestRunner)
                return false;
            return CanHandleTestFor(project.GetAssembly(_configuration.CustomOutputPath));
        }

        public bool CanHandleTestFor(string assembly)
        {
            if (!_configuration.UseAutoTestTestRunner)
                return false;
            if (!File.Exists(assembly))
                return false;
            var plugins = new PluginLocator().Locate();
            foreach (var plugin in plugins)
            {
                var instance = plugin.New();
                if (instance == null)
                    continue;
                if (instance.ContainsTestsFor(assembly))
                    return true;
            }
            return false;
        }

        public TestRunResults[] RunTests(TestRunInfo[] runInfos, Action<AutoTest.TestRunners.Shared.Targeting.Platform,Version,Action<ProcessStartInfo, bool>> processWrapper, Func<bool> abortWhen)
        {
            var options = generateOptions(runInfos);
            if (options == null)
                return new TestRunResults[] { };
            AutoTestRunnerFeedback feedback = null;
            if (_handleRunnerFeedback)
                feedback = new AutoTestRunnerFeedback(_runCache, _bus, options);
            var runner = new TestRunProcess(feedback);
				/*.WrapTestProcessWith(processWrapper)
                .AbortWhen(abortWhen);
            if (_configuration.RunAssembliesInParallel)
                runner.RunParallel();
            if (_configuration.TestRunnerCompatibilityMode)
                runner.RunInCompatibilityMode();*/
            var session = runner.Prepare(options);
			
			foreach (var rnr in options.TestRuns) {
				foreach (var asm in rnr.Assemblies) {
                    DebugLog.Debug.WriteDebug("Loading runner {1} for {0}", asm.Assembly, rnr.ID);
					session.CreateClient(asm.Assembly, rnr.ID, abortWhen).Load();
                }
			}

			var plugins = 
				new PluginLocator().Locate()
				.Select(x => x.New());
			
			var results = new List<TestRunResults>();
			session.Instances.ToList()
				.ForEach(x => {
                    DebugLog.Debug.WriteDebug("Testing {0} - {1}", x.Runner, x.Assembly);
					var runAll = false;

					var plugin = 
						plugins.FirstOrDefault(y => y.Identifier.ToLower() == x.Runner.ToLower());
					var testRunner = TestRunnerConverter.FromString(plugin.Identifier);
					
					if (plugin != null) {
                        DebugLog.Debug.WriteDebug("Plugin found");
						var testRun = new TestRunOptions();
						testRun.HasBeenVerified(true);
						var infos = runInfos.Where(y => plugin.ContainsTestsFor(y.Assembly));
						foreach (var info in  infos) {
							testRun.AddTests(info.GetTestsFor(testRunner));
							DebugLog.Debug.WriteDetail(
								"Found {0} tests for assembly", testRun.Tests.Count());
							testRun.AddMembers(info.GetMembersFor(testRunner));
							DebugLog.Debug.WriteDetail(
								"Found {0} members for assembly", testRun.Members.Count());
							testRun.AddNamespaces(info.GetNamespacesFor(testRunner));
							DebugLog.Debug.WriteDetail(
								"Found {0} namespaces for assembly", testRun.Namespaces.Count());
							DebugLog.Debug.WriteDetail(
								"Run only specified tests for runner {0} is {1}",
								testRunner,
								info.OnlyRunSpcifiedTestsFor(testRunner));

							if (info.OnlyRunSpcifiedTestsFor(testRunner)) {
								runAll = true;
								break;
							}
						}

						if (runAll)
							testRun = new TestRunOptions();

						var client = session.CreateClient(x, abortWhen);
						results.AddRange(
							getResults(
								client.RunTests(
									testRun,
									(test) => {
                                        DebugLog.Debug.WriteDebug("\tTesting {0}", test);
									},
									(result) => {
                                        DebugLog.Debug.WriteDebug("\tTesting {0} - {1}", result.TestDisplayName, result.State);
									}),
								runInfos));
					}
				});

            session.Kill();
            _handleRunnerFeedback = true;
            DebugLog.Debug.WriteDebug("Result contains {0} records", results.Count);
            foreach (var set in results)
                DebugLog.Debug.WriteDebug("Resultset for {1} contains {0} tests", set.All.Count(), set.Assembly);
			return results.ToArray();
        }

        private TestRunResults[] getResults(IEnumerable<AutoTest.TestRunners.Shared.Results.TestResult> tests, TestRunInfo[] runInfos)
        {
            var results = new List<TestRunResults>();
            foreach (var byRunner in tests.GroupBy(x => x.Runner))
            {
                var runner = TestRunnerConverter.FromString(byRunner.Key);
                foreach (var byAssembly in byRunner.GroupBy(x => x.Assembly))
                {
                    var info = runInfos.Where(x => x.Assembly.Equals(byAssembly.Key)).FirstOrDefault();
                    var project = "";
                    var partial = false;
                    if (info != null)
                    {
                        if (info.Project != null)
                            project = info.Project.Key;
                        partial = info.OnlyRunSpcifiedTestsFor(runner) ||
                                  info.GetTestsFor(runner).Count() > 0 ||
                                  info.GetMembersFor(runner).Count() > 0 ||
                                  info.GetNamespacesFor(runner).Count() > 0;
                    }
                    DebugLog.Debug.WriteDetail(string.Format("Partial run is {0} for runner {1}", partial, runner));
                    
                    var result = new TestRunResults(
                                        project,
                                        byAssembly.Key,
                                        partial,
                                        runner,
                                        byAssembly.Select(x => ConvertResult(x)).ToArray());
                    result.SetTimeSpent(TimeSpan.FromMilliseconds(byAssembly.Sum(x => x.DurationInMilliseconds)));
                    results.Add(result);
                }
            }
            return results.ToArray();
        }

        public static Messages.TestResult ConvertResult(AutoTest.TestRunners.Shared.Results.TestResult x)
        {
            return new Messages.TestResult(TestRunnerConverter.FromString(x.Runner),
                                            getTestState(x.State),
                                            x.TestName,
                                            x.Message,
                                            x.StackLines.Select(y => (IStackLine)new StackLineMessage(y.Method, y.File, y.Line)).ToArray<IStackLine>(),
                                            x.DurationInMilliseconds
                                            ).SetDisplayName(x.TestDisplayName);
        }

        private static TestRunStatus getTestState(TestState testState)
        {
            switch (testState)
            {
                case TestState.Failed:
                case TestState.Panic:
                    return TestRunStatus.Failed;
                case TestState.Ignored:
                    return TestRunStatus.Ignored;
                case TestState.Passed:
                    return TestRunStatus.Passed;
            }
            return TestRunStatus.Failed;
        }

        private RunOptions generateOptions(TestRunInfo[] runInfos)
        {
            var options = new RunOptions();
            var plugins = new PluginLocator().Locate();
            foreach (var plugin in plugins)
            {
                var testRun = getTests(plugin, runInfos);
                if (testRun != null)
                    options.AddTestRun(testRun);
            }
                
            if (options.TestRuns.Count() == 0)
                return null;
            return options;
        }

        private RunnerOptions getTests(Plugin plugin, TestRunInfo[] runInfos)
        {
            var instance = plugin.New();
            if (instance == null)
                return null;
            var infos = runInfos.Where(x => instance.ContainsTestsFor(x.Assembly));
            if (infos.Count() == 0)
                return null;
            return getRunnerOptions(infos, instance);
        }

        private RunnerOptions getRunnerOptions(IEnumerable<TestRunInfo> unitInfos, IAutoTestNetTestRunner instance)
        {
            DebugLog.Debug.WriteDetail("Getting runner options for {0}", instance.Identifier);
            var runner = new RunnerOptions(instance.Identifier);
            runner.AddCategories(_configuration.TestCategoriesToIgnore);
            var testRunner = TestRunnerConverter.FromString(instance.Identifier);
            foreach (var info in unitInfos)
            {
				DebugLog.Debug.WriteDetail("Handling {0}", info.Assembly);
				DebugLog.Debug.WriteDetail("About to add assembly");
                var assembly = new AssemblyOptions(info.Assembly);
				DebugLog.Debug.WriteDetail("Adding assembly");
                runner.AddAssembly(assembly);
            }
            return runner;
        }
    }

    public class AutoTestRunnerFeedback : ITestRunProcessFeedback
    {
        private IRunResultCache _runCache;
        private IMessageBus _bus;
        private RunOptions _options;

        private int _totalTestCount = -1;
        private DateTime _lastSend = DateTime.MinValue;
        private object _padLock = new object();
        private string _currentAssembly = "";
        private int _testCount = 0;
        private string _currentTest = "";

        public AutoTestRunnerFeedback(IRunResultCache cache, IMessageBus bus, RunOptions options)
        {
            _runCache = cache;
            _bus = bus;
            _options = options;
            setTotalTestCount();
        }

        private void setTotalTestCount()
        {
			// TODO Fix
            /*if (_options.TestRuns.Count(x => x.Assemblies.Count(y => (y.Members.Count() > 0 || y.Namespaces.Count() > 0)) > 0) > 0)
                _totalTestCount = -1;
            else
                _totalTestCount = _options.TestRuns.Sum(x => x.Assemblies.Sum(y => y.Tests.Count()));*/

        }

        public void ProcessStart(string commandline)
        {
            DebugLog.Debug.WriteInfo("Running tests: " + commandline);
        }

        public void TestStarted(string signature)
        {
            _currentTest = signature;
        }

        public void TestFinished(AutoTest.TestRunners.Shared.Results.TestResult result)
        {
            lock (_padLock)
            {
                if (_lastSend == DateTime.MinValue)
                    _lastSend = DateTime.Now;
                _currentAssembly = result.Assembly;
                _testCount++;
                if (result.State == TestState.Passed && _runCache.Failed.Count(x => x.Value.Name.Equals(result.TestName)) != 0)
                {
                    _bus.Publish(
                        new LiveTestStatusMessage(
                            _currentAssembly,
                            _currentTest,
                            _totalTestCount,
                            _testCount,
                            new LiveTestStatus[] { },
                            new LiveTestStatus[] { new LiveTestStatus(result.Assembly, AutoTestTestRunner.ConvertResult(result)) }));
                    _lastSend = DateTime.Now;
                    return;
                }
                if (result.State == TestState.Failed)
                {
                    _bus.Publish(
                        new LiveTestStatusMessage(
                            _currentAssembly,
                            _currentTest,
                            _totalTestCount,
                            _testCount,
                            new LiveTestStatus[] { new LiveTestStatus(result.Assembly, AutoTestTestRunner.ConvertResult(result)) },
                            new LiveTestStatus[] { }));
                    _lastSend = DateTime.Now;
                    return;
                }
                if (DateTime.Now > _lastSend.AddSeconds(1))
                {
                    _bus.Publish(
                        new LiveTestStatusMessage(
                            _currentAssembly,
                            _currentTest,
                            _totalTestCount,
                            _testCount,
                            new LiveTestStatus[] { },
                            new LiveTestStatus[] { }));
                    _lastSend = DateTime.Now;
                }
            }
        }

		public void ProcessEnd(int count)
		{			
		}
    }
}
