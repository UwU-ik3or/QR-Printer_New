using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Java.Nio;
using ZXing;
using ZXing.Common;
using ZXing.Mobile;

namespace QR_Printer_New
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            MobileBarcodeScanner.Initialize(Application);

            var btnScan = FindViewById<Button>(Resource.Id.btnScan);
            btnScan.Click += async (sender, e) =>
            {
                var scanner = new MobileBarcodeScanner();
                var result = await scanner.Scan();
                if (result != null)
                    ShowDialog(result.Text);
            };

            var btnScanImage = FindViewById<Button>(Resource.Id.btnScanImage);
            btnScanImage.Click += (sender, e) =>
            {
                var intent = new Intent(Intent.ActionPick);
                intent.SetType("image/*");
                StartActivityForResult(intent, 1);
            };
        }

        protected override async void OnActivityResult(int requestCode, Android.App.Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == 1 && resultCode == Android.App.Result.Ok && data != null)
            {
                Bitmap bitmap = null;
                try
                {
                    // Универсальный способ загрузки Bitmap для всех версий Android
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                    {
                        // Для Android 9.0 и выше
                        using (var source = ContentResolver.OpenInputStream(data.Data))
                        {
                            bitmap = await BitmapFactory.DecodeStreamAsync(source);
                        }
                    }
                    else
                    {
                        // Для версий ниже Android 9.0
                        bitmap = MediaStore.Images.Media.GetBitmap(ContentResolver, data.Data);
                    }

                    if (bitmap == null)
                    {
                        Toast.MakeText(this, "Не удалось загрузить изображение", ToastLength.Short).Show();
                        return;
                    }

                    // Декодирование QR-кода
                    var reader = new BarcodeReader
                    {
                        Options = new DecodingOptions
                        {
                            TryHarder = true,
                            PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                        }
                    };

                    // Конвертация Bitmap для ZXing
                    var rgbBytes = GetRGBBytes(bitmap);
                    var luminanceSource = new RGBLuminanceSource(rgbBytes, bitmap.Width, bitmap.Height);
                    var result = reader.Decode(luminanceSource);

                    if (result != null)
                        ShowDialog(result.Text);
                    else
                        Toast.MakeText(this, "QR-код не найден", ToastLength.Long).Show();
                }
                catch (Exception ex)
                {
                    Toast.MakeText(this, $"Ошибка: {ex.Message}", ToastLength.Long).Show();
                }
                finally
                {
                    bitmap?.Recycle();
                }
            }
        }

        private byte[] GetRGBBytes(Bitmap bitmap)
        {
            int[] pixels = new int[bitmap.Width * bitmap.Height];
            bitmap.GetPixels(pixels, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
            byte[] rgbBytes = new byte[pixels.Length * 3];

            for (int i = 0; i < pixels.Length; i++)
            {
                rgbBytes[i * 3] = (byte)((pixels[i] >> 16) & 0xFF); // Red
                rgbBytes[i * 3 + 1] = (byte)((pixels[i] >> 8) & 0xFF); // Green
                rgbBytes[i * 3 + 2] = (byte)(pixels[i] & 0xFF); // Blue
            }

            return rgbBytes;
        }

        private void ShowDialog(string message)
        {
            new AlertDialog.Builder(this)
                .SetTitle("Результат сканирования")
                .SetMessage(message)
                .SetPositiveButton("Печать", (s, e) =>
                {
                    Toast.MakeText(this, $"Печать: {message}", ToastLength.Short).Show();
                })
                .SetNegativeButton("Продолжить", (s, e) => { })
                .Show();
        }
    }
}