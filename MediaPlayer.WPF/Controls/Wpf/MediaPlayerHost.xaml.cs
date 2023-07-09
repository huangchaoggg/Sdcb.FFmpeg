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
        int width=1366, height=768;
        public MediaPlayerHost()
        {
            InitializeComponent();
            //VideoImage = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            //VideoCanvas.Source = VideoImage;
        }
        public static readonly DependencyProperty PlayerProperty=DependencyProperty.Register(nameof(Player),typeof(Player),typeof(MediaPlayerHost),
            new PropertyMetadata(null, PlayerCallBackBind));
        public Player? Player { get => GetValue(PlayerProperty) as Player; set => SetValue(PlayerProperty, value); }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
        }
        public WriteableBitmap VideoImage { get; private set; }
        //private Graphics graphics;
        private static void PlayerCallBackBind(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if(d is MediaPlayerHost host&&e.NewValue is Player player)
            {
                player.OpenEvent += (s, ev) =>
                {
                    host.VideoImage = new WriteableBitmap((int)player.Width, (int)player.Height, 0, 0, PixelFormats.Rgb24, null);
                    host.VideoCanvas.Source = host.VideoImage;
                };
                player.ReadFrameEvent += (s, frame) =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if(frame.Width>0)
                            host.VideoImage.WritePixels(new Int32Rect(0, 0, (int)player.Width, (int)player.Height),frame.GetBitmapBuffer(), frame.Width * 3,0,0);
                    });
                };
            }
        }
    }
}
