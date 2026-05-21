// MetroTrilithon.Threading.Tasks の内製化 (Phase 1)
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace MetroTrilithon.Threading.Tasks
{
    public class TaskLog
    {
        public string CallerMemberName { get; }
        public string CallerFilePath { get; }
        public int CallerLineNumber { get; }
        public Exception Exception { get; }

        public TaskLog(string callerMemberName, string callerFilePath, int callerLineNumber, Exception exception)
        {
            this.CallerMemberName = callerMemberName;
            this.CallerFilePath = callerFilePath;
            this.CallerLineNumber = callerLineNumber;
            this.Exception = exception;
        }

        public static EventHandler<TaskLog> Occured = (sender, e) =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"Unhandled Exception in Task.Forget() [{e.CallerMemberName}] {e.Exception}");
        };

        internal static void Raise(TaskLog log) => Occured?.Invoke(typeof(TaskLog), log);
    }

    public static class TaskExtensions
    {
        public static void Forget(
            this Task task,
            [CallerMemberName] string callerMemberName = "",
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            task.ContinueWith(
                x => TaskLog.Raise(new TaskLog(callerMemberName, callerFilePath, callerLineNumber, x.Exception)),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
