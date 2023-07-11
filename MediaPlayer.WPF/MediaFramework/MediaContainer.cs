using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
                Pause();
                stateMachine.CurTime = value;
                long oldCurTime = value;
                Task.Delay(200).ContinueWith(r =>
                {
                    if (oldCurTime == stateMachine.CurTime)
                        SeekFrame();
                });
                //SeekFrame(value);
            }
        }
        public MediaStatus Statu { get => stateMachine.MediaStatus; }

        public bool IsPlaying { get; private set; } = false;//是否正在播放

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
            iOContext = IOContext.Open(uri);
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
            stateMachine.MediaStatus = MediaStatus.Prepare;
            openToken = new CancellationTokenSource();
            await Task.Run(() =>
            {
                try
                {
                    formatContext = FormatContext.OpenInputIO(context); //
                    MediaStream? videoStream = formatContext.FindBestStreamOrNull(AVMediaType.Video);
                    MediaStream? audioStream = formatContext.FindBestStreamOrNull(AVMediaType.Audio);
                    VideoDecoder = videoStream != null ? VideoDecoder.Create(videoStream.Value, stateMachine) : null;
                    AudioDecoder = audioStream != null ? AudioDecoder.Create(audioStream.Value, stateMachine) : null;
                    if (HasAudio)
                    {
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
                    formatContext.LoadStreamInfo();
                    Run();
                }
                catch (Exception e)
                {
                    Debugger.Log(1, "错误", e.Message);
                    stateMachine.MediaStatus = MediaStatus.Stop;
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
        
        public async ValueTask Play()
        {
            if (stateMachine.MediaStatus == MediaStatus.Playing) return;
            if (stateMachine.MediaStatus == MediaStatus.Pause)
            {
                stateMachine.MediaStatus = MediaStatus.Playing;
                return;
            }
            else if (!string.IsNullOrEmpty(Uri)&& stateMachine.MediaStatus==MediaStatus.Stop)
            {
               await OpenAsync(Uri);
            }
        }
        
        public async Task Stop()
        {
            if (stateMachine.MediaStatus == MediaStatus.Stop) return;
            stateMachine.MediaStatus = MediaStatus.Stop;
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
        }
        public async void Pause()
        {
            stateMachine.MediaStatus = MediaStatus.Pause;
            while (IsPlaying)
            {
                await Task.Delay(30);
            }
            waveOut.Pause();
        }

        public void PreviewFramePrev()
        {
            Pause();
        }
        public void PreviewFrameNext()
        {
            Pause();

        }
        public void PreviewFrame(long timestamp)
        {
            Pause();
            if (!HasVideo) return;
            formatContext!.SeekFrame(timestamp);
            Packet? packet = ReadNextVidoPacket();
            if (packet != null)
            {
                VideoDecoder!.DecodePacket(packet);
            }

        }
        private void Run()
        {
            if (stateMachine.MediaStatus != MediaStatus.Prepare) return;
            stateMachine.MediaStatus = MediaStatus.Pause;
            playToken = new CancellationTokenSource();
            RunReadPackets();
            VideoDecoder?.RunDecode();
            AudioDecoder?.RunDecode();
            playerThread = new Thread(new ParameterizedThreadStart(RunDecodeThread));
            playerThread.IsBackground = true;
            playerThread.Start(playToken.Token);
        }
        /// <summary>
        /// 开始读取包
        /// </summary>
        private void RunReadPackets()
        {
            
            if (formatContext == null) return;
            Task.Run(() =>
            {
                bool audioSeek = false,videoseek=false;
                foreach (var packet in formatContext!.ReadPackets())
                {
                    if (stateMachine.MediaStatus == MediaStatus.Pause|| stateMachine.MediaStatus == MediaStatus.Prepare)
                    {
                        IsPlaying = false;
                        do
                        {
                            Thread.Sleep(30);
                        } while (stateMachine.MediaStatus == MediaStatus.Pause);
                    }
                    else if (stateMachine.MediaStatus == MediaStatus.Stop)
                    {
                        IsPlaying = false;
                        break;
                    }
                    else
                        IsPlaying = true;
                    if (isSeek)
                    {
                        audioSeek = true;
                        videoseek = true;
                        isSeek = false;
                    }
                    if (packet.StreamIndex == VideoDecoder?.SteamIndex)
                    {
                        VideoDecoder.AddPacket(packet.Clone());
                        if (videoseek)
                        {
                            VideoDecoder.Cur_Timestamp = VideoDecoder.GetCurTimestamp(packet.Pts);
                            videoseek = false;
                        }
                    }
                    else if (packet.StreamIndex == AudioDecoder?.SteamIndex)
                    {
                        AudioDecoder.AddPacket(packet.Clone());
                        if (audioSeek)
                        {
                            AudioDecoder.Cur_Timestamp = AudioDecoder.GetCurTimestamp(packet.Pts);
                            audioSeek = false;
                        }
                    }
                }
                VideoDecoder?.AddPacketCompleted();
                AudioDecoder?.AddPacketCompleted();
            });
        }
        /// <summary>
        /// 播放流线程
        /// </summary>
        /// <param name="state"></param>
        private async void RunDecodeThread(object? state)
        {
            if (state == null) return;
            CancellationToken token = (CancellationToken)state!;
            stateMachine.MediaStatus = MediaStatus.Playing;
            while(true)
            {
                if (stateMachine.MediaStatus == MediaStatus.Pause)
                {
                    IsPlaying = false;
                    Thread.Sleep(30);
                    continue;
                }
                else if (token.IsCancellationRequested)
                {
                    IsPlaying = false;
                    stateMachine.MediaStatus = MediaStatus.Stop;
                    return;
                }
                else if(stateMachine.MediaStatus==MediaStatus.Playing)
                {
                    IsPlaying = true;
                }

                Frame? frame=null;
                if (HasAudio && HasVideo)
                {

                    if (AudioDecoder!.IsCompleted && VideoDecoder!.IsCompleted) break;

                    var toer= stateMachine.Synchronization(AudioDecoder!.Cur_Timestamp,VideoDecoder!.Cur_Timestamp);
                    //根据时间戳选择要拿取的包
                    if ((toer >=0&& !VideoDecoder.IsCompleted)||(AudioDecoder.IsCompleted&& !VideoDecoder.IsCompleted))
                    {
                        frame = GetVideoFrame();
                    }
                    else if((toer<=0&&!AudioDecoder.IsCompleted)||(VideoDecoder.IsCompleted&& !AudioDecoder.IsCompleted))
                    {
                        frame = GetAudioFrame();
                    }
                    
                }else if (HasVideo)
                {
                    if (VideoDecoder!.IsCompleted) break;
                    frame = GetVideoFrame();
                }else if (HasAudio)
                {
                    if(AudioDecoder!.IsCompleted) break;
                    frame = GetAudioFrame();
                }
                if (frame == null)
                {
                    IsPlaying = false;
                    stateMachine.MediaStatus = MediaStatus.Prepare;
                    continue;
                }
                else if(stateMachine.MediaStatus == MediaStatus.Prepare)
                {
                    IsPlaying = true;
                    stateMachine.MediaStatus = MediaStatus.Playing;
                }

                ReadFrameEvent?.Invoke(this, frame.Clone());
                if (frame.SampleRate > 0)
                {
                    foreach (var packet in frame.ConverToPcm16Packet(audioEncoder!))
                    {
                        AudioStream?.Write(packet.Data.ToArray());
                    }
                    if (waveOut.PlaybackState != PlaybackState.Playing)
                        waveOut.Play();
                }
                frame.Unref();
            }
            //stateMachine.MediaStatus = MediaStatus.Stop;
            await Stop();
        }
        private Frame? GetAudioFrame()
        {
            Frame? frame = AudioDecoder!.ReadNextFrame();
            float timestamp = AudioDecoder.Cur_Timestamp  - stateMachine.CurTime;
            if (Math.Abs(timestamp) > StateMachine.ToleranceAudioTime / SpeedRatio)
            {
                return null;//丢弃超时的包
            }
            else if (timestamp > 0)
            {
                Thread.Sleep((int)timestamp);
            }
            return frame;
        }
        private Frame? GetVideoFrame()
        {
            Frame? frame = VideoDecoder!.ReadNextFrame();

            float timestamp = VideoDecoder.Cur_Timestamp  - stateMachine.CurTime ;

            if (Math.Abs(timestamp) > StateMachine.BehindTime)
            {
                return null;
            }
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

                if (packet.StreamIndex == VideoDecoder!.SteamIndex)
                {
                    return packet;
                }
                else
                {
                    packet.Unref();
                }
            }
        }
        private object SeekFrameLock = new object();
        private void SeekFrame()
        {
            lock(SeekFrameLock)
            {
                if (stateMachine.MediaStatus == MediaStatus.Prepare) return;

                if (stateMachine.MediaStatus == MediaStatus.Stop)//如果播放已完成则重新开始
                {
                    Play().GetAwaiter().GetResult();
                }
                if (stateMachine.MediaStatus == MediaStatus.Stop) return;
                formatContext?.SeekFrame(stateMachine.CurTime * 1000);
                isSeek = true;
                while (AudioDecoder?.PacketCount > 0 || VideoDecoder?.PacketCount > 0)
                {
                    AudioDecoder?.ClearCaching();
                    VideoDecoder?.ClearCaching();
                    AudioStream?.Clear();
                    Thread.Sleep(10);
                }
                stateMachine.MediaStatus = MediaStatus.Prepare;
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
