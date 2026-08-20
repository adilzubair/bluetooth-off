using BluetoothOff.Domain;
using BluetoothOff.Services;

namespace BluetoothOff.Tests;

[TestClass]
public sealed class BluetoothRadioControllerTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ShortPoll = TimeSpan.FromMilliseconds(2);

    [TestMethod]
    public async Task AuthorizeAsyncAllowsAccess()
    {
        var platform = new FakeRadioPlatform { AccessDecision = RadioAccessDecision.Allowed };
        using var controller = CreateController(platform);

        await controller.AuthorizeAsync(CancellationToken.None);

        Assert.IsTrue(controller.IsAuthorized);
    }

    [TestMethod]
    public async Task AuthorizeAsyncRejectsDeniedAccess()
    {
        var platform = new FakeRadioPlatform { AccessDecision = RadioAccessDecision.Denied };
        using var controller = CreateController(platform);

        var exception = await Assert.ThrowsExactlyAsync<BluetoothControlException>(
            () => controller.AuthorizeAsync(CancellationToken.None));

        Assert.AreEqual(BluetoothFailureCode.PermissionDenied, exception.Code);
        Assert.IsFalse(controller.IsAuthorized);
    }

    [TestMethod]
    public async Task GetStatusAsyncRejectsMissingRadio()
    {
        var platform = new FakeRadioPlatform { Radio = null };
        using var controller = CreateController(platform);

        var exception = await Assert.ThrowsExactlyAsync<BluetoothControlException>(
            () => controller.GetStatusAsync(CancellationToken.None));

        Assert.AreEqual(BluetoothFailureCode.RadioUnavailable, exception.Code);
    }

    [TestMethod]
    public async Task TurnOffAsyncIsIdempotentWhenAlreadyOff()
    {
        var device = new FakeRadioDevice { State = BluetoothState.Off };
        var platform = new FakeRadioPlatform { Radio = device };
        using var controller = CreateController(platform);
        await controller.AuthorizeAsync(CancellationToken.None);

        var result = await controller.TurnOffAsync(CancellationToken.None);

        Assert.AreEqual(BluetoothState.Off, result.State);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(0, device.SetOffCallCount);
    }

    [TestMethod]
    public async Task TurnOffAsyncConfirmsOffState()
    {
        var device = new FakeRadioDevice
        {
            State = BluetoothState.On,
            OnSetOff = static radio => radio.State = BluetoothState.Off,
        };
        var platform = new FakeRadioPlatform { Radio = device };
        using var controller = CreateController(platform);
        await controller.AuthorizeAsync(CancellationToken.None);

        var result = await controller.TurnOffAsync(CancellationToken.None);

        Assert.AreEqual(BluetoothState.Off, result.State);
        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, device.SetOffCallCount);
    }

    [TestMethod]
    public async Task TurnOffAsyncRejectsDisabledHardware()
    {
        var platform = new FakeRadioPlatform
        {
            Radio = new FakeRadioDevice { State = BluetoothState.Disabled },
        };
        using var controller = CreateController(platform);
        await controller.AuthorizeAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<BluetoothControlException>(
            () => controller.TurnOffAsync(CancellationToken.None));

        Assert.AreEqual(BluetoothFailureCode.RadioDisabled, exception.Code);
    }

    [TestMethod]
    public async Task TurnOffAsyncRejectsUnconfirmedState()
    {
        var platform = new FakeRadioPlatform
        {
            Radio = new FakeRadioDevice { State = BluetoothState.On },
        };
        using var controller = CreateController(platform);
        await controller.AuthorizeAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<BluetoothControlException>(
            () => controller.TurnOffAsync(CancellationToken.None));

        Assert.AreEqual(BluetoothFailureCode.StateNotConfirmed, exception.Code);
    }

    [TestMethod]
    public async Task ConcurrentOffRequestsAreSerializedAndCoalesced()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new FakeRadioDevice
        {
            State = BluetoothState.On,
            OnSetOffAsync = async radio =>
            {
                entered.SetResult();
                await release.Task;
                radio.State = BluetoothState.Off;
            },
        };
        var platform = new FakeRadioPlatform { Radio = device };
        using var controller = CreateController(platform);
        await controller.AuthorizeAsync(CancellationToken.None);

        var first = controller.TurnOffAsync(CancellationToken.None);
        await entered.Task;
        var second = controller.TurnOffAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(1, device.SetOffCallCount);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.IsTrue(results[0].Changed);
        Assert.IsFalse(results[1].Changed);
        Assert.AreEqual(1, device.SetOffCallCount);
    }

    private static BluetoothRadioController CreateController(FakeRadioPlatform platform)
    {
        return new BluetoothRadioController(
            platform,
            confirmationTimeout: ShortTimeout,
            pollInterval: ShortPoll);
    }

    private sealed class FakeRadioPlatform : IRadioPlatform
    {
        internal RadioAccessDecision AccessDecision { get; init; } = RadioAccessDecision.Allowed;

        internal IRadioDevice? Radio { get; init; } = new FakeRadioDevice();

        public Task<RadioAccessDecision> RequestAccessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AccessDecision);
        }

        public Task<IRadioDevice?> FindBluetoothRadioAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Radio);
        }
    }

    private sealed class FakeRadioDevice : IRadioDevice
    {
        internal Action<FakeRadioDevice>? OnSetOff { get; init; }

        internal Func<FakeRadioDevice, Task>? OnSetOffAsync { get; init; }

        internal int SetOffCallCount { get; private set; }

        public BluetoothState State { get; set; } = BluetoothState.On;

        public async Task<RadioSetDecision> SetOffAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetOffCallCount++;
            OnSetOff?.Invoke(this);
            if (OnSetOffAsync is not null)
            {
                await OnSetOffAsync(this);
            }

            return RadioSetDecision.Allowed;
        }
    }
}
