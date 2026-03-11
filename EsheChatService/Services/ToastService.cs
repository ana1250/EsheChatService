using System;
using System.Collections.Generic;
using System.Timers;

namespace EsheChatService.Services
{
    public class ToastMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Message { get; set; } = "";
        public string Type { get; set; } = "info"; // info, success, error
    }

    public class ToastService : IDisposable
    {
        public event Action? OnChange;
        private readonly List<ToastMessage> _toasts = new();
        private readonly Dictionary<Guid, System.Timers.Timer> _timers = new();

        public IReadOnlyList<ToastMessage> Toasts => _toasts;

        public void ShowToast(string message, string type = "info")
        {
            var toast = new ToastMessage { Message = message, Type = type };
            _toasts.Add(toast);
            OnChange?.Invoke();

            // Auto-hide after 3.5 seconds using System.Timers.Timer (thread-safe)
            var timer = new System.Timers.Timer(3500);
            timer.AutoReset = false;
            timer.Elapsed += (_, _) => RemoveToast(toast.Id);
            _timers[toast.Id] = timer;
            timer.Start();
        }

        public void RemoveToast(Guid id)
        {
            var toast = _toasts.Find(t => t.Id == id);
            if (toast != null)
            {
                _toasts.Remove(toast);

                if (_timers.TryGetValue(id, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                    _timers.Remove(id);
                }

                OnChange?.Invoke();
            }
        }

        public void Dispose()
        {
            foreach (var timer in _timers.Values)
            {
                timer.Stop();
                timer.Dispose();
            }
            _timers.Clear();
        }
    }
}
