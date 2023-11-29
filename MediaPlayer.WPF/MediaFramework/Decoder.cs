using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using MediaPlayer.Extension;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.MediaFramework
{
    public abstract class Decoder:IDisposable
    {
        protected CodecContext? codecContext;
        private CancellationTokenSource? runToken;//任务token
        private StateMachine stateMachine;
        private bool disposedValue;
        //private BlockingCollection<Packet> CachingPackets = new BlockingCollection<Packet>(200);
        private BlockingCollection<Frame> CachingFrames = new BlockingCollection<Frame>(5);
        internal bool IsStop { get;private set; } = true;

        public MediaStream MediaStream { get; }
        //public int PacketCount => CachingPackets.Count;
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
            MediaStream = inStream; 
            codecContext = new CodecContext(Codec.FindDecoderById(MediaStream.Codecpar!.CodecId));
            codecContext.FillParameters(MediaStream.Codecpar);
            //codecContext.Framerate = new AVRational(inStream.AvgFrameRate.Num, inStream.AvgFrameRate.Den);
            codecContext.Open();
            //ClearCaching();

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
        protected virtual void UpdateCurTimestampFromFrame(Frame frame)
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
        //public void AddPacket(Packet packet)
        //{
        //    CachingPackets.Add(packet);
        //}
        //public void AddPacketCompleted()
        //{
        //    CachingPackets.CompleteAdding();
        //}
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
            codecContext?.FlushBuffers();
            for (int i = 0; i < CachingFrames.Count; i++)
                CachingFrames.Take().Free();
        }
        /// <summary>
        /// 将包读取并存入播放缓存，并指示是否已缓存
        /// </summary>
        /// <param name="p"></param>
        internal bool ReadPacketToCaching(Packet p)
        {
            bool isAdd = false;
            try
            {
                foreach (var frame in DecodePacket(p))
                {
                    AddFrame(frame.Clone());
                    isAdd = true;
                }
                return isAdd;

            }
            catch (Exception)
            {
                //codecContext!.FlushBuffers();
                return isAdd;
            }
            
        }
        /// <summary>
        /// 运行解码缓存帧
        /// </summary>
        //internal void RunDecode()
        //{
        //    runToken = new CancellationTokenSource();
        //    var token= runToken.Token;
        //    Task.Run(() =>
        //    {
        //        IsStop = false;
        //        while (true)
        //        {
        //            if (codecContext == null) return;
        //            if (stateMachine.MediaStatus == MediaStatus.Pause|| CachingPackets == null|| CachingFrames.Count==CachingFrames.BoundedCapacity)
        //            {
        //                continue;
        //            }
        //            if (token.IsCancellationRequested)
        //            {
        //                IsStop =true;
        //                break;
        //            }
        //            lock (CachingPackets)
        //            {
        //                if (CachingPackets.IsCompleted)
        //                {
        //                    foreach (var frame in DecodePacket(null))
        //                    {
        //                        AddFrame(frame.Clone());
        //                    }
        //                    AddFrameCompleted();
        //                    break;
        //                }
        //                else if(CachingPackets.Count>0)
        //                {
        //                    Packet p = CachingPackets.Take();
        //                    foreach (var frame in DecodePacket(p))
        //                    {
        //                        AddFrame(frame.Clone());
        //                    }
        //                    p.Unref();
        //                }
        //            }
        //        }
        //    });
        //}
        public int SearchFrameIndex(long timestamp)
        {
            return MediaStream.SearchTimestamp(timestamp,AVSEEK_FLAG.Any);
        }

        public long SerchFrameTimestamp(int index)
        {
            return MediaStream.SearchIndex(index).Timestamp;
        }
        public void Stop()
        {
            runToken?.CancelAfter(10);
        }
        public Frame CreateFrame() => codecContext==null?throw new Exception():codecContext.CreateFrame();
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    codecContext?.Dispose();
                    codecContext = null;
                    //CachingPackets.Dispose();
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