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
        public Player Player { get; } = new Player();
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if(openFileDialog.ShowDialog()==true )
            {
                Player.Open(openFileDialog.FileName);
                Player.Play();
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if(Player.Statu== MediaStatus.Playing)
            {
                Player.Pause();
            }else if (Player.Statu == MediaStatus.Pause)
            {
                Player.Play();
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            Player.Stop();
        }
    }
}
