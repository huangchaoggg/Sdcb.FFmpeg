using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Documents;

using MediaPlayer.Extension;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;

using SoundTouch.Net.NAudioSupport;

namespace MediaPlayer.MediaFramework
{
    public class MediaContainer:IDisposable
    {

        public MediaContainer()
        {
            this.stateMachine = new StateMachine();
        }
        private bool isSeek = false;
        private float volume=1;

        private FormatContext? formatContext = null;
        private AudioStream? AudioStream;
        private StateMachine stateMachine;
        private IOContext? iOContext;
        private CodecContext? audioEncoder;
        private WaveOutEvent waveOut = new WaveOutEvent();
        private VolumeSampleProvider? volumeSampleProvider;
        private SoundTouchWaveProvider? soundTouchWaveProvider;
        private Thread? playerThread;//播放器解码线程
        private CancellationTokenSource? playToken;
        private CancellationTokenSource? openToken;
        public string? Uri { get; set; }

        public AudioDecoder? AudioDecoder { get; private set; }
        public VideoDecoder? VideoDecoder { get; private set; }
        public bool HasVideo { get => VideoDecoder != null; }
        public bool HasAudio { get => AudioDecoder != null; }
        public long Duration { get => (formatContext!=null&& formatContext.Duration >0) ? formatContext.Duration/1000:0; }

        public long CurTime
        {
            get => stateMachine.CurTime;
            set
            {
                if (stateMachine.CurTime == value) return;
                stateMachine.CurTime = value;
                if (stateMachine.MediaStatus == MediaStatus.Stop) return;
                stateMachine.Prepare();
                long oldCurTime = value;
                Task.Delay(200).ContinueWith(r =>
                {
                    if (oldCurTime == stateMachine.CurTime)
                        SeekFrame();
                });
                //Pause();
                //SeekFrame(value);
            }
        }
        public MediaStatus Statu { get => stateMachine.MediaStatus; }

        public bool IsPlaying { get => Statu == MediaStatus.Playing; }//是否正在播放

        /// <summary>
        /// 初始值为1
        /// </summary>
        public float Volume
        {
            get => volume;
            set
            {
                volume = value;
                if(volumeSampleProvider!=null)
                    volumeSampleProvider.Volume = value;
            }
        }
        /// <summary>
        /// 初始值为1
        /// </summary>
        public float SpeedRatio
        {
            get => stateMachine.SpeedRatio;
            set
            {
                stateMachine.SpeedRatio=value;
                if (soundTouchWaveProvider != null)
                {
                    soundTouchWaveProvider.Rate = value;
                }
            }
        }
        /// <summary>
        /// 读取到帧时触发
        /// </summary>
        public event EventHandler<Frame>? ReadFrameEvent;
        
        public Task OpenAsync(string uri)
        {
            Uri = uri;
            try
            {
                iOContext = IOContext.Open(uri);
            }
            catch (Exception e)
            {
                Debugger.Log(1, "错误", e.Message);
                return Task.CompletedTask;
            }
            return OpenAsync(iOContext);
        }
        public Task OpenAsync(Stream stream)
        {
            iOContext=IOContext.ReadStream(stream);
            return OpenAsync(iOContext);
        }
        private async Task OpenAsync(IOContext context)
        {
            if (openToken != null && !openToken.IsCancellationRequested) 
            { 
                openToken.Cancel();
            }
            if (formatContext != null)
                await Stop();
            stateMachine.Prepare();
            openToken = new CancellationTokenSource();
            await Task.Run(() =>
            {
                try
                {
                    formatContext = FormatContext.OpenInputIO(context); //
                    formatContext.LoadStreamInfo();
                    MediaStream? videoStream = formatContext.FindBestStreamOrNull(AVMediaType.Video);
                    MediaStream? audioStream = formatContext.FindBestStreamOrNull(AVMediaType.Audio);
                    if (videoStream != null)
                    {
                        VideoDecoder =  VideoDecoder.Create(videoStream.Value, stateMachine);
                    }
                    if(audioStream != null)
                    {
                        AudioDecoder = AudioDecoder.Create(audioStream.Value, stateMachine);

                        CreateAudioProvider(AudioDecoder!.Rate, 2);
                        var codeId = AVCodecID.PcmS16le;
                        audioEncoder = new(Codec.FindEncoderById(codeId))
                        {
                            ChLayout = GetChannelLayout(2),
                            SampleFormat = AVSampleFormat.S16,
                            SampleRate = AudioStream!.WaveFormat.SampleRate,
                            BitsPerCodedSample = AudioStream!.WaveFormat.BitsPerSample,
                        };
                        audioEncoder.TimeBase = new AVRational(1, audioEncoder.SampleRate);
                        audioEncoder.Open(Codec.FindEncoderById(codeId));
                    }
                    if (Duration > 0)
                    {
                        stateMachine.MaxDuration = Duration;
                    }
                    if (!HasAudio && !HasVideo)
                    {
                        throw new ArgumentNullException("不支持的视频!");
                    }
                    Run();
                }
                catch (Exception e)
                {
                    Debugger.Log(1, "错误", e.Message);
                    stateMachine.Stop();
                    throw;
                }
            }, openToken.Token);
        }
        private void CreateAudioProvider(int rate, int channels)
        {
            WaveFormat format = new WaveFormat(rate, 16, channels);
            AudioStream = new AudioStream(format);
            Wave16ToFloatProvider wave16ToFloatProvider = new Wave16ToFloatProvider(AudioStream);
            soundTouchWaveProvider = new SoundTouchWaveProvider(wave16ToFloatProvider);
            volumeSampleProvider = new VolumeSampleProvider(soundTouchWaveProvider.ToSampleProvider());
            soundTouchWaveProvider.Rate = SpeedRatio;
            volumeSampleProvider.Volume = volume;
            waveOut.Init(volumeSampleProvider);
        }
        private unsafe AVChannelLayout GetChannelLayout(int nb_channels)
        {
            AVChannelLayout layout = new AVChannelLayout();
            ffmpeg.av_channel_layout_default(&layout, nb_channels);
            return layout;
        }
        
        public void Play()
        {
            if (stateMachine.MediaStatus == MediaStatus.Playing) return;
            if (stateMachine.MediaStatus == MediaStatus.Pause)
            {
                stateMachine.Playing();
                return;
            }
        }
        
        public async Task Stop()
        {
            if (stateMachine.MediaStatus == MediaStatus.Stop) return;
            await Task.Run(() =>
            {
                playToken?.Cancel();
                int i = 0;
                do
                {
                    Thread.Sleep(10);
                    i++;
                } while (stateMachine.MediaStatus != MediaStatus.Stop && i < 5);
            });
            waveOut.Stop();
            AudioDecoder?.Stop();
            VideoDecoder?.Stop();
            stateMachine.Stop();
        }
        public async void Pause()
        {
            stateMachine.Pause();
            while (IsPlaying)
            {
                await Task.Delay(30);
            }
            waveOut.Stop();
        }

        public void PreviewFramePrev()
        {
            ThrowIsNotVideoTrack();
            double timestamp = stateMachine.CurTime - (1000 / VideoDecoder!.FPS);
            if (timestamp < 0)
                timestamp = 0;

            var frame = FindVideoFrame((long)timestamp,false);

            if (frame != null)

                ReadFrameEvent?.Invoke(this, frame);
        }
        public void PreviewFrameNext()
        {
            ThrowIsNotVideoTrack();
            double timestamp = stateMachine.CurTime +(1000 / VideoDecoder!.FPS);
            var frame= FindVideoFrame((long)timestamp);
            if(frame!=null)
                ReadFrameEvent?.Invoke(this, frame!);
        }
        
        public Frame? FindVideoFrame(int index)
        {
            ThrowIsNotVideoTrack();
            double interval = (1000 / VideoDecoder!.FPS);
            double time = interval* index;

            return FindVideoFrame((long)time);
        }
        /// <summary>
        /// 查找下一帧，isNext为ture向下查找，false向上查找
        /// </summary>
        /// <param name="timestamp"></param>
        /// <param name="isNext"></param>
        /// <returns></returns>
        private Frame? FindVideoFrame(long timestamp, bool isNext = true, int retry = 0)
        {
            if (retry > 5) return null;
            ThrowIsNotVideoTrack();
            //将播放缓存清空，以免恢复播放时出现冗余数据
            AudioDecoder?.ClearCaching();
            VideoDecoder?.ClearCaching();
            AudioStream?.Clear();
            //**********************************************

            long findtime = timestamp;
            if (timestamp > 1000)
            {
                findtime -= 1000;
            }
            else
            {
                findtime = 0;
            }
            double interval = (1000 / VideoDecoder!.FPS);
            formatContext!.SeekFrame(findtime*1000);
            while (true)
            {
                Packet? packet = ReadNextVidoPacket();
                if (packet != null)
                {
                    foreach (var f in VideoDecoder!.DecodePacket(packet))
                    {
                        var t = VideoDecoder.GetCurTimestamp(f.Pts);
                        //if (Math.Abs(timestamp - t) <= interval)
                        if ((isNext&&t-timestamp>=0&& t - timestamp <= interval)||(!isNext&& t - timestamp <= 0 && timestamp-t <= interval))
                        {
                            stateMachine.CurTime = isNext?(t > timestamp?t:timestamp): (t < timestamp?t:timestamp);
                            return f.Clone();
                        }if ((isNext && (t - timestamp) > interval) || (!isNext && t>timestamp ))
                        {
                            return FindVideoFrame(timestamp,isNext,retry++);
                        }

                    }
                }
            }
        }
        private void ThrowIsNotVideoTrack()
        {
            if (!HasVideo)
                throw new NotSupportedException("当前媒体没有视频流");
        }
        private void Run()
        {
            if (stateMachine.MediaStatus != MediaStatus.Prepare) return;
            playToken = new CancellationTokenSource();
            //RunReadPackets();
            playerThread = new Thread(new ParameterizedThreadStart(RunDecodeThread));
            playerThread.IsBackground = true;
            playerThread.Start(playToken.Token);
        }
        /// <summary>
        /// 播放流线程
        /// </summary>
        /// <param name="state"></param>
        private async void RunDecodeThread(object? state)
        {
            if (state == null) return;
            CancellationToken token = (CancellationToken)state!;
            stateMachine.Pause();
            Packet packet = new();
            while (true)
            {
                
                if (stateMachine.MediaStatus == MediaStatus.Pause)
                {
                    do
                    {
                        Thread.Sleep(30);

                    } while (stateMachine.MediaStatus == MediaStatus.Pause);
                    //continue;
                }
                else if (token.IsCancellationRequested
                    ||((AudioDecoder==null?true:AudioDecoder.IsCompleted)&& (VideoDecoder==null?true:VideoDecoder.IsCompleted))
                    || stateMachine.MediaStatus==MediaStatus.Stop)
                {
                    break;
                }
                try
                {
                    lock (SeekFrameLock)
                    {
                        //if(!(AudioDecoder == null ? false : AudioDecoder.HasFrame) && !(VideoDecoder == null ? false : VideoDecoder.HasFrame))
                        //{
                        //    stateMachine.Prepare();
                        //}
                        //stateMachine.Prepare();
                        CodecResult result = formatContext!.ReadFrame(packet);//stateMachine.ReadPacket(formatContext!,packet);//formatContext!.ReadFrame(packet);
                        if (result == CodecResult.EOF) 
                            break;

                        Frame? frame = null;
                        if (packet.StreamIndex == VideoDecoder?.SteamIndex)
                        {
                            bool c = VideoDecoder.ReadPacketToCaching(packet.Clone());
                            if ((!c && !VideoDecoder.HasFrame))
                            {
                                stateMachine.Prepare();
                                continue;
                            }
                            frame = GetVideoFrame();
                        }
                        else if (packet.StreamIndex == AudioDecoder?.SteamIndex)
                        {
                            bool c = AudioDecoder.ReadPacketToCaching(packet.Clone());
                            if ((!c && !AudioDecoder.HasFrame))
                            {
                                stateMachine.Prepare();
                                continue;
                            }
                            frame = GetAudioFrame();
                        }
                        if (frame == null)
                            continue;

                        if (isSeek)
                        {
                            stateMachine.Playing();
                            isSeek = false;
                        }else if(stateMachine.MediaStatus == MediaStatus.Prepare)
                        {
                            stateMachine.Playing();

                        }
                        ReadFrameEvent?.Invoke(this, frame.Clone());
                        if (frame.SampleRate > 0)
                        {
                            foreach (var packet2 in frame.ConverToPcm16Packet(audioEncoder!))
                            {
                                AudioStream?.Write(packet2.Data.ToArray());
                            }
                            if (waveOut.PlaybackState != PlaybackState.Playing)
                                waveOut.Play();
                        }
                        frame.Free();
                        
                    }
                }
                finally
                {
                    packet.Unref();
                }
            }

           await Stop();
        }
        //private void RunReadPackets()
        //{
        //    if (formatContext == null) return;
        //    Task.Run(() =>
        //    {
        //        Packet packet = new Packet();
        //        while(true)
        //        {
        //            if (stateMachine.MediaStatus == MediaStatus.Stop) break;
        //            while (stateMachine.MediaStatus == MediaStatus.Pause)
        //            {
        //                Thread.Sleep(10);
        //            }
        //            lock (SeekFrameLock)
        //            {
        //                CodecResult result = formatContext!.ReadFrame(packet);
        //                if (result == CodecResult.EOF) break;

        //                if (packet.StreamIndex == VideoDecoder?.SteamIndex)
        //                {
        //                    bool c = VideoDecoder.ReadPacketToCaching(packet.Clone());
        //                    //if ((!c && !VideoDecoder.HasFrame))
        //                    //{
        //                    //    stateMachine.Prepare();
        //                    //    continue;
        //                    //}
        //                    //frame = GetVideoFrame();
        //                }
        //                else if (packet.StreamIndex == AudioDecoder?.SteamIndex)
        //                {
        //                    bool c = AudioDecoder.ReadPacketToCaching(packet.Clone());
        //                    //if ((!c && !AudioDecoder.HasFrame))
        //                    //{
        //                    //    stateMachine.Prepare();
        //                    //    continue;
        //                    //}
        //                    //frame = GetAudioFrame();
        //                }
        //                packet.Unref();

        //            }
        //        }
        //        AudioDecoder?.AddFrameCompleted();
        //        VideoDecoder?.AddFrameCompleted();

        //    });
        //}
        private Frame? GetAudioFrame()
        {
            Frame? frame = AudioDecoder!.ReadNextFrame();
            if (isSeek)
            {
                stateMachine.CurTime = AudioDecoder.Cur_Timestamp;
            }
            float timestamp = AudioDecoder.Cur_Timestamp- stateMachine.CurTime;

            if (timestamp <0&&timestamp<(-StateMachine.ToleranceAudioTime))
            {
                stateMachine.Prepare();
                return null;//丢弃超时的包
            }if (!isSeek && timestamp > StateMachine.ToleranceAudioTime)
                return null;
            else if (timestamp > 0)
            {
                Thread.Sleep((int)timestamp);
            }
            
            return frame;
        }
        private Frame? GetVideoFrame()
        {
            Frame? frame = VideoDecoder!.ReadNextFrame();
            if (isSeek)
            {
                stateMachine.CurTime = VideoDecoder.Cur_Timestamp;
            }
            float timestamp = VideoDecoder.Cur_Timestamp- stateMachine.CurTime;
            if (timestamp<0&&timestamp< (-StateMachine.ToleranceVideoTime))
            {
                stateMachine.Prepare();
                return null;
            }
            if(!isSeek&&timestamp> StateMachine.ToleranceVideoTime)
                return null;
            else if (timestamp > 1)
            {
                Thread.Sleep((int)timestamp);
            }
            return frame;
        }
        private Packet? ReadNextVidoPacket()
        {
            Packet packet = new();
            while (true)
            {
                CodecResult result = formatContext!.ReadFrame(packet);
                if (result == CodecResult.EOF) return null;

                if (packet.StreamIndex == VideoDecoder?.SteamIndex)
                {
                    if (packet.Pts > 0)
                    {
                        VideoDecoder.Cur_Timestamp = VideoDecoder.GetCurTimestamp(packet.Pts);
                        return packet;

                    }
                }
                else if(packet.StreamIndex==AudioDecoder?.SteamIndex)
                {
                    if(packet.Pts>0)
                        AudioDecoder.Cur_Timestamp = AudioDecoder.GetCurTimestamp(packet.Pts);
                }
                packet.Unref();
            }
        }
        private object SeekFrameLock = new object();
        private void SeekFrame()
        {
            lock (SeekFrameLock)
            {
                if (isSeek) return;
                if (stateMachine.MediaStatus == MediaStatus.Stop) return;
                isSeek = true;
                stateMachine.Prepare();
                formatContext?.SeekFrame(stateMachine.CurTime*1000);
                AudioDecoder?.ClearCaching();
                VideoDecoder?.ClearCaching();
                AudioStream?.Clear();
            }
        }
        public void Dispose()
        {
            volumeSampleProvider = null;
            soundTouchWaveProvider = null;
            //formatContext?.Free();
            VideoDecoder?.Dispose();
            VideoDecoder = null;
            AudioDecoder?.Dispose();
            AudioDecoder = null;
            formatContext?.Dispose();
            formatContext = null;
            audioEncoder?.Dispose();
            audioEncoder = null;
            AudioStream?.Clear();
            AudioStream = null;
        }
    }
}
