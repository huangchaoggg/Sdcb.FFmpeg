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
        protected CodecContext? codecContext;
        private CancellationTokenSource? runToken;//任务token
        private StateMachine stateMachine;
        private bool disposedValue;
        private BlockingCollection<Packet> CachingPackets = new BlockingCollection<Packet>(200);
        private BlockingCollection<Frame> CachingFrames = new BlockingCollection<Frame>(5);
        internal bool IsStop { get;private set; } = true;

        public MediaStream MediaStream { get; }
        public int PacketCount => CachingPackets.Count;
        public int SteamIndex { get => MediaStream.Index; }
        /// <summary>
        /// 是否读取完成
        /// </summary>
        public bool IsCompleted { get; private set; } = false;
        
        public long Cur_Timestamp { get; set; }
        public bool HasFrame => CachingFrames.Count > 0;

        protected Decoder(MediaStream inStream, StateMachine stateMachine)
        {
            this.stateMachine = stateMachine;
            ClearCaching();
            MediaStream = inStream;
            codecContext = new CodecContext(Codec.FindDecoderById(inStream.Codecpar!.CodecId));
            codecContext.FillParameters(inStream.Codecpar);
            codecContext.Open();
        }

        internal IEnumerable<Frame> DecodePacket(Packet? packet)
        {
            if (codecContext == null) yield return new Frame();
            using Frame dstFrame = new Frame();
            foreach (var frame in codecContext!.DecodePacket(packet, dstFrame))
            {
                yield return frame;
            }
        }
        protected void UpdateCurTimestampFromFrame(Frame frame)
        {
            if (frame == null) return;
            long cur_Timestamp = 0;
            if (frame.PktDts != AV_NOPTS_VALUE)
            {
                cur_Timestamp = frame.PktDts;
            }
            else if (frame.Pts != AV_NOPTS_VALUE)
            {
                cur_Timestamp = frame.Pts;
            }
            Cur_Timestamp= GetCurTimestamp(cur_Timestamp);
        }
        public long GetCurTimestamp(long pts)
        {
            return (long)((pts * av_q2d(MediaStream.TimeBase)) * 1000);
        }
        /// <summary>
        /// 读取下一帧
        /// </summary>
        /// <returns></returns>
        internal Frame? ReadNextFrame()
        {
            if(CachingFrames==null) return null;
            if (CachingFrames.IsCompleted)
            {
                IsCompleted = true;
                return null;
            }
            if(CachingFrames.Count == 0)
                return null;

            var f= CachingFrames.Take();
            if (f == null) return null;
            UpdateCurTimestampFromFrame(f);
            return f;
        }
        public void AddPacket(Packet packet)
        {
            CachingPackets.Add(packet);
        }
        public void AddPacketCompleted()
        {
            CachingPackets.CompleteAdding();
        }
        public void AddFrame(Frame frame)
        {
            CachingFrames.Add(frame);
        }
        public void AddFrameCompleted()
        {
            CachingFrames.CompleteAdding();
        }

        internal void ClearCaching()
        {
            for (int i = 0; i < CachingPackets.Count; i++)
                CachingPackets.Take().Free();
            for (int i = 0; i < CachingFrames.Count; i++)
                CachingFrames.Take().Free();
        }
        /// <summary>
        /// 运行解码缓存帧
        /// </summary>
        internal void RunDecode()
        {
            runToken = new CancellationTokenSource();
            var token= runToken.Token;
            Task.Run(() =>
            {
                IsStop = false;
                while (true)
                {
                    if (codecContext == null) return;
                    if (stateMachine.MediaStatus == MediaStatus.Pause|| CachingPackets == null|| CachingFrames.Count==CachingFrames.BoundedCapacity)
                    {
                        Thread.Sleep(30);
                        continue;
                    }
                    if (token.IsCancellationRequested)
                    {
                        IsStop =true;
                        break;
                    }
                    lock (CachingPackets)
                    {
                        if (CachingPackets.IsCompleted)
                        {
                            foreach (var frame in DecodePacket(null))
                            {
                                AddFrame(frame.Clone());
                            }
                            AddFrameCompleted();
                            break;
                        }
                        else if(CachingPackets.Count>0)
                        {
                            Packet p = CachingPackets.Take();
                            foreach (var frame in DecodePacket(p))
                            {
                                AddFrame(frame.Clone());
                            }
                            p.Unref();
                        }
                    }
                }
            });
        }


        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    runToken?.CancelAfter(10);
                   
                    codecContext?.Dispose();
                    codecContext = null;
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