using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using MediaPlayer.Extension;
using MediaPlayer.MediaFramework;

namespace MediaPlayer.Controls.Wpf
{
    /// <summary>
    /// MediaPlayerHost.xaml 的交互逻辑
    /// </summary>
    public partial class MediaPlayerHost : UserControl
    {
        public MediaPlayerHost()
        {
            InitializeComponent();
        }
        public static readonly DependencyProperty PlayerProperty=DependencyProperty.Register(nameof(Player),typeof(Player),typeof(MediaPlayerHost),
            new PropertyMetadata(null, PlayerCallBackBind));
        public Player? Player { get => GetValue(PlayerProperty) as Player; set => SetValue(PlayerProperty, value); }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
        }
        public WriteableBitmap? VideoImage { get; private set; }
        private IntPtr ptr;
        //private Graphics graphics;
        private static void PlayerCallBackBind(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MediaPlayerHost host && e.NewValue is Player player)
            {
                player.OpenEvent += (s, e) =>
                {
                    if (!player.HasVideo) return;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        host.VideoImage = new WriteableBitmap((int)player.Width, (int)player.Height, 96, 96, PixelFormats.Rgba64, null);
                        host.VideoCanvas.Source = host.VideoImage;
                        host.ptr = host.VideoImage.BackBuffer;
                    });
                };
                player.ReadFrameEvent += (s, frame) =>
                {
                    if (frame.Width == 0) return;
                    var buffer = player.GetBitmapBuffer(frame);
                    Marshal.Copy(buffer, 0, host.ptr, buffer.Length);
                    host.VideoImage?.Dispatcher.Invoke(() =>
                    {
                        host.VideoImage?.Lock();
                        host.VideoImage?.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
                        host.VideoImage?.Unlock();
                    });
                };
            }
        }
    }
}
