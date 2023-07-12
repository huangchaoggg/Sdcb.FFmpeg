using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using NAudio.Wave.SampleProviders;
using Sdcb.FFmpeg.Utils;

using SoundTouch.Net.NAudioSupport;

namespace MediaPlayer.MediaFramework
{
    public class Player:INotifyPropertyChanged,IDisposable
    {
        private string? uri;
        
        private MediaContainer mediaContainer;
        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<Frame>? ReadFrameEvent;
        public event EventHandler? OpenEvent;

        private Timer timer;
        public Player()
        {
            mediaContainer = new MediaContainer();
            mediaContainer.ReadFrameEvent += MediaContainer_ReadFrameEvent;
            timer = new Timer(UpdatePosition);
            timer.Change(0, 10);
        }

        private void MediaContainer_ReadFrameEvent(object? sender, Frame e)
        {
            ReadFrameEvent?.Invoke(sender, e);
        }

        /// <summary>
        /// 设置值
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="field"></param>
        /// <param name="value"></param>
        /// <param name="propertyName"></param>
        public void Set<T>(ref T field, T value,[CallerMemberName] string propertyName = "")
        {
            if((field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        public void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public string? Uri { get => uri; private set => Set(ref uri,value); }

        public bool HasVideo { get=> mediaContainer.HasVideo; }
        public bool HasAudio { get => mediaContainer.HasAudio; }
        public AudioDecoder? AudioDecoder { get => mediaContainer.AudioDecoder; }
        public VideoDecoder? VideoDecoder { get => mediaContainer.VideoDecoder; }
        public MediaStatus Statu { get => mediaContainer.Statu; }

        public bool IsPlaying { get => mediaContainer.IsPlaying; }//是否正在播放

        private long position=0;
        public long Position { 
            get => position; 
            set
            {
                if (value < 0) return;
                Set(ref position, value);
                mediaContainer.CurTime = value;
            }
        }
        private long duration = 0;
        public long Duration { get => duration; set => Set(ref duration, value); }
        public double Height { get => mediaContainer.VideoDecoder?.Height??0; }
        public double Width { get=> mediaContainer?.VideoDecoder?.Width ?? 0; }
        /// <summary>
        /// 初始值为1
        /// </summary>
        public float Volume
        {
            get => mediaContainer.Volume;
            set
            {
                mediaContainer.Volume = value;
                OnPropertyChanged();
            }
        }
        /// <summary>
        /// 初始值为1
        /// </summary>
        public float SpeedRatio
        {
            get => mediaContainer.SpeedRatio;
            set
            {
                mediaContainer.SpeedRatio = value;
                OnPropertyChanged();
            }
        }
        public async Task OpenAsync(string uri)
        {
            Uri = uri;
            await Stop();
            await mediaContainer.OpenAsync(uri);
            Duration = mediaContainer.Duration;
            OpenEvent?.Invoke(this,EventArgs.Empty);
        }
        public async Task OpenPlayAsync(string uri)
        {
            await OpenAsync(uri);
            Play();
        }
        /// <summary>
        /// 重播
        /// </summary>
        /// <returns></returns>
        public async Task RepeatPlayerAsync()
        {
            if (!string.IsNullOrEmpty(Uri))
            {
                await OpenAsync(Uri);
                Play();
            }
        }
        public void Play()
        {
            mediaContainer.Play();
        }       
        
        
        public async ValueTask Stop()
        {
            await mediaContainer.Stop();
            Position = 0;
            Duration = 0;
        }
        public void Pause()
        {
            mediaContainer.Pause();
        }

        public void PreviewFramePrev()
        {
            Pause();
            mediaContainer.PreviewFramePrev();
        }
        public void PreviewFrameNext()
        {
            Pause();
            mediaContainer.PreviewFrameNext();
        }

        public Frame? GetVideoFrame(int index)
        {
            Pause();
            return mediaContainer.FindVideoFrame(index);
        }
        public void Dispose()
        {
            mediaContainer.Dispose();
            
        }

        private void UpdatePosition(object? state)
        {
            Set(ref position, mediaContainer.CurTime,nameof(Position));
        }
    }
}
