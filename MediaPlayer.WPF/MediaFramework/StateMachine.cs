using System;
using System.Diagnostics;

namespace MediaPlayer.MediaFramework
{
    public enum MediaStatus
    {
        /// <summary>
        /// 准备状态
        /// </summary>
        Prepare,
        /// <summary>
        /// 播放状态
        /// </summary>
        Playing,
        /// <summary>
        /// 暂停状态
        /// </summary>
        Pause,
        /// <summary>
        /// 停止状态
        /// </summary>
        Stop
    }
    public class StateMachine
    {

        private Stopwatch stopwatch = new Stopwatch();
        private MediaStatus mediaStatus;

        public const long ToleranceAudioTime = 1500;//音频最多不能超过视频的1500毫秒
        public const long ToleranceVideoTime = 150;//视频最多不能超过音频的150毫秒
        /// <summary>
        /// 落后时间多不能超过音频的150毫秒
        /// </summary>
        public const long BehindTime = 150;

        internal float SpeedRatio 
        { 
            get => speedRatio;
            set
            {
                speedRatio = value;
            }
        }
        private long oldElapsedMilliseconds=0;
        private long curTime=0;
        private float speedRatio = 1;

        public long CurTime {
            get
            {
                long cur = stopwatch.ElapsedMilliseconds;
                curTime += (long)((cur - oldElapsedMilliseconds) * SpeedRatio);
                oldElapsedMilliseconds = cur;
                return curTime;
            }
            set
            {
                curTime = value;
            }
        } 
        public MediaStatus MediaStatus 
        { 
            get => mediaStatus;
            set
            {
                if (value == mediaStatus) return;
                if (value == MediaStatus.Playing)
                {
                    oldElapsedMilliseconds = 0;
                    stopwatch.Restart();
                }
                else
                {
                    stopwatch.Stop();
                }
                mediaStatus = value;
            }
        }

        public StateMachine()
        {
            MediaStatus = MediaStatus.Stop;
        }
        /// <summary>
        /// 大于0则视频同步到音频，小于0则音频同步到视频
        /// </summary>
        /// <param name="audio_cur_Timestamp"></param>
        /// <param name="video_cur_Timestamp"></param>
        /// <returns></returns>
        internal int Synchronization(long audio_cur_Timestamp,long video_cur_Timestamp)
        {
            if (video_cur_Timestamp - audio_cur_Timestamp > ToleranceVideoTime)
                return -1;
            if (audio_cur_Timestamp - video_cur_Timestamp > ToleranceAudioTime)
                return 1;
            if(audio_cur_Timestamp > video_cur_Timestamp)
            {
                return 1;
            }
            else if(audio_cur_Timestamp<video_cur_Timestamp)
            {
                return -1;
            }
            return 0;
            
        }
    }
}
