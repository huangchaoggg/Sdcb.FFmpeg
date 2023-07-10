using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;

namespace MediaPlayer.MediaFramework
{
    public class AudioDecoder:Decoder
    {
        private AudioDecoder(MediaStream inStream, StateMachine stateMachine) : base(inStream, stateMachine)
        {
        }

        public int Channels { get => codecContext==null?0:codecContext.ChLayout.nb_channels; }
        public int Bits { get => codecContext == null ? 0 : codecContext.BitsPerCodedSample; }
        public int Rate { get => (int)(codecContext == null ? 0 : codecContext.SampleRate); }

        public static AudioDecoder Create(MediaStream inAudioStream, StateMachine stateMachine) => new AudioDecoder(inAudioStream, stateMachine);


    }
}
