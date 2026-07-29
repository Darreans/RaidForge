using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace RaidForge.Utils
{
    public static class SharedFileIO
    {
        private const int DefaultAttempts = 5;
        private const int DefaultDelayMilliseconds = 75;
        private static readonly object PendingWritesLock = new object();
        private static readonly Dictionary<string, string> PendingWritesByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Thread _writerThread;
        private static bool _isWriting;

        public static string[] ReadAllLinesShared(string path)
        {
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            Exception lastException = null;

            for (int attempt = 1; attempt <= DefaultAttempts; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                    var lines = new List<string>();
                    while (!reader.EndOfStream)
                    {
                        lines.Add(reader.ReadLine());
                    }

                    return lines.ToArray();
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    Thread.Sleep(DefaultDelayMilliseconds * attempt);
                }
            }

            throw lastException ?? new IOException($"Failed to read '{path}'.");
        }

        public static void WriteAllTextWithRetry(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Exception lastException = null;

            for (int attempt = 1; attempt <= DefaultAttempts; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(content);
                    return;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    Thread.Sleep(DefaultDelayMilliseconds * attempt);
                }
            }

            throw lastException ?? new IOException($"Failed to write '{path}'.");
        }

        public static void QueueWriteAllTextWithRetry(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            lock (PendingWritesLock)
            {
                PendingWritesByPath[path] = content ?? string.Empty;
                EnsureWriterThreadStarted();
                Monitor.PulseAll(PendingWritesLock);
            }
        }

        public static void FlushPendingWrites()
        {
            while (true)
            {
                Dictionary<string, string> batch;

                lock (PendingWritesLock)
                {
                    while (_isWriting)
                    {
                        Monitor.Wait(PendingWritesLock, 50);
                    }

                    if (PendingWritesByPath.Count == 0)
                    {
                        return;
                    }

                    batch = TakePendingWritesForCurrentThread();
                }

                WriteBatch(batch);
            }
        }

        private static void EnsureWriterThreadStarted()
        {
            if (_writerThread != null && _writerThread.IsAlive)
            {
                return;
            }

            _writerThread = new Thread(BackgroundWriterLoop)
            {
                IsBackground = true,
                Name = "RaidForge CSV Writer"
            };
            _writerThread.Start();
        }

        private static void BackgroundWriterLoop()
        {
            while (true)
            {
                Dictionary<string, string> batch;

                lock (PendingWritesLock)
                {
                    while (PendingWritesByPath.Count == 0 || _isWriting)
                    {
                        Monitor.Wait(PendingWritesLock);
                    }

                    batch = TakePendingWritesForCurrentThread();
                }

                WriteBatch(batch);
            }
        }

        private static Dictionary<string, string> TakePendingWritesForCurrentThread()
        {
            var batch = new Dictionary<string, string>(PendingWritesByPath, StringComparer.OrdinalIgnoreCase);
            PendingWritesByPath.Clear();
            _isWriting = true;
            return batch;
        }

        private static void WriteBatch(Dictionary<string, string> batch)
        {
            try
            {
                foreach (var write in batch)
                {
                    try
                    {
                        WriteAllTextWithRetry(write.Key, write.Value);
                    }
                    catch (Exception ex)
                    {
                        LoggingHelper.Error($"[SharedFileIO] CSV write failed for '{write.Key}'.", ex);
                    }
                }
            }
            finally
            {
                lock (PendingWritesLock)
                {
                    _isWriting = false;
                    Monitor.PulseAll(PendingWritesLock);
                }
            }
        }
    }
}
