using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using MediaPlayer.Extension;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;

namespace MediaPlayer.MediaFramework
{
    public class MediaContainer:IAsyncDisposable
    {
        private FormatContext? formatContext = null;
        private AudioStream? AudioStream;
        private StateMachine stateMachine;

        public MediaContainer()
        {
            this.stateMachine = new StateMachine();
        }

        private CodecContext? audioEncoder;
        private WaveOut waveOut = new WaveOut();
        private VolumeSampleProvider? volumeSampleProvider;
        private Thread? playerThread;//播放器解码线程
        private CancellationTokenSource? playToken;
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
                stateMachine.CurTime = value;
                //这里设置位置信息
            }
        }
        public MediaStatus Statu { get => stateMachine.MediaStatus; }

        /// <summary>
        /// 读取到帧时触发
        /// </summary>
        public event EventHandler<Frame?>? ReadFrameEvent;
        public void OpenDecode(string uri)
        {
            Uri = uri;
            stateMachine.MediaStatus = MediaStatus.Prepare;
            try
            {
                formatContext = FormatContext.OpenInputUrl(uri);
                formatContext.LoadStreamInfo();
                MediaStream? videoStream = formatContext.FindBestStreamOrNull(Sdcb.FFmpeg.Raw.AVMediaType.Video);
                MediaStream? audioStream = formatContext.FindBestStreamOrNull(Sdcb.FFmpeg.Raw.AVMediaType.Audio);
                VideoDecoder = videoStream != null ? VideoDecoder.Create(videoStream.Value, stateMachine) : null;
                AudioDecoder = audioStream != null ? AudioDecoder.Create(audioStream.Value, stateMachine) : null;
                if (HasAudio)
                {
                    CreateAudioProvider(AudioDecoder!.Rate, AudioDecoder.Bits, AudioDecoder.Channels);
                    audioEncoder = new(Codec.FindEncoderById(AVCodecID.PcmS16le))
                    {
                        ChLayout = GetChannelLayout(AudioStream!.WaveFormat.Channels),
                        SampleFormat = AVSampleFormat.S16,
                        SampleRate = AudioStream!.WaveFormat.SampleRate,
                        BitsPerCodedSample = AudioStream!.WaveFormat.BitsPerSample,
                    };
                    audioEncoder.TimeBase = new AVRational(1, audioEncoder.SampleRate);
                    audioEncoder.Open(Codec.FindEncoderById(AVCodecID.PcmS16le));
                }
            }
            catch(Exception)
            {
                stateMachine.MediaStatus = MediaStatus.Stop;
            }
        }
        private void CreateAudioProvider(int rate, int bits, int channels)
        {
            WaveFormat format = new WaveFormat(rate, bits, channels);
            AudioStream = new AudioStream(format);
            volumeSampleProvider = new VolumeSampleProvider(AudioStream.ToSampleProvider());
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
                stateMachine.MediaStatus = MediaStatus.Playing;
                return;
            }
            else if (!string.IsNullOrEmpty(Uri)&& stateMachine.MediaStatus==MediaStatus.Stop)
            {
                OpenDecode(Uri);
            }
            if (stateMachine.MediaStatus != MediaStatus.Prepare) return;

            playToken = new CancellationTokenSource();
            RunReadPackets();
            VideoDecoder?.RunDecode();
            AudioDecoder?.RunDecode();
            playerThread = new Thread(new ParameterizedThreadStart(RunDecodeThread));
            playerThread.IsBackground = true;
            playerThread.Start(playToken.Token);
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
            await DisposeAsync();
        }
        public void Pause()
        {
            stateMachine.MediaStatus = MediaStatus.Pause;
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
        /// <summary>
        /// 开始读取包
        /// </summary>
        private void RunReadPackets()
        {
            
            if (formatContext == null) return;
            Task.Run(() =>
            {
                foreach (var packet in formatContext!.ReadPackets())
                {

                    if (stateMachine.MediaStatus == MediaStatus.Pause)
                    {
                        do
                        {
                            Thread.Sleep(30);
                        } while (stateMachine.MediaStatus == MediaStatus.Pause);
                    }
                    else if (stateMachine.MediaStatus == MediaStatus.Stop)
                    {
                        break;
                    }
                    if (packet.StreamIndex == VideoDecoder?.SteamIndex)
                        VideoDecoder.CachingPackets.Add(packet.Clone());
                    else if (packet.StreamIndex == AudioDecoder!.SteamIndex)
                        AudioDecoder.CachingPackets.Add(packet.Clone());
                }
                VideoDecoder?.CachingPackets.CompleteAdding();
                AudioDecoder?.CachingPackets.CompleteAdding();
            });
        }
        private async void RunDecodeThread(object? state)
        {
            if (state == null) return;
            CancellationToken token = (CancellationToken)state!;
            stateMachine.MediaStatus = MediaStatus.Playing;
            while(true)
            {
                if(stateMachine.MediaStatus== MediaStatus.Pause)
                {
                    Thread.Sleep(30);
                    continue;
                }
                else if (token.IsCancellationRequested)
                {
                    stateMachine.MediaStatus = MediaStatus.Stop;
                    return;
                }
                Frame? frame=null;
                if (HasAudio && HasVideo)
                {
                    //index = ++index % 2;
                    var toer= stateMachine.Synchronization(AudioDecoder!.Cur_Timestamp,VideoDecoder!.Cur_Timestamp);//获取同步时间
                    //if(AudioDecoder.Cur_Timestamp>)
                    //根据时间戳选择要拿取的包
                    if ((toer >=0&& !VideoDecoder.IsCompleted)||(AudioDecoder.IsCompleted&& !VideoDecoder.IsCompleted))
                    {
                        frame = GetVideoFrame();
                    }
                    else if((toer<=0&&!AudioDecoder.IsCompleted)||(VideoDecoder.IsCompleted&& !AudioDecoder.IsCompleted))
                    {
                        frame = GetAudioFrame();
                    }
                    
                    if (AudioDecoder.IsCompleted && VideoDecoder.IsCompleted) break;
                }else if (HasVideo)
                {
                    frame = GetVideoFrame();
                    if (VideoDecoder!.IsCompleted) break;
                }else if (HasAudio)
                {
                    frame = GetAudioFrame();
                    if(AudioDecoder!.IsCompleted) break;
                }
                if (frame == null) continue;
                
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
            ///播放完后释放资源
            await Stop();
        }
        private Frame? GetAudioFrame()
        {
            Frame? frame = AudioDecoder!.ReadNextFrame();
            if (stateMachine.CurTime - AudioDecoder.Cur_Timestamp > StateMachine.ToleranceAudioTime)
            {
                return null;//丢弃超时的包
            }
            else if (AudioDecoder.Cur_Timestamp - stateMachine.CurTime > 0)
            {
                Thread.Sleep((int)(AudioDecoder.Cur_Timestamp - stateMachine.CurTime));
            }
            return frame;
        }
        private Frame? GetVideoFrame()
        {
            Frame? frame = VideoDecoder!.ReadNextFrame();
            if (stateMachine.CurTime - VideoDecoder!.Cur_Timestamp > StateMachine.BehindTime)
            {
                return null;
            }
            else if (VideoDecoder.Cur_Timestamp - stateMachine.CurTime > 0)
            {
                Thread.Sleep((int)(VideoDecoder.Cur_Timestamp - stateMachine.CurTime));
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

        public async ValueTask DisposeAsync()
        {
            if(VideoDecoder?.IsStop==false)
                await VideoDecoder.Stop();
            if(AudioDecoder?.IsStop==false)
                await AudioDecoder.Stop();
            formatContext?.Dispose();
            audioEncoder?.Dispose();
            AudioStream?.Clear();
        }
    }
}
