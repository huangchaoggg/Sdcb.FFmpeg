using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swresamples;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace MediaPlayer.Extension
{
    public static class FrameExtension
    {
        //static VideoFrameConverter converter = new VideoFrameConverter();
        //static SampleConverter sampleConverter = new();
        /// <summary>
        /// 将帧转换为argb32格式的bitmap
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public static Bitmap GetBitmap(this VideoFrameConverter converter, Frame frame)
        {
            Frame dstFrame = Frame.CreateVideo(frame.Width, frame.Height, AVPixelFormat.Argb);
            if(frame.Format != (int)AVPixelFormat.Rgb24)
            {
                converter.ConvertFrame(frame, dstFrame);
            }
            else
            {
                dstFrame = frame;
            }
            Bitmap bitmap = new Bitmap(dstFrame.Width, dstFrame.Height);
            BitmapData data= bitmap.LockBits(new Rectangle(0, 0, dstFrame.Width, dstFrame.Height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            Marshal.Copy(dstFrame.ToImageBuffer(), 0, data.Scan0, dstFrame.Width * 3);
            bitmap.UnlockBits(data);
            return bitmap;
        }
        public static BitmapSource? GetBitmapSource(this VideoFrameConverter converter, Frame frame)
        {
            if (frame == null|| frame.Width<1) return null;
            Frame dstFrame = Frame.CreateVideo(frame.Width, frame.Height, AVPixelFormat.Rgb24);
            if (frame.Format != (int)AVPixelFormat.Rgb24)
            {
                converter.ConvertFrame(frame, dstFrame);
            }
            else
            {
                dstFrame = frame;
            }
            BitmapSource bs= BitmapSource.Create(dstFrame.Width, dstFrame.Height, 96, 96, PixelFormats.Rgb24, null, dstFrame.ToImageBuffer(), dstFrame.Width * 3);
            dstFrame.Free();
            return bs;
        }
        public static byte[] GetBitmapBuffer(this VideoFrameConverter converter, Frame frame)
        {
            if (frame == null || frame.Width < 1) return new byte[0];
            Frame dstFrame = Frame.CreateVideo(frame.Width, frame.Height, AVPixelFormat.Rgba64le);
            if (frame.Format != (int)AVPixelFormat.Rgba64le)
            {
                converter.ConvertFrame(frame, dstFrame);
            }
            else
            {
                dstFrame = frame;
            }
            return dstFrame.ToImageBuffer();
        }
        public static IEnumerable<Packet> ConverToPcmPacket(this SampleConverter sampleConverter, Frame frame, CodecContext audioEncoder)
        {
            if (frame.SampleRate > 0)
            {
                Frame dstFrame = audioEncoder.CreateFrame();
                if (!sampleConverter.Initialized)
                {
                    sampleConverter.Options.Set("in_chlayout", frame.ChLayout, default(AV_OPT_SEARCH));
                    sampleConverter.Options.Set("in_sample_rate", frame.SampleRate, default(AV_OPT_SEARCH));
                    sampleConverter.Options.Set("in_sample_fmt", (AVSampleFormat)frame.Format, default(AV_OPT_SEARCH));
                    sampleConverter.Options.Set("out_chlayout", dstFrame.ChLayout, default(AV_OPT_SEARCH));
                    sampleConverter.Options.Set("out_sample_rate", dstFrame.SampleRate, default(AV_OPT_SEARCH));
                    sampleConverter.Options.Set("out_sample_fmt", (AVSampleFormat)dstFrame.Format, default(AV_OPT_SEARCH));
                    sampleConverter.Initialize();
                }
                int destSampleCount = (int)av_rescale_rnd(sampleConverter.GetDelay(frame.SampleRate) + frame.NbSamples, frame.SampleRate, dstFrame.SampleRate, AVRounding.PassMinmax);
                dstFrame.MakeWritable();
                int converted = sampleConverter.Convert(dstFrame.Data, destSampleCount, frame.Data, frame.NbSamples);
                dstFrame.Pts = frame.Pts;
                frame.Unref();
                dstFrame.NbSamples = converted;
                Packet dstPacket = new Packet();
                foreach (var packet in audioEncoder.EncodeFrame(dstFrame, dstPacket))
                {
                    yield return packet;
                }
            }
        }
    }
}
