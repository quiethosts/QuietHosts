using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace QuietHosts
{
    /// <summary>
    /// One window per Windows session: the first launch owns the "Local\QuietHosts.Window" mutex and
    /// publishes its process id; a second launch lets that process take the foreground
    /// (AllowSetForegroundWindow), signals a named event and exits. Elevated runs never get here.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly EventWaitHandle? _show;
        private readonly MemoryMappedFile? _pid;
        private RegisteredWaitHandle? _wait;

        private SingleInstance(Mutex mutex, EventWaitHandle? show, MemoryMappedFile? pid)
        {
            _mutex = mutex;
            _show = show;
            _pid = pid;
        }

        /// <summary>Becomes the first instance, or returns null after asking the first one to come forward.</summary>
        public static SingleInstance? Acquire(string baseName)
        {
            Mutex mutex;
            bool createdNew;
            try
            {
                mutex = new Mutex(true, baseName, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // The open window runs as administrator: its mutex exists but this normal process can't open it.
                SignalFirst(baseName);
                return null;
            }
            if (!createdNew)
            {
                mutex.Dispose();
                SignalFirst(baseName);
                return null;
            }
            EventWaitHandle? show = null;
            try
            {
                show = new EventWaitHandle(false, EventResetMode.AutoReset, baseName + ".Show");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is WaitHandleCannotBeOpenedException)
            {
                // Without the event a second launch can't bring this window forward; everything else works.
            }
            MemoryMappedFile? pid = null;
            try
            {
                pid = MemoryMappedFile.CreateNew(baseName + ".Pid", 8, MemoryMappedFileAccess.ReadWrite);
                using (MemoryMappedViewAccessor view = pid.CreateViewAccessor(0, 8))
                {
                    using (Process self = Process.GetCurrentProcess()) view.Write(0, (long)self.Id);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                pid?.Dispose();
                pid = null;
            }
            return new SingleInstance(mutex, show, pid);
        }

        /// <summary>Calls <paramref name="onShow"/> (on a worker thread) each time a second launch asks.</summary>
        public void Listen(Action onShow)
        {
            if (_show != null) _wait = ThreadPool.RegisterWaitForSingleObject(_show, (state, timedOut) => onShow(), null, Timeout.Infinite, false);
        }

        private static void SignalFirst(string baseName)
        {
            try
            {
                using (MemoryMappedFile pid = MemoryMappedFile.OpenExisting(baseName + ".Pid", MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor view = pid.CreateViewAccessor(0, 8, MemoryMappedFileAccess.Read))
                {
                    long id = view.ReadInt64(0);
                    if (id > 0 && id <= int.MaxValue) NativeUi.AllowSetForegroundWindow((int)id);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                // The first window still comes forward; Windows may only flash its taskbar button.
            }
            try
            {
                if (EventWaitHandle.TryOpenExisting(baseName + ".Show", out EventWaitHandle? show))
                {
                    using (show) show.Set();
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public void Dispose()
        {
            _wait?.Unregister(null);
            _show?.Dispose();
            _pid?.Dispose();
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
