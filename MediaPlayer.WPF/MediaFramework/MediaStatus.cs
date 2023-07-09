using System;
using System.Diagnostics;

namespace MediaPlayer.MediaFramework
{
    public enum MediaStatus
    {
        Playing,
        Pause,
        Stop
    }
    public class StateMachine
    {

        private Stopwatch stopwatch = new Stopwatch();
        private MediaStatus mediaStatus;

        public const long ToleranceAudioTime = 500;//音频最多不能超过视频的500毫秒
        public const long ToleranceVideoTime = 50;//视频最多不能超过音频的50毫秒
        /// <summary>
        /// 落后时间多不能超过音频的150毫秒
        /// </summary>
        public const long BehindTime = 150;

        private long oldElapsedMilliseconds=0;
        private long curTime=0;

        public long CurTime {
            get
            {
                long cur = stopwatch.ElapsedMilliseconds;
                curTime += cur - oldElapsedMilliseconds;
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
                    oldElapsedMilliseconds = 0;
                    stopwatch.Stop();
                }
                mediaStatus = value;
            }
        }
        /// <summary>
        /// 大于0则视频同步到音频，小于0则音频同步到视频
        /// </summary>
        /// <param name="audio_cur_Timestamp"></param>
        /// <param name="video_cur_Timestamp"></param>
        /// <returns></returns>
        internal int Synchronization(long audio_cur_Timestamp,long video_cur_Timestamp)
        {
            if(audio_cur_Timestamp > video_cur_Timestamp)
            {
                return 1;
            }
            else if(audio_cur_Timestamp<video_cur_Timestamp)
            {
                return -1;
            }
            return 0;
            //if (audio_cur_Timestamp <= CurTime && video_cur_Timestamp <= CurTime) return 0;

            //if (audio_cur_Timestamp - video_cur_Timestamp > ToleranceAudioTime)
            //{
            //    var cur = (audio_cur_Timestamp - video_cur_Timestamp - ToleranceAudioTime);
            //    return (int)cur;
            //}
            //else if (video_cur_Timestamp-audio_cur_Timestamp>ToleranceVideoTime)
            //{
            //    var cur = (video_cur_Timestamp - audio_cur_Timestamp - ToleranceVideoTime);
            //    return (int)-cur;
            //}
            //return 0;
        }
    }
}
