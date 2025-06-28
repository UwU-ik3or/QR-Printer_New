using Android.Bluetooth;
using Android.Content;
using Android.Text;
using Android.Views;

namespace QR_Printer
{
    [Activity(Label = "Принтер")]
    public class PrinterActivity : Activity
    {

        private const string TAG = "CatPrinter"; // Подключения к принтеру через имя, пока четные, но пусть будет виесть как напоминание
        private const string PRINTER_MAC_ADDRESS = "48:0F:57:2B:65:0E"; // Подключение по MAC-адресу, пока хватает с головой

        private CatPrinterService CatPrinterServiceToConnectAndPrint;


        private BluetoothAdapter AndroidBluetoothAdapter;
        private EditText ScanResultText;
        private Button PrintScanResultTextButton;
        private Button ConnectToPrinterButton;
        private Button DisconnectFromPrinterButton;
        private TextView PrinterConnectionStatus;
        private ProgressBar ConnectionStatusBar;

        protected override void OnCreate(Bundle savedInstanceState) {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_printer);

            Window.SetBackgroundDrawableResource(Resource.Drawable.bg_image);

            InitializeComponents();
            SetupBluetooth();
            SetupEventHandlers();

            string qrContent = Intent.GetStringExtra("qr_content");
            if (!string.IsNullOrEmpty(qrContent)) { ScanResultText.Text = qrContent; }

            ScanResultText.SetFilters(new IInputFilter[] { new InputFilterLengthFilter(17) });
        }

        private void InitializeComponents() {
            ScanResultText = FindViewById<EditText>(Resource.Id.ScanResultText);
            PrintScanResultTextButton = FindViewById<Button>(Resource.Id.PrintScanResultTextButton);
            ConnectToPrinterButton = FindViewById<Button>(Resource.Id.ConnectToPrinterButton);
            DisconnectFromPrinterButton = FindViewById<Button>(Resource.Id.DisconnectFromPrinterButton);
            PrinterConnectionStatus = FindViewById<TextView>(Resource.Id.PrinterConnectionStatus);
            ConnectionStatusBar = FindViewById<ProgressBar>(Resource.Id.ConnectionStatusBar);
        }

        private void SetupBluetooth() {
            AndroidBluetoothAdapter = BluetoothAdapter.DefaultAdapter;
            if (AndroidBluetoothAdapter == null) {
                ShowError("Bluetooth не поддерживается");
                Finish();
                return;
            }

            CatPrinterServiceToConnectAndPrint = new CatPrinterService(this);
            CatPrinterServiceToConnectAndPrint.StatusChanged += (s, msg) => RunOnUiThread(() => PrinterConnectionStatus.Text = msg);
            CatPrinterServiceToConnectAndPrint.ErrorOccurred += (s, err) => RunOnUiThread(() => ShowError(err));
            CatPrinterServiceToConnectAndPrint.PrintCompleted += (s, msg) => RunOnUiThread(() => {
                Toast.MakeText(this, msg, ToastLength.Long).Show();
                ShowProgress(false);
            });
        }

        private void SetupEventHandlers() {
            ConnectToPrinterButton.Click += async (s, e) => await ConnectToPrinter();
            DisconnectFromPrinterButton.Click += async (s, e) => await CatPrinterServiceToConnectAndPrint.DisconnectAsync();
            PrintScanResultTextButton.Click += (s, e) => PrintText();

            UpdateUIState(false);
        }

        private async Task ConnectToPrinter() {
            try {
                ShowProgress(true);

                if (!AndroidBluetoothAdapter.IsEnabled) {
                    ShowError("Включите Bluetooth");
                    return;
                }

                var device = AndroidBluetoothAdapter.GetRemoteDevice(PRINTER_MAC_ADDRESS);
                if (device == null) {
                    ShowError("Принтер не найден");
                    return;
                }

                await CatPrinterServiceToConnectAndPrint.ConnectAsync(device.Address);

                if (CatPrinterServiceToConnectAndPrint.PrinterIsConnected) {
                    UpdateUIState(true);
                    Toast.MakeText(this, "Принтер подключен", ToastLength.Short).Show();
                }
            }
            catch (Exception ex) { ShowError($"Ошибка подключения: {ex.Message}"); }
            finally { ShowProgress(false); }
        }

        private void PrintText() {
            if (string.IsNullOrWhiteSpace(ScanResultText.Text)) {
                Toast.MakeText(this, "Введите текст для печати", ToastLength.Short).Show();
                return;
            }

            Task.Run(async () => {
                try {
                    RunOnUiThread(() => ShowProgress(true));
                    await CatPrinterServiceToConnectAndPrint.PrintTextAsync(
                        text: ScanResultText.Text,
                        fontName: "Times New Roman", // База)
                        fontSize: 36, // Не база
                        alignment: "center",
                        upsideDown: false);
                }
                catch (Exception ex) { RunOnUiThread(() => ShowError($"Ошибка печати: {ex.Message}")); }
                finally { RunOnUiThread(() => ShowProgress(false)); }
            });
        }

        private void UpdateUIState(bool isConnected) {
            ConnectToPrinterButton.Enabled = !isConnected;
            DisconnectFromPrinterButton.Enabled = isConnected;
            PrintScanResultTextButton.Enabled = isConnected;
            ScanResultText.Enabled = isConnected;
        }

        private void ShowProgress(bool show) { ConnectionStatusBar.Visibility = show ? ViewStates.Visible : ViewStates.Invisible; }

        private void ShowError(string message) {
            PrinterConnectionStatus.Text = $"Ошибка: {message}";
            Toast.MakeText(this, message, ToastLength.Long).Show();
            ShowProgress(false);
        }

        protected override void OnDestroy() {
            CatPrinterServiceToConnectAndPrint?.Dispose();
            base.OnDestroy();
        }
    }
}