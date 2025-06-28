using Android.Bluetooth;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;


namespace QR_Printer
{
    // Код основан на работах:
    // MaikelChan - Cat Printer BLE (С#)
    // PinThePenguinOne - MXW01_Thermal-Printer-Tool (Phyton)

    public class CatPrinterService : IDisposable {
        private const string PRINTER_SERVICE_UUID = "0000ae30-0000-1000-8000-00805f9b34fb";
        private const string PRINTER_WRITE_CHAR_UUID = "0000ae01-0000-1000-8000-00805f9b34fb";
        private const string PRINTER_NOTIFY_CHAR_UUID = "0000ae02-0000-1000-8000-00805f9b34fb";
        private const string PRINTER_DATA_CHAR_UUID = "0000ae03-0000-1000-8000-00805f9b34fb";

        private const int PRINTER_WIDTH = 384; // Макс. ширина
        private const int CONNECT_TIMEOUT = 15000; // ~15 секунд до активации Connection Timeout. Вроде хвататет на подключение

        private readonly Context AndroidContextInformation;
        private readonly object SynchronizeLocker = new object();
        private BluetoothGatt BluetoothGattConnection;
        private BluetoothGattCharacteristic BluetoothGattWriteCharacteristic;
        private BluetoothGattCharacteristic BluetoothGattDataCharacteristic;
        private bool isPrinterConnected = false;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<string> PrintCompleted;

        public bool PrinterIsConnected {
            get { lock (SynchronizeLocker) { return isPrinterConnected && BluetoothGattConnection != null; } }
        }

        public CatPrinterService(Context context) { AndroidContextInformation = context ?? throw new ArgumentNullException(nameof(context)); }

        #region "Connect and Print"
        public async Task ConnectAsync(string deviceAddress) {
            if (string.IsNullOrEmpty(deviceAddress))
                throw new ArgumentNullException(nameof(deviceAddress));

            lock (SynchronizeLocker) {
                if (PrinterIsConnected) return;
                DisconnectInternal();
            }

            try {
                var adapter = BluetoothAdapter.DefaultAdapter;
                if (adapter == null || !adapter.IsEnabled)
                    throw new InvalidOperationException("Bluetooth не доступен");

                var device = adapter.GetRemoteDevice(deviceAddress);
                if (device == null)
                    throw new InvalidOperationException("Устройство не найдено");

                using (var cts = new CancellationTokenSource(CONNECT_TIMEOUT))
                {
                    var callback = new GattCallback(this);

                    BluetoothGattConnection = Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop
                        ? device.ConnectGatt(AndroidContextInformation, false, callback, BluetoothTransports.Le)
                        : device.ConnectGatt(AndroidContextInformation, false, callback);

                    await WaitForConnectionAsync(cts.Token);
                }
            }
            catch (Exception ex) {
                DisconnectInternal();
                throw new InvalidOperationException("Ошибка подключения", ex);
            }
        }

        private async Task WaitForConnectionAsync(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                lock (SynchronizeLocker) { if (isPrinterConnected) break; }
                await Task.Delay(100, ct);
            }

            if (!PrinterIsConnected)
                throw new TimeoutException("Таймаут подключения");
        }

        public async Task PrintTextAsync(string text, string fontName, int fontSize, string alignment, bool upsideDown)
        {
            if (!PrinterIsConnected)
                throw new InvalidOperationException("Принтер не подключен");

            using (var bmp = CreateTextBitmap(text, fontName, fontSize, alignment, upsideDown)) {
                var bwImage = ConvertToBlackAndWhite(bmp);

                var pixels = GetPixels(bwImage);

                var bytes = PackPixels(pixels, bwImage.Width, bwImage.Height);

                await SendPrintCommand(bytes, bwImage.Height);
            }
        }
        #endregion

        #region "Create Image"
        private Bitmap CreateTextBitmap(string text, string fontName, int fontSize, string alignment, bool upsideDown) {
            var typeface = Typeface.Create(fontName, TypefaceStyle.Normal) ?? Typeface.Default;
            var paint = new Paint
            {
                Color = Color.Black,
                TextSize = fontSize,
                AntiAlias = true,
                TextAlign = alignment switch {
                    "center" => Paint.Align.Center,
                    "right" => Paint.Align.Right,   // - Пока не нашел адекватного применения
                    _ => Paint.Align.Left   //- Пока не нашел адекватного применения
                }
            };
            paint.SetTypeface(typeface);

            var bounds = new Rect();
            paint.GetTextBounds(text, 0, text.Length, bounds);

            int width = Math.Max(PRINTER_WIDTH, bounds.Width() + 40);
            int height = bounds.Height() + 40;

            var bmp = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888);

            using (var canvas = new Canvas(bmp)) {
                canvas.DrawColor(Color.White);
                float x = alignment switch {
                    "center" => width / 2f,
                    "right" => width - 20,
                    _ => 20
                };
                canvas.DrawText(text, x, height - 20, paint);
            }

            if (upsideDown) {
                var matrix = new Matrix();
                matrix.PostRotate(180, bmp.Width / 2f, bmp.Height / 2f);
                var rotated = Bitmap.CreateBitmap(bmp, 0, 0, bmp.Width, bmp.Height, matrix, true);
                bmp.Recycle();
                bmp = rotated; // GET ROTATED, idiot
            }
            return bmp;
        }

        private Bitmap ConvertToBlackAndWhite(Bitmap original) {
            var bw = Bitmap.CreateBitmap(original.Width, original.Height, Bitmap.Config.Argb8888); // Argb8888 говорит о том, что каждый пиксель будет хранится в 4-ех байтах, по стандарту в 1-ом
            using (var canvas = new Canvas(bw)) {
                var cm = new ColorMatrix();
                cm.SetSaturation(0);

                var paint = new Paint();
                paint.SetColorFilter(new ColorMatrixColorFilter(cm));
                canvas.DrawBitmap(original, 0, 0, paint);

                var pixels = new int[original.Width * original.Height];
                bw.GetPixels(pixels, 0, original.Width, 0, 0, original.Width, original.Height);

                for (int i = 0; i < pixels.Length; i++) {
                    int r = (pixels[i] >> 16) & 0xFF;
                    pixels[i] = r < 128 ? Color.Black : Color.White;
                }

                bw.SetPixels(pixels, 0, original.Width, 0, 0, original.Width, original.Height);
            }
            return bw;
        }

        private int[] GetPixels(Bitmap bitmap) {
            var pixels = new int[bitmap.Width * bitmap.Height];
            bitmap.GetPixels(pixels, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
            return pixels;
        }

        private byte[] PackPixels(int[] pixels, int width, int height) {
            int bytesPerRow = (width + 7) / 8;
            var result = new byte[bytesPerRow * height];

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int pixel = pixels[y * width + x];
                    bool isBlack = Color.GetRedComponent(pixel) < 128;

                    if (isBlack) {
                        int byteIndex = y * bytesPerRow + (x / 8);
                        int bitIndex = x % 8;
                        result[byteIndex] |= (byte)(1 << bitIndex);
                    }
                }
            }
            return result;
        }
        #endregion

        #region "Command Send To Printer"
        private async Task SendPrintCommand(byte[] data, int height) { // Тут идет правильный порядок команд по PROTOCOL.md [droppaltabels]
            try {
                await SendCommand(0xA2, new byte[] { 0x5D }); // - **`AE02`:** Notify characteristic (`0000ae02-0000-1000-8000-00805f9b34fb`) - Подписка на уведомления. 05XD - Интенсивность, ~90%

                var heightBytes = BitConverter.GetBytes((short)height);
                await SendCommand(0xA9, new byte[] { heightBytes[0], heightBytes[1], 0x30, 0x00 }); // A9 - Print Response. Ожидаем ответа о готовности принтера

                const int chunkSize = 20;
                for (int i = 0; i < data.Length; i += chunkSize) {
                    var chunk = new byte[Math.Min(chunkSize, data.Length - i)];
                    Array.Copy(data, i, chunk, 0, chunk.Length);
                    BluetoothGattDataCharacteristic.SetValue(chunk);
                    BluetoothGattConnection.WriteCharacteristic(BluetoothGattDataCharacteristic);
                    await Task.Delay(10);
                }

                await SendCommand(0xAD, new byte[] { 0x00 }); // 00 - Команда завершения
                PrintCompleted?.Invoke(this, "Печать завершена");
            }
            catch (Exception ex) {
                ErrorOccurred?.Invoke(this, $"Ошибка печати: {ex.Message}");
                throw;
            }
        }

        private async Task SendCommand(byte cmd, byte[] data) {
            var packet = new byte[8 + data.Length];
            packet[0] = 0x22; // Подготовка к работе (Преамбула)
            packet[1] = 0x21;
            packet[2] = cmd;
            packet[3] = 0x00;
            BitConverter.GetBytes((short)data.Length).CopyTo(packet, 4);
            data.CopyTo(packet, 6);
            packet[6 + data.Length] = CalculateCrc(data);
            packet[7 + data.Length] = 0xFF;

            BluetoothGattWriteCharacteristic.SetValue(packet);
            BluetoothGattConnection.WriteCharacteristic(BluetoothGattWriteCharacteristic);
            await Task.Delay(50);
        }


        // Подсчет Crc8 (Циклический избыточный код)
        private byte CalculateCrc(byte[] data) {
            byte crc = 0;
            foreach (var b in data) {
                crc ^= b;
                for (int i = 0; i < 8; i++) {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
        #endregion

        #region "Disconnect"
        public async Task DisconnectAsync() {
            await Task.Run(() => {
                lock (SynchronizeLocker) { DisconnectInternal(); }
            });
        }

        private void DisconnectInternal() {
            try {
                BluetoothGattConnection?.Disconnect();
                BluetoothGattConnection?.Close();
                BluetoothGattConnection?.Dispose();
            }
            finally {
                BluetoothGattConnection = null;
                isPrinterConnected = false;
                BluetoothGattWriteCharacteristic = null;
                BluetoothGattDataCharacteristic = null;
            }
        }

        public void Dispose() { DisconnectInternal(); }

        private class GattCallback : BluetoothGattCallback {
            private readonly CatPrinterService _service;

            public GattCallback(CatPrinterService service) { _service = service; }

            public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState) {
                lock (_service.SynchronizeLocker) {
                    _service.BluetoothGattConnection = gatt;

                    if (newState == ProfileState.Connected) {
                        _service.isPrinterConnected = true;
                        _service.StatusChanged?.Invoke(_service, "Подключено, обнаружение сервисов...");
                        gatt.DiscoverServices();
                    }
                    else {
                        _service.isPrinterConnected = false;
                        _service.ErrorOccurred?.Invoke(_service, $"Отключено: {status}");
                        _service.DisconnectInternal();
                    }
                }
            }
            #endregion

            public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status) {
                base.OnServicesDiscovered(gatt, status);

                lock (_service.SynchronizeLocker) {
                    if (status == GattStatus.Success) {
                        var service = gatt.GetService(Java.Util.UUID.FromString(PRINTER_SERVICE_UUID));
                        if (service != null) {

                            _service.BluetoothGattWriteCharacteristic = service.GetCharacteristic(Java.Util.UUID.FromString(PRINTER_WRITE_CHAR_UUID));
                            _service.BluetoothGattDataCharacteristic = service.GetCharacteristic(Java.Util.UUID.FromString(PRINTER_DATA_CHAR_UUID));

                            var notifyChar = service.GetCharacteristic(Java.Util.UUID.FromString(PRINTER_NOTIFY_CHAR_UUID)); // Подписка на уведомления?
                            if (notifyChar != null) {
                                gatt.SetCharacteristicNotification(notifyChar, true);
                                var descriptor = notifyChar.GetDescriptor(
                                    Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"));
                                descriptor?.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                                gatt.WriteDescriptor(descriptor);
                            }
                            _service.StatusChanged?.Invoke(_service, "Принтер готов к работе"); // Хороший знак
                            return;
                        }
                    } 
                    _service.ErrorOccurred?.Invoke(_service, "Ошибка обнаружения сервисов"); // Плохой знак
                    _service.DisconnectInternal();
                }
            }
        }
    }
}