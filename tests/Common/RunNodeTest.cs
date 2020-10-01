using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mirror.SimpleWeb.Tests
{
    public static class RunNode
    {
        static Thread mainThread = Thread.CurrentThread;

        public struct Result
        {
            public bool timedOut;
            public string[] output;
            public string[] error;

            public void AssetOutput(params string[] messages)
            {
                AssertLogs(nameof(output), output, messages);
            }

            public void AssetErrors(params string[] messages)
            {
                AssertLogs(nameof(error), error, messages);
            }

            static void AssertLogs(string label, string[] logs, string[] expected)
            {
                int length = expected.Length;
                Assert.That(logs, Has.Length.EqualTo(length), $"{label} should have {length} logs. Logs: \n{WriteLogs(logs)}");

                for (int i = 0; i < length; i++)
                {
                    Assert.That(logs[i].ToLower(), Is.EqualTo(expected[i].ToLower()));
                }
            }

            static string WriteLogs(string[] lines)
            {
                IEnumerable<int> range = Enumerable.Range(0, lines.Length);
                return string.Join("", Enumerable.Zip(range, lines, (i, line) => $"{i}: {line}\n"));
            }

            public void AssetTimeout(bool expected)
            {
                Assert.That(timedOut, Is.EqualTo(expected),
                    expected
                    ? "nodejs should have timed out"
                    : "nodejs should close before timeout"
                    );
            }
        }

        public static Result Run(string scriptName, bool continueOnCapturedContext, int msTimeout = 5000)
        {
            Task<RunNode.Result> task = RunAsync(scriptName, msTimeout, continueOnCapturedContext);
            task.Wait();
            return task.Result;
        }
        public static async Task<Result> RunAsync(string scriptName, int msTimeout = 5000, bool continueOnCapturedContext = true)
        {
            string fullPath = ResolvePath(scriptName);

            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"--no-warnings {fullPath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false, // needs to be false for redirect
                };

                process.Start();
                StreamReader output = process.StandardOutput;
                StreamReader error = process.StandardError;

                await WaitForEnd(process, msTimeout).ConfigureAwait(continueOnCapturedContext);

                bool timeoutReached = !process.HasExited;
                if (timeoutReached)
                    process.Kill();

                string outputString = output.ReadToEnd();
                string errorString = error.ReadToEnd();

                UnityEngine.Debug.Log($"node outputStream: {outputString}");
                UnityEngine.Debug.Log($"node errorStream: {errorString}");

                return new Result
                {
                    timedOut = timeoutReached,
                    // split and remove empties
                    output = outputString.Split('\n').Where(x => !string.IsNullOrEmpty(x)).ToArray(),
                    error = errorString.Split('\n').Where(x => !string.IsNullOrEmpty(x)).ToArray(),
                };
            }
        }

        static async Task WaitForEnd(Process process, int msTimeout)
        {
            bool cancel = false;

            _ = Task.Run(async () =>
            {
                await Task.Delay(msTimeout).ConfigureAwait(false);

                cancel = true;
            });

            while (!process.HasExited)
            {
                if (cancel)
                {
                    UnityEngine.Debug.Log($"<color=red>NodeRun Timeout reached</color>");

                    return;
                }

                // ConfigureAwait so it runs on side thread
                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        public static string ResolvePath(string path)
        {
            string full = Path.Combine(NodeDir, path);
            if (full.StartsWith(Application.dataPath))
            {
                full = "Assets" + full.Substring(Application.dataPath.Length);
            }
            return full;
        }
        static string _nodeDir;
        static string NodeDir
        {
            get
            {
                if (string.IsNullOrEmpty(_nodeDir))
                {
                    string[] guidsFound = AssetDatabase.FindAssets($"t:Script " + nameof(RunNode));
                    if (guidsFound.Length == 1 && !string.IsNullOrEmpty(guidsFound[0]))
                    {
                        // tests/common/RunNode.cs
                        string script = AssetDatabase.GUIDToAssetPath(guidsFound[0]);
                        // tests/common/
                        string dir = Path.GetDirectoryName(script);
                        // tests/node~/
                        _nodeDir = Path.Combine(dir, "../", "node~/");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("Could not find path of RunNode");
                    }
                }
                return _nodeDir;
            }
        }
    }
}
