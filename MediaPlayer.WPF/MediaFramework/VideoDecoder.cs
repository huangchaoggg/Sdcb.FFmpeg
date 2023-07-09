using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.MediaFramework
{
    public class VideoDecoder: Decoder
    {
        private VideoDecoder(MediaStream inStream, StateMachine stateMachine) : base(inStream,stateMachine)
        {
            RefreshFPS(inStream);
        }
        public double FPS
        {
            get;
            private set;
        }
        public double Height => codecContext.Height;
        public double Width=> codecContext.Width;
        public static VideoDecoder Create(MediaStream inAudioStream, StateMachine stateMachine) => new VideoDecoder(inAudioStream, stateMachine);

        private void RefreshFPS(MediaStream stream)
        {
            FPS = av_q2d(stream.AvgFrameRate) > 0 ? av_q2d(stream.AvgFrameRate) : av_q2d(stream.RFrameRate);
            if (FPS <= 0)
            {
                FPS = av_q2d(codecContext.Framerate) > 0 ? av_q2d(codecContext.Framerate) : 0;
            }
            if (FPS <= 0)
            {
                FPS=((stream.Duration / stream.NbFrames * av_q2d(stream.TimeBase)) * 1000);
            }
        }
       
    }
}
