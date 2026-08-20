using BluetoothOff.UI;

namespace BluetoothOff;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        const string mutexName = @"Local\BluetoothOff.7D10A32D-1EB1-46B5-A7C8-C6361667C6BD";
        using var singleInstance = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Bluetooth Off is already running in the notification area.",
                "Bluetooth Off",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        _ = System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.Run(new TrayApplicationContext());
    }
}
