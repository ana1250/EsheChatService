using EsheChatService.Services;

namespace EsheChatService.Tests;

public class ToastServiceTests
{
    [Fact]
    public void ShowToast_AddsToastToList()
    {
        var service = new ToastService();

        service.ShowToast("Test message", "success");

        Assert.Single(service.Toasts);
        Assert.Equal("Test message", service.Toasts[0].Message);
        Assert.Equal("success", service.Toasts[0].Type);
    }

    [Fact]
    public void ShowToast_FiresOnChangeEvent()
    {
        var service = new ToastService();
        bool eventFired = false;
        service.OnChange += () => eventFired = true;

        service.ShowToast("Hello");

        Assert.True(eventFired);
    }

    [Fact]
    public void ShowToast_DefaultTypeIsInfo()
    {
        var service = new ToastService();

        service.ShowToast("Info message");

        Assert.Equal("info", service.Toasts[0].Type);
    }

    [Fact]
    public void ShowToast_MultipleToasts_AllAdded()
    {
        var service = new ToastService();

        service.ShowToast("First", "info");
        service.ShowToast("Second", "success");
        service.ShowToast("Third", "error");

        Assert.Equal(3, service.Toasts.Count);
    }

    [Fact]
    public void RemoveToast_RemovesCorrectToast()
    {
        var service = new ToastService();
        service.ShowToast("Toast A");
        service.ShowToast("Toast B");
        var idToRemove = service.Toasts[0].Id;

        service.RemoveToast(idToRemove);

        Assert.Single(service.Toasts);
        Assert.Equal("Toast B", service.Toasts[0].Message);
    }

    [Fact]
    public void RemoveToast_InvalidId_DoesNothing()
    {
        var service = new ToastService();
        service.ShowToast("Keep me");

        service.RemoveToast(Guid.NewGuid());

        Assert.Single(service.Toasts);
    }

    [Fact]
    public void RemoveToast_FiresOnChangeEvent()
    {
        var service = new ToastService();
        service.ShowToast("Test");
        var id = service.Toasts[0].Id;

        bool eventFired = false;
        service.OnChange += () => eventFired = true;

        service.RemoveToast(id);

        Assert.True(eventFired);
    }

    [Fact]
    public void ToastMessage_HasUniqueId()
    {
        var service = new ToastService();
        service.ShowToast("A");
        service.ShowToast("B");

        Assert.NotEqual(service.Toasts[0].Id, service.Toasts[1].Id);
    }
}
