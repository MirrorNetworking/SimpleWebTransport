using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
#if SIMPLEWEB_PROFILING_ENABLED
using System.Linq;
#if UNITY_EDITOR || UNITY_SERVER
using System.IO;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
#endif

namespace Mirror.SimpleWeb.Profiling
{
    public class Measure
    {
        const string SIMPLEWEB_PROFILING_ENABLED = nameof(SIMPLEWEB_PROFILING_ENABLED);

        static Measure()
        {
            // do nothing if not profiling
#if SIMPLEWEB_PROFILING_ENABLED
            sw = Stopwatch.StartNew();
            All = new ConcurrentDictionary<string, Measure>();
#endif
        }

        public static Stopwatch sw;
        [ThreadStatic] public static Measure threadInstance;
        public static ConcurrentDictionary<string, Measure> All;
        public static volatile bool TakeMeasurements = false;

        [Conditional(SIMPLEWEB_PROFILING_ENABLED)]
        public static void CreateThreadInstance(string label)
        {
            threadInstance = new Measure();
            All.TryAdd(label, threadInstance);
            UnityEngine.Debug.Log($"Add Measure {label}");
        }
        [Conditional(SIMPLEWEB_PROFILING_ENABLED)]
        public static void DestroyThreadInstance(string label)
        {
            threadInstance = null;
            All.TryRemove(label, out Measure _);
            UnityEngine.Debug.Log($"Remove Measure {label}");
        }

        public bool writtenToFile;

        public readonly object lockObj = new object();
        public readonly List<Call> calls = new List<Call>();

        double start;

        private Measure() { }

        [Conditional(SIMPLEWEB_PROFILING_ENABLED)]
        public static void Start()
        {
            threadInstance.start = sw.Elapsed.TotalMilliseconds;
        }

        [Conditional(SIMPLEWEB_PROFILING_ENABLED)]
        public static void End(int bytesWriten)
        {
            double end = sw.Elapsed.TotalMilliseconds;
            if (TakeMeasurements)
            {
                lock (threadInstance.lockObj)
                {
                    threadInstance.calls.Add(new Call
                    {
                        start = threadInstance.start,
                        end = end,
                        bytesWriten = bytesWriten,
                    });
                }
            }
        }

        public struct Call
        {
            public double start;
            public double end;
            public int bytesWriten;

            public double delta => end - start;
        }
    }

    public class SslBenchmarkRunner : MonoBehaviour
    {
        public int writeTime = 10_000;
        public int expectedMeasures = 8;
        private double startTime = double.MaxValue;
        private string outFile;

        // only enable if profiling and server/editor
#if SIMPLEWEB_PROFILING_ENABLED && (UNITY_SERVER || UNITY_EDITOR) 
        private void Update()
        {
            if (!NetworkServer.active) { return; }

            if (!Measure.TakeMeasurements && Measure.All.Count == expectedMeasures)
            {
                UnityEngine.Debug.Log("Starting Measurements");
                Measure.TakeMeasurements = true;
                startTime = Measure.sw.Elapsed.TotalMilliseconds;
                //outFile = $"./sslBenchmark/results-{DateTime.Now:yyyy-MM-dd--HH-mm-ss}.txt";
                outFile = $"./sslBenchmark/results.txt";

                if (File.Exists(outFile)) { File.AppendAllText(outFile, "\n\n"); }
                else { File.WriteAllText(outFile, ""); }

                File.AppendAllText(outFile,
                    $"{"".PadRight(20, '-')}\n" +
                    $"[" +
                        $"ssl:{FindObjectOfType<SimpleWebTransport>().sslEnabled,-7}" +
                        $"batch:{SendLoopConfig.batchSend,-7}" +
                        $"flush:{SendLoopConfig.flushAfterSend,-7}" +
                        $"sleep:{SendLoopConfig.sleepBeforeSend,-7}" +
                    $"]\n" +
                    $"{"".PadRight(20, '-')}\n");
            }
            double time = Measure.sw.Elapsed.TotalMilliseconds;
            if (time > startTime + writeTime)
            {
                foreach (KeyValuePair<string, Measure> kvp in Measure.All.OrderBy(x => x.Key))
                {
                    if (!kvp.Value.writtenToFile)
                    {
                        kvp.Value.writtenToFile = true;
                        string label = kvp.Key;
                        string text = GetOutput(kvp);
                        File.AppendAllText(outFile, text + "\n");
                        UnityEngine.Debug.Log($"writen {label}");
                    }
                }
#if UNITY_EDITOR
                EditorApplication.ExitPlaymode();
#else
                Application.Quit();
#endif
            }
        }
#endif

        public static string GetOutput(KeyValuePair<string, Measure> kvp)
        {
            Measure.Call[] calls;
            double time = Measure.sw.Elapsed.TotalMilliseconds;
            lock (kvp.Value.lockObj)
            {
                calls = kvp.Value.calls.ToArray();
            }

            int callCount = calls.Length;
            if (callCount == 0)
            {
                return $"{kvp.Key,20} NO CALLS";
            }

            double[] times = calls.Select(x => x.delta).ToArray();
            double timeAvg = times.Average();
            double timeMax = times.Max();
            double timeTotal = times.Sum();

            long[] bytes = calls.Select(x => (long)x.bytesWriten).ToArray();
            long bytesAvg = (long)bytes.Average();
            long bytesMax = bytes.Max();
            long bytesTotal = bytes.Sum();

            string text = $"{kvp.Key,-20} Calls:{callCount,5} " +
                $"times[avg:{timeAvg,8:0.000} max:{timeMax,8:0.000} total:{timeTotal,8:0.000}] " +
                $"bytes[avg:{bytesAvg,8} max:{bytesMax,8} total:{bytesTotal,8}]";
            return text;
        }
    }

#if UNITY_EDITOR && SIMPLEWEB_PROFILING_ENABLED
    public class SslBenchmarkWindow : EditorWindow
    {
        void OnGUI()
        {
            foreach (KeyValuePair<string, Measure> kvp in Measure.All.OrderBy(x => x.Key))
            {
                string text = SslBenchmarkRunner.GetOutput(kvp);
                EditorGUILayout.LabelField(text);
            }
        }

        [MenuItem("Window/SSL benchmark", priority = 20002)]
        public static void ShowWindow()
        {
            SslBenchmarkWindow window = GetWindow<SslBenchmarkWindow>();
            window.titleContent = new GUIContent("SSL benchmark");
            window.Show();
        }
    }
#endif
}
