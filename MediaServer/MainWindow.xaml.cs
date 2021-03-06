﻿namespace ReCaD.MediaServer
{
    using System;
    using System.ComponentModel;
    using System.Net;
    using System.Net.Sockets;
    using System.Windows;
    using System.Windows.Media.Imaging;
    using System.Threading;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private class ClientVm
        {
            private Client client;

            public ClientVm(Client client)
            {
                this.client = client;
            }

            public string Name
            {
                get
                {
                    return this.client.Remote.ToString();
                }
            }
        }

        private class MessageVm
        {
            private DateTime time;

            public MessageVm(string text)
            {
                this.Text = text;
                this.time = DateTime.Now;
            }

            public string Time
            {
                get
                {
                    return this.time.ToShortTimeString();
                }
            }

            public string Text
            {
                get; private set;
            }
        }

        private class Vm
        {
            private MainWindow window;
            public Vm(MainWindow wnd)
            {
                this.window = wnd;
                this.Clients = new ObservableCollection<ClientVm>();
                this.Messages = new ObservableCollection<MessageVm>();
            }

            public ObservableCollection<ClientVm> Clients
            {
                get; private set;
            }

            public ObservableCollection<MessageVm> Messages
            {
                get; private set;
            }
        }

        private Vm vm = null;
        private List<Client> clients = null;
        private Thread previewThread = null;
        private TcpListener listener = null;

        private const int TcpClientReceivePort = 54321;

        public MainWindow()
        {
            this.vm = new Vm(this);
            this.DataContext = this.vm;

            this.clients = new List<Client>();

            this.InitializeComponent();
        }

        public void LogMessage(string msg)
        {
            this.vm.Messages.Add(new MessageVm(msg));
        }

        private void SetActiveClient(Client c)
        {
            if (this.previewThread != null)
            {
                this.previewThread.Abort();
            }

            if (c != null)
            {
                this.previewThread = new Thread(this.PreviewHandler);
                this.previewThread.IsBackground = true;
                this.previewThread.Start(c);
            }
        }

        private void AddClient(Client client)
        {
            this.clients.Add(client);
            this.vm.Clients.Add(new ClientVm(client));
        }

        private void PreviewHandler(object args)
        {
            var client = (Client)args;
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    var capture = client.Capture;
                    this.canvas.Source = BitmapSourceFromHandle(capture.CaptureBitmapHandle());
                    GC.Collect();
                }
                catch (Exception ex)
                {
                    client.StopCapturing();
                    MessageBox.Show(ex.Message);
                }
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.StopCapturing();
            this.StopSending();
            base.OnClosing(e);
        }

        private void StopSending()
        {
            foreach (var item in this.clients)
            {
                item.StopSending();
            }
        }

        // http://nahidulkibria.blogspot.co.at/2009/01/setting-image-source-from-code-wpf.html
        private static BitmapSource BitmapSourceFromBitmap(System.Drawing.Bitmap bitmap)
        {
            BitmapSource bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            return bitmapSource;
        }

        private static BitmapImage BitmapImageFromBitmap(System.Drawing.Bitmap bitmap)
        {
            var bi = new BitmapImage();
            using (var ms = new System.IO.MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                ms.Seek(0, System.IO.SeekOrigin.Begin);

                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.EndInit();
            }

            return bi;
        }

        // http://stackoverflow.com/questions/15270844/how-can-i-convert-byte-to-bitmapimage
        /*private BitmapSource BitmapSourceFromCapture()
        {
            var width = this.capture.Width;
            var height = this.capture.Height;
            var dpiX = 96d;
            var dpiY = 96d;
            //var pixelFormat = PixelFormats.Pbgra32;
            var pixelFormat = PixelFormats.Bgra32;
            //var bytesPerPixel = (pixelFormat.BitsPerPixel + 7) / 8;
            //var pixelFormat = PixelFormats.Rgb24;
            var bytesPerPixel = 4;
            var stride = bytesPerPixel * width;

            var buffer = this.captureBuffer;
            var bitmap = BitmapSource.Create(
                width, height, dpiX, dpiY, pixelFormat, null, buffer, stride);
            return bitmap;
        }*/

        private static BitmapImage BitmapImageFromByteArray(byte[] bytes)
        {
            using (var stream = new System.IO.MemoryStream(bytes))
            {
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                BitmapImage image = new BitmapImage();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.BeginInit();
                image.StreamSource = stream;
                image.EndInit();
                return image;
            }
        }

        private static BitmapSource BitmapSourceFromHandle(IntPtr handle)
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }

        private void StopCapturing()
        {
            foreach (var item in this.clients)
            {
                item.StopCapturing();
            }
        }

        private void StopListening()
        {
            if (this.listener != null)
            {
                try
                {
                    this.listener.Stop();
                    this.listener = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
           }

            foreach (var item in this.clients)
            {
                item.Stop();
            }

            this.vm.Clients.Clear();
            this.clients.Clear();
        }

        private void AcceptClients()
        {
            try
            {
                this.listener = new TcpListener(IPAddress.Any, TcpClientReceivePort);
                this.listener.Start();
                this.listener.BeginAcceptTcpClient((IAsyncResult ar)=> 
                {
                    try
                    {
                        var tcpClient = listener.EndAcceptTcpClient(ar);
                        var client = new Client(this, tcpClient);
                        this.AddClient(client);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                }, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            // Start server

            this.StopListening();
            this.AcceptClients();
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            // Stop server

            this.StopListening();
        }

        private void ListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            int i = this.clientList.SelectedIndex;
            if (i >= 0)
            {
                var client = this.clients[i];
                SetActiveClient(client);
            }
            else
            {
                SetActiveClient(null);
            }
        }
    }
}
