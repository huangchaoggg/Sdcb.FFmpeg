using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaPlayer.Extension;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

using SoundTouch.Net.NAudioSupport;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.MediaFramework
{
    public class VideoDecoder: Decoder
    {
        private VideoDecoder(MediaStream inStream,StateMachine stateMachine) : base(inStream,stateMachine)
        {
            RefreshFPS(inStream);
        }
        /// <summary>
        /// 固定帧率模式下不会被系统刷新
        /// </summary>
        public double FPS
        {
            get;
            set;
        }
        //是否固定帧率播放
        public bool IsFixFps { get; set; }
        public double Height => codecContext==null?0:codecContext.Height;
        public double Width=> codecContext==null?0:codecContext.Width;
        public static VideoDecoder Create(MediaStream inAudioStream, StateMachine stateMachine) => new VideoDecoder(inAudioStream, stateMachine);

        public long GetCurTimestampFromIndex(int codedPictureNumber)
        {
            return MediaStream.SearchIndex(codedPictureNumber).Timestamp;
        }

        public void RefreshFPS(MediaStream stream)
        {
            FPS = av_q2d(stream.AvgFrameRate) > 0 ? av_q2d(stream.AvgFrameRate) : av_q2d(stream.RFrameRate);
            if (FPS <= 0)
            {
                FPS = av_q2d(codecContext!.Framerate) > 0 ? av_q2d(codecContext.Framerate) : 0;
            }
            if (FPS <= 0)
            {
                FPS=((stream.Duration / stream.NbFrames * av_q2d(stream.TimeBase)) * 1000);
            }
        }
        protected override void UpdateCurTimestampFromFrame(Frame frame)
        {
            if ((frame.Pts == frame.PktDts && frame.Pts < 0)||IsFixFps)
            {
                frame.Pts= (long)(frame.CodedPictureNumber *(1000/(FPS==0?60:FPS)));
                frame.PktDts = frame.Pts;
                Cur_Timestamp = frame.Pts;
            }
            else
            {
                base.UpdateCurTimestampFromFrame(frame);
            }
        }
    }
}
