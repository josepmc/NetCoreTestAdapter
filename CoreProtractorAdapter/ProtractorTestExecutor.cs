using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System.Diagnostics;
using System.IO;
using System.Json;
using ProtractorAdapter;
using System.Threading;

namespace ProtractorTestAdapter
{
    [ExtensionUri(ProtractorTestExecutor.ExecutorUriString)]
    public class ProtractorTestExecutor : ITestExecutor
    {
        #region Constants

        /// <summary>
        /// The Uri used to identify the XmlTestExecutor.
        /// </summary>
        public const string ExecutorUriString = "executor://ProtractorTestExecutor";

        /// <summary>
        /// The Uri used to identify the XmlTestExecutor.
        /// </summary>
        public static readonly Uri ExecutorUri = new Uri(ProtractorTestExecutor.ExecutorUriString);

        /// <summary>
        /// specifies whether execution is cancelled or not
        /// </summary>
        private bool m_cancelled;

        #endregion

        #region ITestExecutor
        public void RunTests(IEnumerable<string> sources, IRunContext runContext,
          IFrameworkHandle frameworkHandle)
        {
            //if (Debugger.IsAttached) Debugger.Break();
            //else Debugger.Launch();
            try
            {
                frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Running from process:" + Process.GetCurrentProcess() + " ID:" + Process.GetCurrentProcess().Id.ToString());
                foreach (var source in sources)
                {
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Finding tests in source:" + source);
                }

                IEnumerable<TestCase> tests = ProtractorTestDiscoverer.GetTests(sources, null);
                foreach (var test in tests)
                {
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Found test:" + test.DisplayName);
                }
                RunTests(tests, runContext, frameworkHandle);
            }
            catch(Exception e)
            {
                frameworkHandle.SendMessage(TestMessageLevel.Error, "Framework: Exception during test execution: " + e.Message);
            }
        }
        /// <summary>
        /// Runs the tests.
        /// </summary>
        /// <param name="tests">Tests to be run.</param>
        /// <param name="runContext">Context to use when executing the tests.</param>
        /// <param param name="frameworkHandle">Handle to the framework to record results and to do framework operations.</param>
        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            //if (Debugger.IsAttached) Debugger.Break();
            //else Debugger.Launch();
            m_cancelled = false;
            try
            {
                foreach (TestCase test in tests)
                {
                    if (m_cancelled)
                    {
                        break;
                    }
                    frameworkHandle.RecordStart(test);
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Starting external test for " + test.DisplayName);
                    var testOutcome = RunExternalTest(test, runContext, frameworkHandle, test);
                    frameworkHandle.RecordResult(testOutcome);
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Test result:" + testOutcome.Outcome.ToString());
                    frameworkHandle.RecordEnd(test, testOutcome.Outcome);
                }
            }
            catch(Exception e)
            {
                frameworkHandle.SendMessage(TestMessageLevel.Error, "Framework: Exception during test execution: " + e.Message);
                frameworkHandle.SendMessage(TestMessageLevel.Error, "Framework: " + e.StackTrace);
            }
        }

        private TestResult RunExternalTest(TestCase test, IRunContext runContext, IFrameworkHandle frameworkHandle, TestCase testCase)
        {
            var resultFile = RunProtractor(test, runContext, frameworkHandle);
            var testResult = GetResultsFromJsonResultFile(resultFile, testCase);

            // clean the temp file

            File.Delete(resultFile);

            return testResult;
        }

        public static TestResult GetResultsFromJsonResultFile(string resultFile, TestCase testCase)
        {
            var jsonResult = "";
            var resultOutCome = new TestResult(testCase);
            if (File.Exists(resultFile))
            {

                using (var stream = File.OpenRead(resultFile))
                {
                    using (var textReader = new StreamReader(stream))
                    {
                        jsonResult = textReader.ReadToEnd();
                    }
                    stream.Close();
                }
            }
            else
            {
                resultOutCome.Outcome = TestOutcome.Failed;
                resultOutCome.ErrorMessage = "Framework: Error! No results were created. Check your arguments, use /logger:console,verbosity=detailed or /diag:results.log";
                return resultOutCome;
            }

            var results = JsonObject.Parse(jsonResult);
            resultOutCome.Outcome = TestOutcome.Passed;
            foreach (JsonObject result in results)
            {
                foreach (JsonObject assert in result["assertions"])
                {
                    if (!assert["passed"])
                    {
                        resultOutCome.Outcome = TestOutcome.Failed;
                        resultOutCome.ErrorStackTrace = $"{resultOutCome.ErrorStackTrace}\n{assert["stackTrace"]}";
                        resultOutCome.ErrorStackTrace = $"{resultOutCome.ErrorMessage}\n{assert["errorMsg"]}";
                    }
                }
            }

            return resultOutCome;
        }

        private string RunProtractor(TestCase test, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            var resultFile = Path.GetFileNameWithoutExtension(test.CodeFilePath);
            resultFile += ".result.json";

            resultFile = AppConfig.ResultsPath ?? Path.GetTempPath() + Path.DirectorySeparatorChar + resultFile;
            frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Using result file: " + resultFile);
            // Can't use test.Source as it's going to be in lowercase
            var cwd = Helper.FindInDirectoryTree(test.CodeFilePath, "package.json");
            var exe = Helper.FindExePath(AppConfig.Program);

            //frameworkHandle.SendMessage(TestMessageLevel.Error, "Framework: Exe " + exe);
            //frameworkHandle.SendMessage(TestMessageLevel.Error, "Framework: Cwd " + cwd);
            //frameworkHandle.SendMessage(TestMessageLevel.Error, "Framework: ResultFile " + resultFile);

            ProcessStartInfo info = new ProcessStartInfo()
            {
                Arguments = string.Format("{0} --resultJsonOutputFile \"{1}\" --specs \"{2}\"", AppConfig.Arguments, resultFile, test.CodeFilePath),
                FileName = exe,
                WorkingDirectory = cwd,//runContext.SolutionDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            frameworkHandle.SendMessage(TestMessageLevel.Informational, $"Framework: Starting {exe} on '{cwd}' with arguments: {info.Arguments}");


            Process p = new Process();
            p.StartInfo = info;

            using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
            using (AutoResetEvent errorWaitHandle = new AutoResetEvent(false))
            {
                p.OutputDataReceived += (sender, e) => {
                    if (e.Data == null)
                    {
                        outputWaitHandle.Set();
                    }
                    else if(!String.IsNullOrWhiteSpace(e.Data))
                    {
                        frameworkHandle.SendMessage(TestMessageLevel.Warning, e.Data);
                    }
                };
                p.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        errorWaitHandle.Set();
                    }
                    else if (!String.IsNullOrWhiteSpace(e.Data))
                    {
                        frameworkHandle.SendMessage(TestMessageLevel.Error, e.Data);
                    }
                };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                outputWaitHandle.WaitOne(10000);
                errorWaitHandle.WaitOne(10000);
                    // Process completed. Check process.ExitCode here.
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, "Framework: Complete. Exit code: " + p.ExitCode.ToString());
            }

            return resultFile;
        }

        /// <summary>
        /// Cancel the execution of the tests
        /// </summary>
        public void Cancel()
        {
            m_cancelled = true;
        }


        #endregion

    }

}
