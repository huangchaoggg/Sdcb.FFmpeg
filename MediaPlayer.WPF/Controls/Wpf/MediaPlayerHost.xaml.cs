using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
                //if (player.HasVideo)
                //{
                //    host.VideoImage = new WriteableBitmap((int)player.Width, (int)player.Height, 0, 0, PixelFormats.Rgb24, null);
                //    host.VideoCanvas.Source = host.VideoImage;
                //}
                //else
                //{
                //    player.OpenEvent += (s, ev) =>
                //    {
                //        if (!player.HasVideo) return;
                //        host.VideoImage = new WriteableBitmap((int)player.Width, (int)player.Height, 0, 0, PixelFormats.Rgb24, null);
                //        host.VideoCanvas.Source = host.VideoImage;
                //    };
                //}
                player.ReadFrameEvent += (s, frame) =>
                {
                    if (frame.Width == 0) return;

                    if (host.VideoImage == null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            host.VideoImage = new WriteableBitmap((int)player.Width, (int)player.Height, 96, 96, PixelFormats.Rgb24, null);
                            host.VideoCanvas.Source = host.VideoImage;
                            host.ptr = host.VideoImage.BackBuffer;
                        });
                    }
                    
                    var buffer = frame.GetBitmapBuffer();
                    Marshal.Copy(buffer, 0, host.ptr, buffer.Length);
                    host.VideoImage!.Dispatcher.Invoke(() =>
                    {
                        host.VideoImage.Lock();
                        host.VideoImage!.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
                        host.VideoImage.Unlock();
                    });
                };
            }
        }
    }
}
