using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private CancellationTokenSource? runToken;//任务token
        private StateMachine stateMachine;
        private bool disposedValue;
        internal bool IsStop { get;private set; } = true;

        public MediaStream MediaStream { get; }

        public int SteamIndex { get => MediaStream.Index; }
        /// <summary>
        /// 是否读取完成
        /// </summary>
        public bool IsCompleted { get; private set; } = false;
        public BlockingCollection<Packet> CachingPackets { get; private set; } = new BlockingCollection<Packet>(50);
        public BlockingCollection<Frame> CachingFrames { get; private set; } = new BlockingCollection<Frame>(5);
        public long Cur_Timestamp { get; set; }
        protected Decoder(MediaStream inStream, StateMachine stateMachine)
        {
            this.stateMachine = stateMachine;
            MediaStream = inStream;
            codecContext = new CodecContext(Codec.FindDecoderById(inStream.Codecpar!.CodecId));
            codecContext.FillParameters(inStream.Codecpar);
            codecContext.Open();
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
            runToken = new CancellationTokenSource();
            Task.Run(() =>
            {
               var token= runToken.Token;
                IsStop = false;
                while (true)
                {
                    if (stateMachine.MediaStatus == MediaStatus.Pause)
                    {
                        Thread.Sleep(30);
                        continue;
                    }
                    if (token.IsCancellationRequested)
                    {
                        IsStop =true;
                        break;
                    }
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
        internal async ValueTask Stop()
        {
            await Task.Run(() =>
            {
                runToken?.Cancel();
                int i = 0;
                do
                {
                    Thread.Sleep(10);
                    i++;
                } while (stateMachine.MediaStatus != MediaStatus.Stop && i < 5);
            });
            Dispose();
            
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    codecContext.Dispose();
                    CachingPackets.Dispose();
                    CachingFrames.Dispose();
                    // TODO: 释放托管状态(托管对象)
                }

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~Decoder()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}