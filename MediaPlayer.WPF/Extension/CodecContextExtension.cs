using Sdcb.FFmpeg.Codecs;

using static Sdcb.FFmpeg.Raw.ffmpeg;
namespace MediaPlayer.Extension
{
    public static class CodecContextExtension
    {
        /// <summary>
        /// <see cref="avcodec_flush_buffers"/>
        /// </summary>
        public unsafe static void FlushBuffers(this CodecContext codecContext) => avcodec_flush_buffers(codecContext);
    }
}
