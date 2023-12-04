using System;
using System.Globalization;
using System.Windows.Data;

namespace MediaPlayer.WPF.Demo
{
    public class TicketToTimeSpanConverter : IValueConverter
    {
        public static TicketToTimeSpanConverter Current = new TicketToTimeSpanConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long tick)
                return TimeSpan.FromMilliseconds(tick).ToString("d\\:hh\\:mm\\:ss");
            return TimeSpan.Zero;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string ss)
                return TimeSpan.Parse(ss);
            return 0;
        }
    }
}
