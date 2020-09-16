﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    public static class TaskPool
    {
        const int MaxTasks = 5000;

        private struct TaskAction
        {
            public string Name;
            public Task Task;
            public Action<Task, object> OnCompletion;
            public object UserData;
        }

        private static List<TaskAction> taskActions = new List<TaskAction>();

        public static void ListTasks(string[] args)
        {
            lock (taskActions)
            {
                DebugConsole.NewMessage($"Task count: {taskActions.Count}");
                for (int i=0;i<taskActions.Count;i++)
                {
                    DebugConsole.NewMessage($" -{i}: {taskActions[i].Name}, {taskActions[i].Task.Status}");
                }
            }
        }

<<<<<<< HEAD
        public static void Add(Task task, Action<Task> onCompletion)
        {
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke(t); }, null);
        }

        public static void Add<U>(Task task, U userdata, Action<Task, U> onCompletion) where U : class
        {
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke(t, (U)obj); }, userdata);
=======
        private static void AddInternal(string name, Task task, Action<Task, object> onCompletion, object userdata)
        {
            lock (taskActions)
            {
                if (taskActions.Count >= MaxTasks)
                {
                    throw new Exception(
                        "Too many tasks in the TaskPool:\n" + string.Join('\n', taskActions.Select(ta => ta.Name))
                    );
                }
                taskActions.Add(new TaskAction() { Name = name, Task = task, OnCompletion = onCompletion, UserData = userdata });
                DebugConsole.Log($"New task: {name} ({taskActions.Count}/{MaxTasks})");
            }
>>>>>>> upstream/master
        }

        public static void Add(string name, Task task, Action<Task> onCompletion)
        {
<<<<<<< HEAD
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke((Task<T>)t); }, null);
=======
            AddInternal(name, task, (Task t, object obj) => { onCompletion?.Invoke(t); }, null);
>>>>>>> upstream/master
        }

        public static void Add<U>(string name, Task task, U userdata, Action<Task, U> onCompletion) where U : class
        {
<<<<<<< HEAD
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke((Task<T>)t, (U)obj); }, userdata);
=======
            AddInternal(name, task, (Task t, object obj) => { onCompletion?.Invoke(t, (U)obj); }, userdata);
>>>>>>> upstream/master
        }

        public static void Update()
        {
            lock (taskActions)
            {
                for (int i = 0; i < taskActions.Count; i++)
                {
                    if (taskActions[i].Task.IsCompleted)
                    {
                        taskActions[i].OnCompletion?.Invoke(taskActions[i].Task, taskActions[i].UserData);
                        DebugConsole.Log($"Task {taskActions[i].Name} completed ({taskActions.Count-1}/{MaxTasks})");
                        taskActions.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        public static void PrintTaskExceptions(Task task, string msg)
        {
            DebugConsole.ThrowError(msg);
            foreach (Exception e in task.Exception.InnerExceptions)
            {
                DebugConsole.ThrowError(e.Message + "\n" + e.StackTrace);
            }
        }
    }
}
