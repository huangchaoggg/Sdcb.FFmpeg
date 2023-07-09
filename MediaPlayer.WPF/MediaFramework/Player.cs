using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Sdcb.FFmpeg.Utils;

namespace MediaPlayer.MediaFramework
{
    public class Player:INotifyPropertyChanged,IDisposable
    {
        private string? uri;
        
        private MediaContainer mediaContainer;
        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<Frame>? ReadFrameEvent;
        public event EventHandler? OpenEvent;
        public Player()
        {
            mediaContainer = new MediaContainer();
            mediaContainer.ReadFrameEvent += MediaContainer_ReadFrameEvent;
        }

        private void MediaContainer_ReadFrameEvent(object? sender, Frame? e)
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

        public string? Uri { get => uri; private set => Set(ref uri,value); }

        public bool HasVideo { get=> mediaContainer.HasVideo; }
        public bool HasAudio { get => mediaContainer.HasAudio; }
        public MediaStatus Statu { get => mediaContainer.Statu; }

        private long postion=0;
        public long Postion { 
            get => mediaContainer.CurTime; 
            set
            {
                Set(ref postion, value);
            }
        }
        private long duration = 0;
        public long Duration { get => duration; set => Set(ref duration, value); }
        public double Height { get => mediaContainer.VideoDecoder?.Height??0; }
        public double Width { get=> mediaContainer?.VideoDecoder?.Width ?? 0; }

        public void Open(string uri)
        {
            Uri = uri;
            Stop();
            mediaContainer.OpenDecode(uri);
            OpenEvent?.Invoke(this,EventArgs.Empty);
        }
        
        public void Play()
        {
            mediaContainer.Play();
        }       
        
        
        public void Stop()
        {
            mediaContainer.Stop();
            Set(ref postion,0,nameof(Postion));
            Dispose();

        }
        public void Pause()
        {
            mediaContainer.Pause();
        }

        public void PreviewFramePrev()
        {
            Pause();
        }
        public void PreviewFrameNext()
        {
            Pause();

        }
        
        public void Dispose()
        {
            mediaContainer.Dispose();
            
        }
    }
}
