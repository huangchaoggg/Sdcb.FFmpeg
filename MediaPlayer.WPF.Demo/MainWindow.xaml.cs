using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using MediaPlayer.MediaFramework;

using Microsoft.Win32;

namespace MediaPlayer.WPF.Demo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Player Player { get; init; } = new Player();
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if(openFileDialog.ShowDialog()==true )
            {
                await Player.OpenAsync(openFileDialog.FileName);
                UrlBox.Text = openFileDialog.FileName;
                await Player.Play();
            }
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if(Player.IsPlaying)
            {
                Player.Pause();
            }else
            {
                await Player.Play();
            }
        }

        private async void Button_Click_2(object sender, RoutedEventArgs e)
        {
            await Player.Stop();
        }

        private async void Button_Click_3(object sender, RoutedEventArgs e)
        {
            if (Uri.IsWellFormedUriString(UrlBox.Text, UriKind.Absolute))
                await Player.OpenAsync(UrlBox.Text);
            else
                MessageBox.Show("不是一个有效的URL");
        }
    }
}
