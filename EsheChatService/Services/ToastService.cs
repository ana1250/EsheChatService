using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EsheChatService.Services
{
    public class ToastMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Message { get; set; } = "";
        public string Type { get; set; } = "info"; // info, success, error
    }

    public class ToastService
    {
        public event Action? OnChange;
        private readonly List<ToastMessage> _toasts = new();
        public IReadOnlyList<ToastMessage> Toasts => _toasts;

        public void ShowToast(string message, string type = "info")
        {
            var toast = new ToastMessage { Message = message, Type = type };
            _toasts.Add(toast);
            OnChange?.Invoke();

            // Auto-hide after 3 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                RemoveToast(toast.Id);
            });
        }

        public void RemoveToast(Guid id)
        {
            var toast = _toasts.Find(t => t.Id == id);
            if (toast != null)
            {
                _toasts.Remove(toast);
                OnChange?.Invoke();
            }
        }
    }
}
