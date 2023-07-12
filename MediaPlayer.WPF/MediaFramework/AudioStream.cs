using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NAudio.Wave;

namespace MediaPlayer.MediaFramework
{
    internal class AudioStream : IWaveProvider
    {
        List<byte> arrayBufferWriter = new List<byte>(short.MaxValue);
        //List<byte> byteList = new List<byte>(short.MaxValue);
        public WaveFormat WaveFormat { get; init; }

        public int Position { get; private set; }
        public AudioStream(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;
        }
        public void Write(ReadOnlySpan<byte> bytes)
        {
            //arrayBufferWriter.Write(bytes);
            arrayBufferWriter.AddRange(bytes.ToArray());
        }
        public int Read(byte[] buffer, int offset, int count)
        {
            int c = count;
            if (arrayBufferWriter.Count< count)
                c = arrayBufferWriter.Count;
            Array.Copy(arrayBufferWriter.GetRange(0, c).ToArray(), 0, buffer, offset, c);
            if(arrayBufferWriter.Count>=c)
                arrayBufferWriter.RemoveRange(0, c);
            //Position += c;
            return c;
        }

        public void Clear()
        {
            arrayBufferWriter.Clear();
        }
    }
}
