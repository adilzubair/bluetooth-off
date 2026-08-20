using BluetoothOff.Domain;

namespace BluetoothOff.Services;

internal sealed class BluetoothRadioController : IBluetoothRadioController, IDisposable
{
    private static readonly TimeSpan DefaultConfirmationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IRadioPlatform _platform;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _confirmationTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    internal BluetoothRadioController(
        IRadioPlatform platform,
        TimeProvider? timeProvider = null,
        TimeSpan? confirmationTimeout = null,
        TimeSpan? pollInterval = null)
    {
        _platform = platform;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _confirmationTimeout = confirmationTimeout ?? DefaultConfirmationTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;

        if (_confirmationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationTimeout));
        }

        if (_pollInterval <= TimeSpan.Zero || _pollInterval > _confirmationTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }

    public bool IsAuthorized { get; private set; }

    public async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            var decision = await _platform.RequestAccessAsync(cancellationToken).ConfigureAwait(true);
            IsAuthorized = decision == RadioAccessDecision.Allowed;

            if (!IsAuthorized)
            {
                throw new BluetoothControlException(
                    BluetoothFailureCode.PermissionDenied,
                    "Windows did not grant permission to control Bluetooth.");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<BluetoothStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var radio = await GetRadioAsync(cancellationToken).ConfigureAwait(false);
        return new BluetoothStatus(radio.State, _timeProvider.GetUtcNow());
    }

    public async Task<BluetoothOffResult> TurnOffAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsAuthorized)
        {
            throw new BluetoothControlException(
                BluetoothFailureCode.PermissionDenied,
                "Bluetooth control has not been authorized in the tray application.");
        }

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var radio = await GetRadioAsync(cancellationToken).ConfigureAwait(false);

            if (radio.State == BluetoothState.Off)
            {
                return new BluetoothOffResult(BluetoothState.Off, false, _timeProvider.GetUtcNow());
            }

            if (radio.State == BluetoothState.Disabled)
            {
                throw new BluetoothControlException(
                    BluetoothFailureCode.RadioDisabled,
                    "The Bluetooth radio is disabled by hardware or system policy.");
            }

            var decision = await radio.SetOffAsync(cancellationToken).ConfigureAwait(false);
            if (decision != RadioSetDecision.Allowed)
            {
                throw new BluetoothControlException(
                    BluetoothFailureCode.PolicyRestricted,
                    "Windows policy rejected the Bluetooth state change.");
            }

            var deadline = _timeProvider.GetUtcNow() + _confirmationTimeout;
            while (_timeProvider.GetUtcNow() < deadline)
            {
                if (radio.State == BluetoothState.Off)
                {
                    return new BluetoothOffResult(BluetoothState.Off, true, _timeProvider.GetUtcNow());
                }

                await Task.Delay(_pollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            throw new BluetoothControlException(
                BluetoothFailureCode.StateNotConfirmed,
                "Windows accepted the request but the Bluetooth radio did not report an off state in time.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _operationLock.Dispose();
        _disposed = true;
    }

    private async Task<IRadioDevice> GetRadioAsync(CancellationToken cancellationToken)
    {
        var radio = await _platform.FindBluetoothRadioAsync(cancellationToken).ConfigureAwait(false);
        return radio ?? throw new BluetoothControlException(
            BluetoothFailureCode.RadioUnavailable,
            "No Bluetooth radio was found.");
    }
}

