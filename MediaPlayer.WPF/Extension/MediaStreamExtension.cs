using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.Extension
{
    public static class MediaStreamExtension
    {
        /// <summary>
        /// <see cref="av_index_search_timestamp(AVStream*, long, int)"/>
        /// </summary>
        public unsafe static int SearchTimestamp(this MediaStream stream,long timestamp, AVSEEK_FLAG flags = AVSEEK_FLAG.Backward)
            => av_index_search_timestamp(stream, timestamp, (int)flags);
        /// <summary>
        /// <see cref="avformat_index_get_entry(AVStream*, int)"/>
        /// </summary>
        public unsafe static IndexEntity SearchIndex(this MediaStream stream, int index) => IndexEntity.FromNative(avformat_index_get_entry(stream, index));
    }
}
