﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.MediaFramework
{
    public abstract class Decoder:IDisposable
    {
        protected CodecContext codecContext;
        private MediaStream inStream;

        public MediaStream MediaStream { get; }
        public StateMachine StateMachine { get; }

        public int SteamIndex { get => MediaStream.Index; }
        /// <summary>
        /// 是否读取完成
        /// </summary>
        public bool IsCompleted { get; private set; } = false;
        public BlockingCollection<Packet> CachingPackets { get; } = new BlockingCollection<Packet>(50);
        public BlockingCollection<Frame> CachingFrames { get; } = new BlockingCollection<Frame>(5);
        public long Cur_Timestamp { get; set; }
        protected Decoder(MediaStream inStream,StateMachine stateMachine)
        {
            MediaStream = inStream;
            StateMachine = stateMachine;
            codecContext = new CodecContext(Codec.FindDecoderById(inStream.Codecpar!.CodecId));
            codecContext.FillParameters(inStream.Codecpar);
            codecContext.Open(); 
        }

        public void Dispose()
        {
            codecContext.Dispose();
        }

        internal IEnumerable<Frame> DecodePacket(Packet? packet)
        {
            using Frame dstFrame = new Frame();
            foreach (var frame in codecContext.DecodePacket(packet, dstFrame))
            {
                yield return frame;
            }
        }
        protected void UpdateCurTimestampFromFrame(Frame frame)
        {
            if (frame == null) return;
            Cur_Timestamp = 0;
            if (frame.PktDts != AV_NOPTS_VALUE)
            {
                Cur_Timestamp = frame.PktDts;
            }
            else if (frame.Pts != AV_NOPTS_VALUE)
            {
                Cur_Timestamp = frame.Pts;
            }
            Cur_Timestamp=(long)((Cur_Timestamp * av_q2d(MediaStream.TimeBase)) * 1000);
        }
        /// <summary>
        /// 读取下一帧
        /// </summary>
        /// <returns></returns>
        internal Frame? ReadNextFrame()
        {
            if(CachingFrames.Count==0) return null;
            var f= CachingFrames.Take();
            if (f == null) return null;
            UpdateCurTimestampFromFrame(f);
            if (CachingFrames.IsCompleted)
                IsCompleted = true;
            return f;
        }
        /// <summary>
        /// 运行解码缓存帧
        /// </summary>
        internal void RunDecode()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (CachingPackets.IsCompleted)
                    {
                        foreach (var frame in DecodePacket(null))
                        {
                            CachingFrames.Add(frame.Clone());
                        }
                        CachingFrames.CompleteAdding();
                        break;
                    }
                    else
                    {
                        Packet p = CachingPackets.Take();
                        foreach (var frame in DecodePacket(p))
                        {
                            CachingFrames.Add(frame.Clone());
                        }
                        p.Unref();
                    }
                }
            });
        }
    }
}